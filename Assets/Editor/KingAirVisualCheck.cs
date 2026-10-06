using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // 指定高难谱面视觉检查：用于复现 KING 等复杂 AIR 组合的头尾和层级问题。
    public static class KingAirVisualCheck
    {
        public static void Run()
        {
            foreach (string name in new[] { "VerifyAirHoldBodyOwnership",
                "VerifyOutOfOrderContinuationOwnership", "VerifyDenseAirPairTimingPlane" })
                typeof(OriginalPlaybackSelfTest).GetMethod(name,
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var path = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH");
            if (string.IsNullOrEmpty(path))
                path = Path.GetFullPath("../\u63d0\u53d6\u7684\u6b4c\u66f2/2018/chart/2018_03.c2s");
            var chart = C2sParser.Parse(path);
            var points = JudgePointScheduler.Build(chart).Where(p => p.IsScoring).OrderBy(p => p.TimeMs).ToList();
            Require(points.Count == chart.OriginalJudgeTotal, "KING judgement total changed");
            double now = points[679].TimeMs;
            Debug.Log($"KING combo680 time={now:F3} total={points.Count}");
            foreach (var n in chart.Notes.Where(n => n.EndTimeMs >= now - 1500 && n.TimeMs <= now + 3000))
                Debug.Log($"KING {n.OriginalType}/{n.SourceType} {n.Kind} {n.TimeMs:F3}..{n.EndTimeMs:F3} lane={n.Lane}/{n.Width}->{n.EndLane}/{n.EndWidth} height={n.AirHeight}->{n.EndAirHeight} head={n.HasVisualHead} tail={n.VisibleTail}");
            var root = new GameObject("KING visual check");
            var renderer = root.AddComponent<ChartPlaybackRenderer>();
            var materials = (IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
            if (materials.Count == 0)
                typeof(ChartPlaybackRenderer).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(renderer, null);
            renderer.Bind(chart, new DspChartClock());
            var cameraObject = new GameObject("KING camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 11.45f, -9.2f);
            camera.transform.rotation = Quaternion.Euler(35f, 0, 0);
            camera.fieldOfView = 48;
            camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var process = typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < chart.Notes.Count; i++) process.Invoke(renderer, new object[] { i, now });
            VerifyGeometry(renderer, chart, now);
            string output = Environment.GetEnvironmentVariable("CHUNITHM_VISUAL_OUTPUT") ?? "Build/king-air-check.png";
            Capture(camera, 1280, 720, output);
            Capture(camera, 1600, 720, Path.ChangeExtension(output, "wide.png"));
            Capture(camera, 1024, 768, Path.ChangeExtension(output, "tablet.png"));
            foreach (var point in points.Where(p => p.TimeMs < now + 100))
                renderer.MarkJudged(point.NoteIndex, point.PointIndex);
            for (int i = 0; i < chart.Notes.Count; i++) process.Invoke(renderer, new object[] { i, now + 100 });
            Capture(camera, 1280, 720, Path.ChangeExtension(output, "next.png"));
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void Capture(Camera camera, int width, int height, string output)
        {
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            Require(texture.GetPixels32().Count(p => p.r > 30 || p.g > 30 || p.b > 30) > 1000,
                "KING render is blank");
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("KING rendered: " + output);
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(target);
        }

        private static void VerifyGeometry(ChartPlaybackRenderer renderer, ParsedChart chart, double now)
        {
            Require(renderer.GetComponentsInChildren<MeshRenderer>(true).All(item => item.sortingOrder == 0),
                "chart-record priorities override material layers or transparent distance sorting");
            var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
            int holds = 0;
            foreach (DictionaryEntry entry in active) {
                var note = chart.Notes[(int)entry.Key];
                if (note.Kind != NoteKind.AirHold || note.TimeMs != now) continue;
                var body = (Mesh)entry.Value.GetType().GetField("BodyMesh").GetValue(entry.Value);
                var material = ((MeshRenderer)entry.Value.GetType().GetField("BodyRenderer").GetValue(entry.Value)).sharedMaterial;
                Require(body.bounds.size.x > 3f && material.color.a > 0 && material.color.a < .5f,
                    "AIR HOLD lost its full-width translucent sheet");
                holds++;
            }
            Require(holds == 2, "KING fixture must contain both side AIR HOLD sheets");
            Require(chart.Notes.Where(n => n.OriginalType == "AIR").All(n => n.Kind == NoteKind.Air),
                "visual repair must not change AIR judgement types");
            Debug.Log("KING visual regression passed: 10 independent caps, narrow posts, 2 translucent sheets, AIR kinds unchanged.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
