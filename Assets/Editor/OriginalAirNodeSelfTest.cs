using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // Tests the native endpoint/ladder semantics against real KING, including
    // the same-frame visual and judge lifecycle rather than only note totals.
    public static class OriginalAirNodeSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class TimeSource : IAudioTimeSource { public double Seconds { get; set; } }
        private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var tiny = Parse("ALD\t1\t0\t0\t4\t1\t5\t2\t0\t4\t2\n" +
                             "ALD\t1\t0\t12\t4\t1\t5\t2\t12\t4\t2\n");
            Require(tiny.OriginalJudgeTotal == 6, "2-tick ladder lost its endpoint checker");
            Require(tiny.GuidePaths.All(p => p.Nodes.Select(n => n.Height).SequenceEqual(new[] { 5f, 3.5f, 2f })),
                "ladder geometry no longer follows authored height");
            var bpm = Parse("BPM\t0\t1\t240\nALD\t0\t0\t0\t4\t1\t5\t2\t0\t4\t2\n");
            double[] times = bpm.GuidePaths.Single().Nodes.Select(n => n.TimeMs).ToArray();
            Require(Math.Abs(times[1] - 2000d / 384) < .001 &&
                Math.Abs(times[2] - 3000d / 384) < .001, "ladder used a constant ms interval across BPM");
            var odd = Parse("ALD\t0\t0\t0\t4\t2\t5\t3\t0\t4\t2\n");
            Require(odd.GuidePaths.Single().Nodes.Count == 2, "non-divisible ladder invented an endpoint");
            var decoration = Parse("ALD\t0\t0\t0\t4\t0\t5\t2\t0\t4\t2\n");
            Require(decoration.Notes.Count == 0, "zero-interval guide became a judge note");
            var connected = Parse("ALD\t1\t0\t0\t4\t2\t5\t1\t2\t4\t4\n" +
                                  "ALD\t1\t1\t2\t4\t2\t4\t3\t8\t4\t1\n");
            var connectedNodes = connected.GuidePaths.SelectMany(p => p.Nodes).ToArray();
            Require(connectedNodes.Length == 3 && connectedNodes.Count(n => n.IsFirst) == 1 &&
                Math.Abs(connectedNodes[1].Lane - 4) < .001 && Math.Abs(connectedNodes[1].Height - 3) < .001,
                "ALD chain restarted its cadence or duplicated a seam");
            ChartMirror.Apply(tiny);
            foreach (AirLadderNode node in tiny.GuidePaths.SelectMany(p => p.Nodes))
                Require(Math.Abs(node.Lane - tiny.Notes[node.NoteIndex].Lane) < .001, "MIRROR detached ladder checker and art");
            VerifyDenseInput(tiny);

            string king = Path.GetFullPath("../Original-Game-Transfer-20261008/原版Windows游戏/data/A000/music/music2018/2018_03.c2s");
            ParsedChart chart = C2sParser.Parse(king);
            var points = JudgePointScheduler.Build(chart);
            Require(chart.OriginalJudgeTotal == 1490 && points.Count == 1490, "KING total changed");
            Require(chart.Notes.Where(n => n.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction)
                .Sum(n => n.JudgePointCount) == 398, "KING AIR total changed");
            Require(chart.GuidePaths.Count == 79 && chart.GuidePaths.Sum(p => p.Nodes.Count) == 145,
                "KING ladder node count differs from the native tick generator");
            foreach (AirLadderNode node in chart.GuidePaths.SelectMany(p => p.Nodes))
                Require(points.Any(p => p.NoteIndex == node.NoteIndex && Math.Abs(p.TimeMs - node.TimeMs) < .001),
                    "plate and scoring plane disagree");
            double Tick(int bar, int tick) => (bar + tick / 384d) * 240000d / 166;
            var six = chart.GuidePaths.Where(p => Math.Abs(p.TimeMs - Tick(36, 285)) < .001).ToArray();
            Require(six.Length == 2 && six.Sum(p => p.Nodes.Count) == 6, "KING six-layer fixture changed");
            foreach (int bar in new[] { 44, 46 })
                Require(chart.GuidePaths.Where(p => Math.Abs(p.TimeMs - Tick(bar, 383)) < .001)
                    .Sum(p => p.Nodes.Count) == 10, "KING ten-layer fixture changed");
            int segment = chart.Notes.FindIndex(n => n.OriginalType == "ASD" && Math.Abs(n.TimeMs - Tick(38, 246)) < .001);
            Require(segment >= 0 && !chart.Notes[segment].HasVisualHead && chart.Notes[segment].JudgePointCount == 0,
                "curve/straight seam gained an early purple node");
            int endpoint = chart.Notes.FindIndex(n => n.OriginalType == "ASD_NODE" && Math.Abs(n.TimeMs - Tick(38, 288)) < .001);
            Require(endpoint >= 0 && chart.Notes[endpoint].Lane == 12 && chart.Notes[endpoint].Width == 4,
                "ASD endpoint lost authored time or position");
            int entry = chart.Notes.FindIndex(n => n.OriginalType == "ASC" && Math.Abs(n.TimeMs - Tick(38, 192)) < .001);
            VerifyRendering(chart, segment, endpoint, entry, six);
            foreach (int fps in new[] { 30, 60, 120 }) VerifyAutoTimeline(chart, fps);
            Debug.Log("Original AIR node regression passed: KING 1490/398, 145 ladder nodes, six/ten layers, ASD endpoint, entry arrow, dense manual input, BPM/MIRROR and 30/60/120 FPS AUTO.");
        }

        private static void VerifyDenseInput(ParsedChart chart)
        {
            var root = new GameObject("Dense AIR ladder input test");
            try {
                var source = new TimeSource(); var clock = new DspChartClock(source); clock.Play();
                var input = root.AddComponent<ArcadeInputRig>();
                if (input.Ground == null) typeof(ArcadeInputRig).GetMethod("Awake", Private).Invoke(input, null);
                var judge = root.AddComponent<GameplayJudgeController>(); judge.Bind(chart, clock, input);
                source.Seconds = chart.Notes[0].TimeMs / 1000d;
                input.Ground.Current.Air = new AirState { Present = true, LastPresentTimeMs = source.Seconds * 1000,
                    LastMotionTimeMs = source.Seconds * 1000, MotionSequence = 1 };
                typeof(GameplayJudgeController).GetMethod("Update", Private).Invoke(judge, null);
                Require(judge.ScoreState.Resolved == 6 && judge.ScoreState.Miss == 0,
                    "one AIR movement failed to resolve dense ladder height layers");
                judge.Bind(chart, clock, input);
                source.Seconds = (chart.Notes.Max(n => n.TimeMs) + 300) / 1000d;
                typeof(GameplayJudgeController).GetMethod("Update", Private).Invoke(judge, null);
                Require(judge.ScoreState.Resolved == 6 && judge.ScoreState.Miss == 6,
                    "untouched ladder nodes did not expire independently");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void VerifyAutoTimeline(ParsedChart chart, int fps)
        {
            var root = new GameObject("AIR AUTO timing test");
            try {
                var source = new TimeSource(); var clock = new DspChartClock(source); clock.Play();
                var input = root.AddComponent<ArcadeInputRig>();
                if (input.Ground == null) typeof(ArcadeInputRig).GetMethod("Awake", Private).Invoke(input, null);
                var judge = root.AddComponent<GameplayJudgeController>(); judge.Bind(chart, clock, input, enableAutoPlay: true);
                MethodInfo update = typeof(GameplayJudgeController).GetMethod("Update", Private);
                var points = JudgePointScheduler.Build(chart);
                for (int frame = 0; frame <= (int)(chart.DurationMs * fps / 1000) + 1; frame++) {
                    source.Seconds = (double)frame / fps; update.Invoke(judge, null);
                    if (frame % fps == 0)
                        Require(judge.ScoreState.Resolved == points.Count(p => p.TimeMs <= clock.TimeMs),
                            "AUTO combo was not resolved at its authored time");
                }
                Require(judge.ScoreState.Combo == 1490 && judge.ScoreState.Miss == 0, "AUTO dropped or duplicated an AIR plane");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void VerifyRendering(ParsedChart chart, int segment, int endpoint, int entry, VisualGuidePath[] six)
        {
            var root = new GameObject("KING AIR visual test");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Private).GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                renderer.Bind(chart, new DspChartClock(new TimeSource()));
                var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Private).GetValue(renderer);
                MethodInfo process = typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", Private);
                GameObject Layer(int index, string name) => (GameObject)active[index].GetType().GetField(name).GetValue(active[index]);
                process.Invoke(renderer, new object[] { segment, chart.Notes[segment].TimeMs - 30 });
                Require(active.Contains(segment) && !Layer(segment, "Head").activeSelf, "ASD seam still displays a purple cap");
                process.Invoke(renderer, new object[] { endpoint, chart.Notes[endpoint].TimeMs - 30 });
                Require(active.Contains(endpoint) && Layer(endpoint, "Head").activeSelf, "ASD endpoint cap missing in renderer");
                renderer.MarkJudged(endpoint, 0);
                process.Invoke(renderer, new object[] { endpoint, chart.Notes[endpoint].TimeMs });
                Require(!Layer(endpoint, "Head").activeSelf, "judged ASD endpoint remained visible");
                Require(entry >= 0, "KING SLD/ASC entry missing");
                process.Invoke(renderer, new object[] { entry, chart.Notes[entry].TimeMs - 30 });
                Require(active.Contains(entry) && Layer(entry, "Head").activeSelf && Layer(entry, "Accent").activeSelf,
                    "KING ground-to-AIR entry lost its connector or up arrow");
                Require(Layer(entry, "Accent").GetComponent<MeshFilter>().sharedMesh.vertexCount > 0, "entry up arrow has no mesh");

                using (var ladder = new AirLadderVisuals(root.transform)) {
                    ladder.Bind(six); Require(ladder.CellCount == 6, "renderer rebuilt a different ladder cadence");
                    float Distance(int profile, double time, double now) => ChartSpace.JudgementZ + (float)(time - now) / 100;
                    ladder.Render(six[0].TimeMs - 30, Distance); Require(ladder.VisibleCount == 6, "approaching ladder layers missing");
                    foreach (AirLadderNode node in six.SelectMany(p => p.Nodes)) ladder.MarkJudged(node.NoteIndex);
                    ladder.Render(six[0].TimeMs, Distance); Require(ladder.VisibleCount == 0, "judged ladder layers remained visible");
                }
                Capture(renderer, chart, six[0].TimeMs - 200, "Build/king-air-six-layers.png");
                Capture(renderer, chart, chart.Notes[segment].TimeMs - 200, "Build/king-air-curve-seam.png");
                Capture(renderer, chart, chart.Notes[entry].TimeMs - 200, "Build/king-air-entry.png");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Capture(ChartPlaybackRenderer renderer, ParsedChart chart, double now, string path)
        {
            var cameraRoot = new GameObject("KING audit camera");
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                renderer.Clear(); renderer.Bind(chart, new DspChartClock(new TimeSource()));
                typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private).Invoke(renderer, new object[] { now });
                Camera camera = cameraRoot.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 11.45f, -9.2f);
                camera.transform.rotation = Quaternion.Euler(35, 0, 0);
                camera.fieldOfView = 48; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.targetTexture = target; RenderTexture.active = target;
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                Require(pixels.GetPixels32().Count(p => p.r > 80 && p.b > 80) > 100,
                    "KING capture has no visible note pixels");
                if (path.EndsWith("six-layers.png")) {
                    // The same-plane green AIR-HOLD cap must not overpaint
                    // either purple top plate. Queue numbers alone do not
                    // guarantee this when MeshRenderer sortingOrder differs.
                    foreach (var guide in chart.GuidePaths.Where(p => Math.Abs(p.TimeMs - now - 200) < .001)) {
                        AirLadderNode node = guide.Nodes.First();
                        float z = (float)typeof(ChartPlaybackRenderer).GetMethod("Distance", Private)
                            .Invoke(renderer, new object[] { node.ScrollProfileId, node.TimeMs, now });
                        Vector3 position = camera.WorldToScreenPoint(new Vector3(
                            ChartSpace.NoteCenterX(node.Lane, node.Width, z), ChartSpace.AirY(node.Height) + .035f, z));
                        int purple = 0;
                        for (int dx = -4; dx <= 4; dx++) for (int dy = -4; dy <= 4; dy++) {
                            Color p = pixels.GetPixel(Mathf.Clamp((int)position.x + dx, 0, 1279),
                                Mathf.Clamp((int)position.y + dy, 0, 719));
                            if (p.r > .2f && p.b > .4f && p.g < p.r * .8f) purple++;
                        }
                        Require(purple >= 10, "green cap obscured the purple ladder top");
                    }
                }
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            } finally {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(cameraRoot); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static ParsedChart Parse(string records)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("RESOLUTION\t384\nBPM_DEF\t120\n" + records));
            return C2sParser.Parse(stream);
        }
    }
}
