using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // Gold EX heads must retain their base/UVs without the imported upright rim.
    // Checks all widths, HXD head reuse, external-texture UV adaptation and a
    // rendered preview; gameplay timing/score allocation must remain unchanged.
    public static class ExTapVisualSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var root = new GameObject("EXTAP base regression");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                var materials = (IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Private).GetValue(renderer);
                if (materials.Count == 0) typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                // Produce a preview even on the old model so the regression's
                // failing baseline can be compared to the corrected base.
                Capture(renderer, root);
                var cache = new NoteSkinUvCache();
                var texture = new Texture2D(64, 16, TextureFormat.RGBA32, false);
                try {
                    var pixels = Enumerable.Repeat(new Color32(255, 220, 0, 255), 64 * 16).ToArray();
                    texture.SetPixels32(pixels); texture.Apply();
                    foreach (int width in Enumerable.Range(1, 16)) {
                        string key = "extap_" + width.ToString("00");
                        var resource = Resources.Load<GameObject>("OriginalMeshes/ntt_" + key);
                        Mesh mesh = resource.GetComponentInChildren<MeshFilter>().sharedMesh;
                        Require(mesh.bounds.size.y < .0001f, "EXTAP still has upright walls: " + key);
                        Require(mesh.vertexCount == 4 && mesh.triangles.Length == 6, "base is not exactly two triangles: " + key);
                        // The original base uses width-specific cropped UVs (e.g.
                        // width 3 is .125..875), matching its TAP plane.
                        Mesh originalBase = Resources.Load<GameObject>("OriginalMeshes/ntt_tap_" + width.ToString("00"))
                            .GetComponentInChildren<MeshFilter>().sharedMesh;
                        Require(mesh.vertices.SequenceEqual(originalBase.vertices) && mesh.uv.Zip(originalBase.uv, (a, b) => (a - b).sqrMagnitude < 1e-10f).All(equal => equal) &&
                            mesh.triangles.SequenceEqual(originalBase.triangles), "original base geometry/UVs changed: " + key);
                        Mesh skinned = cache.Get(mesh, texture);
                        Require(skinned.vertices.SequenceEqual(mesh.vertices) && skinned.triangles.SequenceEqual(mesh.triangles),
                            "skin texture adaptation added raised geometry");
                        foreach (bool exHold in new[] { false, true }) {
                            var note = new ChartNote { Kind = exHold ? NoteKind.Hold : NoteKind.ExTap,
                                OriginalType = exHold ? "HXD" : "CHR", TimeMs = 1000, EndTimeMs = exHold ? 2000 : 1000,
                                Lane = (16 - width) / 2, Width = width, EndLane = (16 - width) / 2, EndWidth = width,
                                JudgePointCount = exHold ? 4 : 1, IsLongChainHead = true, IsLongChainTail = true, VisibleTail = exHold };
                            ParsedChart chart = Chart(new List<ChartNote> { note });
                            var points = JudgePointScheduler.Build(chart);
                            renderer.Bind(chart, new DspChartClock());
                            typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", Private).Invoke(renderer, new object[] { 0, 950.0 });
                            var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Private).GetValue(renderer);
                            Require(active.Contains(0), "EX head was culled before its timing");
                            object visual = active[0];
                            var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
                            Require(head.activeSelf && head.GetComponent<MeshFilter>().sharedMesh == mesh, "EX base not used by " + note.OriginalType);
                            Require(head.GetComponent<MeshRenderer>().sharedMaterial.mainTexture == GameplaySkinManager.LoadNoteTexture(key),
                                "EX gold texture replaced by another note family");
                            Require(head.GetComponent<MeshRenderer>().bounds.size.y < .0001f, "head transform inflated base height");
                            renderer.MarkJudged(0, 0);
                            typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", Private).Invoke(renderer, new object[] { 0, 1001.0 });
                            Require(!head.activeSelf, "judged EX base no longer hides normally");
                            Require(JudgePointScheduler.Build(chart).Count == points.Count, "visual change altered judge count");
                        }
                    }
                } finally { cache.Clear(); UnityEngine.Object.DestroyImmediate(texture); }
                Debug.Log("EXTAP visual regression passed: widths 1–16, flat gold bases, HXD heads, original UVs, skin textures, head lifetime and unchanged judge counts.");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static ParsedChart Chart(List<ChartNote> notes) => new ParsedChart(notes, 5000, 120,
            notes.Sum(n => n.JudgePointCount), new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(), new List<ScrollAssignment>());
        private static void Capture(ChartPlaybackRenderer renderer, GameObject root)
        {
            var notes = new List<ChartNote>(); int i = 0;
            foreach (int width in new[] { 1, 4, 8, 16 })
                notes.Add(new ChartNote { Kind = NoteKind.ExTap, OriginalType = "CHR", TimeMs = 1000 + 500 * i++,
                    Lane = (16 - width) / 2, Width = width, EndWidth = width, JudgePointCount = 1 });
            renderer.Bind(Chart(notes), new DspChartClock());
            for (i = 0; i < notes.Count; i++) typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", Private).Invoke(renderer, new object[] { i, 900.0 });
            var cameraObject = new GameObject("EXTAP camera", typeof(Camera));cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.GetComponent<Camera>();camera.transform.position = new Vector3(0, 11.45f, -9.2f);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0);camera.fieldOfView = 48;camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor;camera.backgroundColor = Color.black;
            string path = Environment.GetEnvironmentVariable("CHUNITHM_EXTAP_PREVIEW") ?? "Build/extap-base-preview.png";
            typeof(KingAirVisualCheck).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { camera, 1280, 720, path });
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
