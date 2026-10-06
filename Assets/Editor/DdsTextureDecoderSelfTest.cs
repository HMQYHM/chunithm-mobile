using System;
using System.IO;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Checks the DDS formats and orientation used by imported song jackets.</summary>
    public static class DdsTextureDecoderSelfTest
    {
        private const uint Dxt1 = 0x31545844;
        private const uint Dxt3 = 0x33545844;
        private const uint Dxt5 = 0x35545844;

        /// <summary>Runs CPU decode and main-thread texture upload regression checks.</summary>
        public static void Run()
        {
            DdsTextureData bc1 = DdsTextureDecoder.Decode(BuildBc1());
            ExpectPixel(bc1, 0, 0, 255, 0, 0, 255, "BC1 top row");
            ExpectPixel(bc1, 0, 3, 0, 255, 0, 255, "BC1 bottom row/orientation");

            DdsTextureData bc1Alpha = DdsTextureDecoder.Decode(BuildBc1Alpha());
            ExpectPixel(bc1Alpha, 0, 0, 0, 0, 0, 0, "BC1 transparent selector");

            DdsTextureData bc2 = DdsTextureDecoder.Decode(BuildBc2());
            ExpectPixel(bc2, 0, 0, 255, 0, 0, 0, "BC2 alpha");
            ExpectPixel(bc2, 1, 0, 255, 0, 0, 255, "BC2 opaque pixel");

            DdsTextureData bc3 = DdsTextureDecoder.Decode(BuildBc3());
            ExpectPixel(bc3, 0, 0, 255, 0, 0, 0, "BC3 alpha selector");
            ExpectPixel(bc3, 1, 0, 255, 0, 0, 255, "BC3 opaque pixel");

            Texture2D texture = DdsTextureDecoder.CreateTexture(bc1, "DDS regression");
            try
            {
                if (texture == null || texture.width != 4 || texture.height != 4 || texture.format != TextureFormat.RGBA32)
                    throw new Exception("DDS Unity texture is not portable RGBA32");
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
            string realSample = Environment.GetEnvironmentVariable("CHUNITHM_DDS_SAMPLE");
            if (!string.IsNullOrWhiteSpace(realSample) && File.Exists(realSample))
            {
                DdsTextureData real = DdsTextureDecoder.DecodeFile(realSample);
                if (real.Width <= 0 || real.Height <= 0 || real.Rgba32.Length != real.Width * real.Height * 4)
                    throw new Exception("Real DDS jacket decoded to invalid dimensions");
                Texture2D realTexture = DdsTextureDecoder.CreateTexture(real, Path.GetFileNameWithoutExtension(realSample));
                if (realTexture == null) throw new Exception("Real DDS jacket failed RGBA32 upload");
                UnityEngine.Object.DestroyImmediate(realTexture);
            }
            Debug.Log("DDS texture regression passed: BC1/2/3 colors, alpha, orientation and RGBA32 upload");
        }

        private static byte[] BuildBc1()
        {
            byte[] block = ColorBlock(0xf800, 0x07e0);
            uint selectors = 0;
            for (int pixel = 0; pixel < 16; pixel++)
                selectors |= (uint)(pixel / 4 == 3 ? 1 : 0) << (pixel * 2);
            WriteUInt32(block, 4, selectors);
            return BuildDds(Dxt1, block);
        }

        private static byte[] BuildBc1Alpha()
        {
            byte[] block = ColorBlock(0x0000, 0xffff);
            WriteUInt32(block, 4, 3);
            return BuildDds(Dxt1, block);
        }

        private static byte[] BuildBc2()
        {
            var block = new byte[16];
            for (int index = 0; index < 8; index++) block[index] = 0xff;
            block[0] = 0xf0;
            Buffer.BlockCopy(ColorBlock(0xf800, 0x07e0), 0, block, 8, 8);
            return BuildDds(Dxt3, block);
        }

        private static byte[] BuildBc3()
        {
            var block = new byte[16];
            block[0] = 255;
            block[1] = 0;
            block[2] = 1;
            Buffer.BlockCopy(ColorBlock(0xf800, 0x07e0), 0, block, 8, 8);
            return BuildDds(Dxt5, block);
        }

        private static byte[] ColorBlock(ushort first, ushort second)
        {
            var block = new byte[8];
            block[0] = (byte)first;
            block[1] = (byte)(first >> 8);
            block[2] = (byte)second;
            block[3] = (byte)(second >> 8);
            return block;
        }

        private static byte[] BuildDds(uint fourCc, byte[] block)
        {
            var bytes = new byte[128 + block.Length];
            WriteUInt32(bytes, 0, 0x20534444);
            WriteUInt32(bytes, 4, 124);
            WriteUInt32(bytes, 8, 0x00081007);
            WriteUInt32(bytes, 12, 4);
            WriteUInt32(bytes, 16, 4);
            WriteUInt32(bytes, 20, (uint)block.Length);
            WriteUInt32(bytes, 76, 32);
            WriteUInt32(bytes, 80, 4);
            WriteUInt32(bytes, 84, fourCc);
            WriteUInt32(bytes, 108, 0x1000);
            Buffer.BlockCopy(block, 0, bytes, 128, block.Length);
            return bytes;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void ExpectPixel(DdsTextureData data, int x, int y,
            byte red, byte green, byte blue, byte alpha, string context)
        {
            int offset = (y * data.Width + x) * 4;
            if (data.Rgba32[offset] != red || data.Rgba32[offset + 1] != green ||
                data.Rgba32[offset + 2] != blue || data.Rgba32[offset + 3] != alpha)
                throw new Exception($"{context} mismatch: actual " +
                    $"{data.Rgba32[offset]},{data.Rgba32[offset + 1]},{data.Rgba32[offset + 2]},{data.Rgba32[offset + 3]}");
        }
    }
}
