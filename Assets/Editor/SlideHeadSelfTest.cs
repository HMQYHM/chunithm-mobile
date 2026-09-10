using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class SlideHeadSelfTest
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            GroundInputSelfTest.Run();
            foreach (string name in new[] { "VerifySlideChainHeadVisibility", "VerifyIndependentLongHeadVisibility",
                "VerifyOutOfOrderContinuationOwnership" })
                typeof(OriginalPlaybackSelfTest).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

            var curve = Parse("SLC\t0\t0\t7\t2\t9\t7\t2\tSLD\n" +
                "SLC\t0\t9\t7\t2\t49\t6\t4\tSLD\n" +
                "SLD\t0\t58\t6\t4\t23\t5\t6\tSLD\n");
            Require(curve.Notes[0].HasVisualHead && !curve.Notes[0].VisibleHead && curve.Notes[0].IsContinuation,
                "SLC root must gain a visual head without changing judgement ownership");
            Require(!curve.Notes[1].HasVisualHead, "intermediate SLC gained a duplicate head");
            var before = JudgePointScheduler.Build(curve);
            curve.Notes[0].HasVisualHead = false;
            var after = JudgePointScheduler.Build(curve);
            Require(before.Count == after.Count && before.Zip(after, (a, b) => a.NoteIndex == b.NoteIndex &&
                a.PointIndex == b.PointIndex && a.TimeMs == b.TimeMs).All(equal => equal),
                "visual heads changed the judgement schedule");

            foreach (string head in new[] { "TAP", "CHR" }) {
                var shared = Parse(head + "\t0\t0\t7\t2\nSLC\t0\t0\t7\t2\t48\t6\t4\tSLD\n");
                Require(!shared.Notes.Last().HasVisualHead, "shared TAP/EX start received a duplicate blue head");
            }
            var dense = Parse("SLC\t0\t96\t4\t4\t24\t8\t4\tSLD\n" +
                "SLC\t0\t0\t0\t4\t96\t4\t4\tSLD\n" +
                "TAP\t0\t48\t10\t2\nSLC\t0\t96\t12\t4\t24\t12\t4\tSLD\n");
            Require(dense.Notes[0].HasVisualHead && !dense.Notes[2].HasVisualHead && dense.Notes[3].HasVisualHead,
                "out-of-order/dense records broke endpoint ownership or hid another lane's root");

            string path = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH") ??
                Path.GetFullPath("../\u63d0\u53d6\u7684\u6b4c\u66f2/2018/chart/2018_03.c2s");
            ParsedChart king = C2sParser.Parse(path);
            int index = king.Notes.FindIndex(n => n.Kind == NoteKind.Slide);
            ChartNote rootNote = king.Notes[index];
            Require(rootNote.OriginalType == "SLC" && rootNote.HasVisualHead,
                "KING's opening blue curve still has no head");
            Require(JudgePointScheduler.Build(king).Count(p => p.IsScoring) == king.OriginalJudgeTotal,
                "KING scoring total changed");
            CheckRendering(king, index);
            KingAirVisualCheck.Run();
            Debug.Log($"Slide head regression passed: KING root={rootNote.TimeMs:F3}ms, independent SLC roots, shared TAP/EX heads, continuation/dense/out-of-order records, unchanged scoring and head arrival lifetime.");
        }

        private static ParsedChart Parse(string records)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("RESOLUTION\t384\nBPM_DEF\t120\n" + records));
            return C2sParser.Parse(stream);
        }

        private static void CheckRendering(ParsedChart chart, int rootIndex)
        {
            var root = new GameObject("SLIDE head check");
            var cameraObject = new GameObject("SLIDE camera");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                var materials = (IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", PrivateInstance).GetValue(renderer);
                if (materials.Count == 0) typeof(ChartPlaybackRenderer).GetMethod("Awake", PrivateInstance).Invoke(renderer, null);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 11.45f, -9.2f);
                camera.transform.rotation = Quaternion.Euler(35f, 0, 0);
                camera.fieldOfView = 48;
                camera.farClipPlane = 80;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                var process = typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", PrivateInstance);
                var capture = typeof(KingAirVisualCheck).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
                ChartNote note = chart.Notes[rootIndex];
                double now = note.TimeMs - 180;
                foreach (bool show in new[] { false, true }) {
                    note.HasVisualHead = show;
                    renderer.Bind(chart, new DspChartClock());
                    foreach (var point in JudgePointScheduler.Build(chart).Where(p => p.TimeMs < now))
                        renderer.MarkJudged(point.NoteIndex, point.PointIndex);
                    for (int i = 0; i < chart.Notes.Count; i++) process.Invoke(renderer, new object[] { i, now });
                    var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", PrivateInstance).GetValue(renderer);
                    Require(active.Contains(rootIndex), "opening curve was culled");
                    object visual = active[rootIndex];
                    var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
                    Require(head.activeSelf == show, "SLC head visibility did not reach the renderer");
                    capture.Invoke(null, new object[] { camera, 1280, 720, show ? "Build/slide-head-after.png" : "Build/slide-head-before.png" });
                    if (show) {
                        capture.Invoke(null, new object[] { camera, 1024, 768, "Build/slide-head-tablet.png" });
                        renderer.MarkJudged(rootIndex, 0);
                        process.Invoke(renderer, new object[] { rootIndex, now });
                        Require(head.activeSelf, "a body checkpoint consumed a visual-only head too early");
                        process.Invoke(renderer, new object[] { rootIndex, note.TimeMs });
                        Require(!head.activeSelf, "visual-only head remained pinned after arrival");
                    }
                }
            } finally {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
