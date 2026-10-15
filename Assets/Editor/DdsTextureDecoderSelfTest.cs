using System;
using System.IO;
using System.Reflection;
using ChunithmMobile.Rendering;
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
                VerifyUploadedOrientation(texture);
                // Upload must not mutate the source-order worker result.
                ExpectPixel(bc1, 0, 0, 255, 0, 0, 255, "source data after upload");
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
            VerifyImageFormatAndBackdropParity();
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
            Debug.Log("DDS texture regression passed: BC1/2/3 colors, alpha, GPU upload orientation, PNG/JPG parity, normal backdrop UVs and rounded-cover readback");
        }

        private static void VerifyUploadedOrientation(Texture texture)
        {
            Texture2D readback = Readback(texture);
            try {
                Color top = readback.GetPixel(1, readback.height - 1);
                Color bottom = readback.GetPixel(1, 0);
                if (top.r < .8f || top.g > .2f || bottom.g < .8f || bottom.r > .2f)
                    throw new Exception("Uploaded jacket is upside down: top=" + top + ", bottom=" + bottom);
            }
            finally { UnityEngine.Object.DestroyImmediate(readback); }
        }

        private static Texture2D Readback(Texture source)
        {
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0);
            RenderTexture previous = RenderTexture.active;
            try {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply();
                return result;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        private static void VerifyImageFormatAndBackdropParity()
        {
            // JPEG chroma subsampling blends tiny one-row fixtures. Use large
            // color bands so the assertion checks orientation, not compression.
            var authored = new Texture2D(16, 16, TextureFormat.RGB24, false);
            var cameraRoot = new GameObject("Jacket orientation regression camera", typeof(Camera));
            Texture2D dds = null, png = null, jpg = null;
            try {
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                    authored.SetPixel(x, y, y < 8 ? Color.green : Color.red);
                authored.Apply();
                dds = DdsTextureDecoder.CreateTexture(DdsTextureDecoder.Decode(BuildBc1()), "orientation DDS");
                png = new Texture2D(2, 2); png.LoadImage(authored.EncodeToPNG());
                jpg = new Texture2D(2, 2); jpg.LoadImage(authored.EncodeToJPG(100));
                var backdrop = cameraRoot.AddComponent<GameplayBackdropRenderer>();
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                if (typeof(GameplayBackdropRenderer).GetField("coverRenderer", instance).GetValue(backdrop) == null)
                    typeof(GameplayBackdropRenderer).GetMethod("Awake", instance).Invoke(backdrop, null);
                foreach (string field in new[] { "coverRenderer", "blurredRenderer", "skinBackgroundRenderer" }) {
                    var renderer = (MeshRenderer)typeof(GameplayBackdropRenderer).GetField(field, instance).GetValue(backdrop);
                    Vector2[] uv = renderer.GetComponent<MeshFilter>().sharedMesh.uv;
                    if (uv[0] != Vector2.zero || uv[1] != Vector2.right || uv[2] != Vector2.one || uv[3] != Vector2.up)
                        throw new Exception(field + " applies a second jacket flip");
                }
                foreach (Texture2D source in new[] { dds, png, jpg }) {
                    VerifyUploadedOrientation(source);
                    var rounded = (Texture2D)typeof(GameplayBackdropRenderer).GetMethod("CreateRoundedCover",
                        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source, 32, .085f });
                    try {
                        Texture2D readback = Readback(rounded);
                        try {
                            Color top = readback.GetPixel(16, 27), bottom = readback.GetPixel(16, 1);
                            if (top.r <= top.g || bottom.g <= bottom.r)
                                throw new Exception("Rounded backdrop changes jacket orientation");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(readback); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(rounded); }
                }
            }
            finally {
                UnityEngine.Object.DestroyImmediate(cameraRoot);
                UnityEngine.Object.DestroyImmediate(authored);
                if (dds != null) UnityEngine.Object.DestroyImmediate(dds);
                if (png != null) UnityEngine.Object.DestroyImmediate(png);
                if (jpg != null) UnityEngine.Object.DestroyImmediate(jpg);
            }
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
