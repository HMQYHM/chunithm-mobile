using System;
using System.Linq;
using ChunithmMobile.Runtime;
using ChunithmMobile.Rendering;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class SkinWidthSelfTest
    {
        public static void BuildVerifiedApk()
        {
            GameplayPauseSelfTest.Run();
            VerifySkinWidths();
            SkinSettingsSelfTest.Run();
            AndroidBuildConfigurator.BuildApk();
        }

        private static void VerifySkinWidths()
        {
            var cache = new NoteSkinUvCache();
            int checkedCases = 0;
            try {
                foreach (string kind in new[] { "tap", "hold", "slide", "flick", "dmg" })
                foreach (int width in Enumerable.Range(1, 16)) {
                    string name = "ntt_" + kind + "_" + width.ToString("00");
                    Mesh source = Resources.Load<GameObject>("OriginalMeshes/" + name).GetComponentInChildren<MeshFilter>().sharedMesh;
                    Vector2[] original = source.uv;
                    foreach (bool padded in new[] { false, true }) {
                        // Same 1024px canvas, either filled completely or a
                        // centered body growing by 64px. Both must show edges.
                        var texture = new Texture2D(1024, 128, TextureFormat.RGBA32, false);
                        try {
                            int left = padded ? (1024 - width * 64) / 2 : 0;
                            int right = 1024 - left;
                            var pixels = new Color32[1024 * 128];
                            for (int y = 0; y < 128; y++)
                                for (int x = left; x < right; x++)
                                    pixels[y * 1024 + x] = new Color32((byte)(x == left ? 255 : 20), 120, (byte)(x == right - 1 ? 255 : 20), 255);
                            texture.SetPixels32(pixels);
                            texture.Apply();
                            Mesh adjusted = cache.Get(source, texture);
                            float min = adjusted.uv.Min(p => p.x), max = adjusted.uv.Max(p => p.x);
                            if (kind == "dmg")
                                Require(ReferenceEquals(adjusted, source), "complex mine mapping changed: " + name);
                            else
                                Require(min <= left / 1024f + .0001f && max >= right / 1024f - .0001f,
                                    "skin body clipped: " + name + " padded=" + padded);
                            Require(adjusted.vertices.SequenceEqual(source.vertices) && adjusted.triangles.SequenceEqual(source.triangles),
                                "skin UV adaptation changed geometry");
                            Require(original.SequenceEqual(source.uv), "source mesh mutated");
                            Require(ReferenceEquals(adjusted, cache.Get(source, texture)), "mesh not cached");
                            checkedCases++;
                        } finally { cache.Clear(); UnityEngine.Object.DestroyImmediate(texture); }
                    }
                }
            } finally { cache.Clear(); }
            Debug.Log($"Skin UV regression passed: {checkedCases} cases, widths 1-16, full canvas/padded bodies, intact edges, unchanged geometry, caching.");
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
