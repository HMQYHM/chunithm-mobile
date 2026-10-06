using System;
using System.IO;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Owns portable RGBA pixels decoded from a DDS on a worker thread. Unity objects are not
    /// stored here because Texture2D construction belongs to the main thread.
    /// </summary>
    public sealed class DdsTextureData
    {
        /// <summary>Decoded image width in pixels.</summary>
        public int Width { get; }
        /// <summary>Decoded image height in pixels.</summary>
        public int Height { get; }
        /// <summary>Rows of red, green, blue and alpha bytes in the DDS source order.</summary>
        public byte[] Rgba32 { get; }

        internal DdsTextureData(int width, int height, byte[] rgba32)
        {
            Width = width;
            Height = height;
            Rgba32 = rgba32;
        }
    }

    /// <summary>
    /// Decodes imported DDS jackets. iOS/Metal cannot sample DXT textures directly, so BC1/2/3
    /// blocks are expanded to portable RGBA32 before a Unity texture is created.
    /// </summary>
    public static class DdsTextureDecoder
    {
        private const uint DdsMagic = 0x20534444;
        private const uint Dxt1 = 0x31545844;
        private const uint Dxt3 = 0x33545844;
        private const uint Dxt5 = 0x35545844;
        private const uint Dx10 = 0x30315844;
        private const int MaxDimension = 16384;
        private const long MaxPixels = 64L * 1024 * 1024;

        private enum BlockFormat { Bc1, Bc2, Bc3, Rgb }

        /// <summary>Reads and decodes a DDS file without calling Unity APIs; safe for a worker thread.</summary>
        /// <exception cref="IOException">The file cannot be read.</exception>
        /// <exception cref="InvalidDataException">The header, dimensions or pixel payload is unsupported.</exception>
        public static DdsTextureData DecodeFile(string path) => Decode(File.ReadAllBytes(path));

        /// <summary>Decodes DDS bytes to RGBA32 without calling Unity APIs; safe for a worker thread.</summary>
        /// <exception cref="InvalidDataException">The header, dimensions or pixel payload is unsupported.</exception>
        public static DdsTextureData Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 128 || ReadUInt32(bytes, 0) != DdsMagic)
                throw new InvalidDataException("DDS 文件头无效");
            if (ReadUInt32(bytes, 4) != 124 || ReadUInt32(bytes, 76) != 32)
                throw new InvalidDataException("DDS 文件头长度无效");

            int height = ReadInt32(bytes, 12);
            int width = ReadInt32(bytes, 16);
            long pixelCount = (long)width * height;
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension ||
                pixelCount <= 0 || pixelCount > MaxPixels)
                throw new InvalidDataException($"DDS 尺寸不受支持：{width}x{height}");

            int dataOffset = 128;
            uint fourCc = ReadUInt32(bytes, 84);
            BlockFormat format;
            if (fourCc == Dxt1) format = BlockFormat.Bc1;
            else if (fourCc == Dxt3) format = BlockFormat.Bc2;
            else if (fourCc == Dxt5) format = BlockFormat.Bc3;
            else if (fourCc == Dx10)
            {
                if (bytes.Length < 148) throw new InvalidDataException("DDS DX10 文件头不完整");
                uint dxgiFormat = ReadUInt32(bytes, 128);
                dataOffset = 148;
                if (dxgiFormat == 71 || dxgiFormat == 72) format = BlockFormat.Bc1;
                else if (dxgiFormat == 74 || dxgiFormat == 75) format = BlockFormat.Bc2;
                else if (dxgiFormat == 77 || dxgiFormat == 78) format = BlockFormat.Bc3;
                else throw new InvalidDataException($"不支持的 DDS DX10 格式：{dxgiFormat}");
            }
            else if (fourCc == 0 && (ReadUInt32(bytes, 80) & 0x40) != 0)
                format = BlockFormat.Rgb;
            else
                throw new InvalidDataException("只支持 DXT1、DXT3、DXT5、BC1、BC2 和 BC3 封面");

            var rgba = new byte[checked((int)pixelCount * 4)];
            if (format == BlockFormat.Rgb)
                DecodeRgb(bytes, dataOffset, width, height, rgba);
            else
                DecodeBlocks(bytes, dataOffset, width, height, format, rgba);
            return new DdsTextureData(width, height, rgba);
        }

        /// <summary>Uploads decoded pixels into a portable RGBA32 texture on Unity's main thread.</summary>
        /// <remarks>The returned texture is non-readable and owned by the caller.</remarks>
        public static Texture2D CreateTexture(DdsTextureData data, string name)
        {
            if (data == null) return null;
            var texture = new Texture2D(data.Width, data.Height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.LoadRawTextureData(data.Rgba32);
            texture.Apply(false, true);
            return texture;
        }

        private static void DecodeBlocks(byte[] source, int offset, int width, int height, BlockFormat format,
            byte[] destination)
        {
            int blockBytes = format == BlockFormat.Bc1 ? 8 : 16;
            int blocksWide = (width + 3) / 4;
            int blocksHigh = (height + 3) / 4;
            long required = (long)blocksWide * blocksHigh * blockBytes;
            if (offset < 0 || required > source.Length - offset)
                throw new InvalidDataException("DDS 像素数据不完整");

            // Reuse block scratch buffers. A 1024px jacket contains 65,536 blocks;
            // allocating palettes per block causes visible GC spikes while scrolling the list.
            var palette = new byte[16];
            byte[] alpha = format == BlockFormat.Bc1 ? null : new byte[16];
            for (int blockY = 0; blockY < blocksHigh; blockY++)
            for (int blockX = 0; blockX < blocksWide; blockX++)
            {
                int blockOffset = offset + (blockY * blocksWide + blockX) * blockBytes;
                int colorOffset = blockOffset;
                bool forceFourColors = format != BlockFormat.Bc1;
                if (format == BlockFormat.Bc2)
                {
                    DecodeBc2Alpha(source, blockOffset, alpha);
                    colorOffset += 8;
                }
                else if (format == BlockFormat.Bc3)
                {
                    DecodeBc3Alpha(source, blockOffset, alpha);
                    colorOffset += 8;
                }
                DecodeColorBlock(source, colorOffset, blockX, blockY, width, height, destination,
                    forceFourColors, alpha, palette);
            }
        }

        private static void DecodeColorBlock(byte[] source, int offset, int blockX, int blockY,
            int width, int height, byte[] destination, bool forceFourColors, byte[] alpha, byte[] palette)
        {
            ushort color0 = ReadUInt16(source, offset);
            ushort color1 = ReadUInt16(source, offset + 2);
            ExpandRgb565(color0, palette, 0);
            ExpandRgb565(color1, palette, 4);
            palette[3] = palette[7] = 255;
            if (color0 > color1 || forceFourColors)
            {
                Interpolate(palette, 0, 4, palette, 8, 2, 1, 3);
                Interpolate(palette, 0, 4, palette, 12, 1, 2, 3);
                palette[11] = palette[15] = 255;
            }
            else
            {
                Interpolate(palette, 0, 4, palette, 8, 1, 1, 2);
                palette[11] = 255;
                palette[12] = palette[13] = palette[14] = palette[15] = 0;
            }

            uint indices = ReadUInt32(source, offset + 4);
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = blockX * 4 + pixel % 4;
                int y = blockY * 4 + pixel / 4;
                if (x >= width || y >= height) continue;
                int paletteOffset = (int)((indices >> (pixel * 2)) & 3) * 4;
                int target = (y * width + x) * 4;
                destination[target] = palette[paletteOffset];
                destination[target + 1] = palette[paletteOffset + 1];
                destination[target + 2] = palette[paletteOffset + 2];
                destination[target + 3] = alpha == null ? palette[paletteOffset + 3] : alpha[pixel];
            }
        }

        private static void DecodeBc2Alpha(byte[] source, int offset, byte[] alpha)
        {
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int value = (source[offset + pixel / 2] >> ((pixel & 1) * 4)) & 0x0f;
                alpha[pixel] = (byte)(value * 17);
            }
        }

        private static void DecodeBc3Alpha(byte[] source, int offset, byte[] alpha)
        {
            byte alpha0 = source[offset], alpha1 = source[offset + 1];
            var palette = new byte[8];
            palette[0] = alpha0;
            palette[1] = alpha1;
            if (alpha0 > alpha1)
            {
                for (int index = 1; index <= 6; index++)
                    palette[index + 1] = (byte)(((7 - index) * alpha0 + index * alpha1) / 7);
            }
            else
            {
                for (int index = 1; index <= 4; index++)
                    palette[index + 1] = (byte)(((5 - index) * alpha0 + index * alpha1) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }

            ulong indices = 0;
            for (int index = 0; index < 6; index++) indices |= (ulong)source[offset + 2 + index] << (index * 8);
            for (int pixel = 0; pixel < 16; pixel++)
                alpha[pixel] = palette[(int)((indices >> (pixel * 3)) & 7)];
        }

        private static void DecodeRgb(byte[] source, int offset, int width, int height, byte[] destination)
        {
            int bitCount = ReadInt32(source, 88);
            if (bitCount != 24 && bitCount != 32)
                throw new InvalidDataException($"不支持的 DDS RGB 位深：{bitCount}");
            int bytesPerPixel = bitCount / 8;
            int minimumPitch = checked(width * bytesPerPixel);
            int headerPitch = ReadInt32(source, 20);
            int pitch = headerPitch >= minimumPitch ? headerPitch : minimumPitch;
            if ((long)pitch * height > source.Length - offset)
                throw new InvalidDataException("DDS RGB 像素数据不完整");
            uint redMask = ReadUInt32(source, 92), greenMask = ReadUInt32(source, 96);
            uint blueMask = ReadUInt32(source, 100), alphaMask = ReadUInt32(source, 104);
            if (redMask == 0 || greenMask == 0 || blueMask == 0)
                throw new InvalidDataException("DDS RGB 色彩通道无效");

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = offset + y * pitch + x * bytesPerPixel;
                uint value = 0;
                for (int index = 0; index < bytesPerPixel; index++) value |= (uint)source[pixelOffset + index] << (index * 8);
                int target = (y * width + x) * 4;
                destination[target] = ExtractChannel(value, redMask, 0);
                destination[target + 1] = ExtractChannel(value, greenMask, 0);
                destination[target + 2] = ExtractChannel(value, blueMask, 0);
                destination[target + 3] = ExtractChannel(value, alphaMask, 255);
            }
        }

        private static byte ExtractChannel(uint value, uint mask, byte fallback)
        {
            if (mask == 0) return fallback;
            int shift = 0;
            while (((mask >> shift) & 1) == 0) shift++;
            uint normalizedMask = mask >> shift;
            uint component = (value & mask) >> shift;
            return (byte)((component * 255 + normalizedMask / 2) / normalizedMask);
        }

        private static void ExpandRgb565(ushort value, byte[] palette, int offset)
        {
            int red = (value >> 11) & 31, green = (value >> 5) & 63, blue = value & 31;
            palette[offset] = (byte)((red << 3) | (red >> 2));
            palette[offset + 1] = (byte)((green << 2) | (green >> 4));
            palette[offset + 2] = (byte)((blue << 3) | (blue >> 2));
        }

        private static void Interpolate(byte[] source, int first, int second, byte[] destination, int target,
            int firstWeight, int secondWeight, int divisor)
        {
            for (int channel = 0; channel < 3; channel++)
                destination[target + channel] = (byte)((source[first + channel] * firstWeight +
                    source[second + channel] * secondWeight) / divisor);
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset > bytes.Length - 2) throw new InvalidDataException("DDS 数据意外结束");
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("DDS 数据意外结束");
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        private static int ReadInt32(byte[] bytes, int offset) => unchecked((int)ReadUInt32(bytes, offset));
    }
}
