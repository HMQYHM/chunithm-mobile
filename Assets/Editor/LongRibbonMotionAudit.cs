using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEditor;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // Diagnostic replay only: feeds the real DSP clock and LateUpdate path at
    // deterministic display cadences, renders actual materials, and records
    // body changes on judgement callbacks. Does not alter runtime behavior.
    public static class LongRibbonMotionAudit
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private sealed class AudioTime : IAudioTimeSource { public double Seconds { get; set; } }
        private static EditorApplication.CallbackFunction liveTick;
        // Live companion to the deterministic replays: use the application's
        // real audio-domain clock and editor update cadence, with no time seeks
        // or injected samples. Exit explicitly because this entry is asynchronous.
        public static void RunLive()
        {
            string output = Environment.GetEnvironmentVariable("CHUNITHM_RIBBON_AUDIT") ?? "Build/ribbon-motion-audit";
            string directory = Path.Combine(output, "live-dsp"); Directory.CreateDirectory(directory);
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var chart = C2sParser.Parse(Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH"));
            var root = new GameObject("Live DSP ribbon audit"); var renderer = Create(root);
            var camera = Camera(root); var clock = new DspChartClock(); renderer.Bind(chart, clock);
            var target = new RenderTexture(960, 540, 24); var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var points = JudgePointScheduler.Build(chart).Where(p => p.TimeMs >= 16691.304348 && p.TimeMs <= 19041.304348)
                .OrderBy(p => p.TimeMs).ToArray();
            var late = typeof(ChartPlaybackRenderer).GetMethod("LateUpdate", Hidden);
            var csv = new StringBuilder("frame,hostSeconds,chartMs,chartDeltaMs,judgements\n");
            double start = EditorApplication.timeSinceStartup, lastHost = start, lastChart = 16691.304348;
            int frame = 0, eventIndex = 0;
            clock.Play(lastChart);
            Action<bool> end = success => {
                EditorApplication.update -= liveTick; clock.Pause();
                File.WriteAllText(Path.Combine(directory, "live-clock.csv"), csv.ToString());
                camera.targetTexture = null; RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(root);
                if (success) Debug.Log("Live DSP ribbon audit completed: frames=" + frame + ", judgements=" + eventIndex);
                EditorApplication.Exit(success ? 0 : 1);
            };
            liveTick = () => {
                try {
                    double host = EditorApplication.timeSinceStartup;
                    if (host-start > 15) throw new TimeoutException("Live audio clock did not complete the interval.");
                    if (host-lastHost < 1.0/60) return;
                    lastHost = host;
                    double now = clock.TimeMs;
                    while (eventIndex < points.Length && points[eventIndex].TimeMs <= now) {
                        renderer.MarkJudged(points[eventIndex].NoteIndex, points[eventIndex].PointIndex); eventIndex++;
                    }
                    late.Invoke(renderer, null); camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0,0,960,540),0,0,false); texture.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(directory, frame.ToString("D4") + ".png"), texture.EncodeToPNG());
                    csv.AppendLine(string.Format(Inv, "{0},{1:F6},{2:F6},{3:F6},{4}", frame++,host,now,now-lastChart,eventIndex));
                    lastChart = now;
                    if (now >= 19041.304348) end(true);
                } catch (Exception ex) { Debug.LogException(ex); end(false); }
            };
            EditorApplication.update += liveTick;
        }
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("CHUNITHM_RIBBON_AUDIT") ?? "Build/ribbon-motion-audit";
            Directory.CreateDirectory(output);
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var manifest = new StringBuilder();
            manifest.AppendLine("scenario\tframeRate\tframes\tjudgeCallbacks\tbodyChangesAtJudge\tinvalidVertices\tmaxFrontDeltaPixels\tcacheMutationChecksFailed\tqualityAA");
            var synthetic = Synthetic();
            foreach (int fps in new[] { 60, 144, 240 })
                Replay(synthetic, "synthetic-" + fps, 300, 4600, fps, fps == 60, output, manifest);
            string path = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("Set CHUNITHM_CHART_PATH to KING.", path);
            ParsedChart real = C2sParser.Parse(path);
            var group = real.Notes.Select((n, i) => new { Note = n, Index = i })
                .Where(x => x.Note.Kind == NoteKind.Slide && x.Note.LongChainId >= 0)
                .GroupBy(x => x.Note.LongChainId)
                .Where(g => g.Count() >= 4 && g.Max(x => x.Note.EndTimeMs) - g.Min(x => x.Note.TimeMs) > 900)
                .OrderByDescending(g => g.Sum(x => Math.Abs(x.Note.EndLane - x.Note.Lane)) + g.Count()).First();
            double from = group.Min(x => x.Note.TimeMs) - 700;
            double to = Math.Min(group.Max(x => x.Note.EndTimeMs) + 350, from + 7500);
            File.WriteAllText(Path.Combine(output, "real-chain.txt"), path + "\n" + string.Join("\n", group.Select(x =>
                $"index={x.Index} type={x.Note.OriginalType} time={x.Note.TimeMs:F3}..{x.Note.EndTimeMs:F3} lane={x.Note.Lane}/{x.Note.Width}->{x.Note.EndLane}/{x.Note.EndWidth} points={x.Note.JudgePointCount} chain={x.Note.LongChainId}")));
            foreach (int fps in new[] { 60, 144 }) Replay(real, "king-" + fps, from, to, fps, fps == 60, output, manifest);
            int quality = QualitySettings.GetQualityLevel();
            try {
                QualitySettings.SetQualityLevel(2, false);
                Replay(real, "king-mobile-60", from, to, 60, true, output, manifest);
            } finally { QualitySettings.SetQualityLevel(quality, false); }
            File.WriteAllText(Path.Combine(output, "summary.tsv"), manifest.ToString());
            CaptureScale(output);
            BoundaryProbe(real, output);
            ClockBackstepProbe(output);
            Debug.Log("Long ribbon motion audit completed: " + output);
        }
        private static ParsedChart Synthetic()
        {
            var notes = new List<ChartNote>();
            foreach (NoteKind kind in new[] { NoteKind.Slide, NoteKind.AirHold, NoteKind.AirAction }) {
                int chain = (int)kind + 100;
                for (int j = 0; j < 6; j++) {
                    int lane = j % 2 == 0 ? 1 : 10;
                    int endLane = j % 2 == 0 ? 10 : 1;
                    notes.Add(new ChartNote { Kind = kind, OriginalType = kind == NoteKind.Slide ? "SLD" : kind == NoteKind.AirHold ? "AHD" : "ASD",
                        TimeMs = 1000 + j * 500, EndTimeMs = 1500 + j * 500, Lane = lane, EndLane = endLane,
                        Width = j % 2 == 0 ? 2 : 5, EndWidth = j % 2 == 0 ? 5 : 2,
                        AirHeight = j % 2 == 0 ? 1 : 7, EndAirHeight = j % 2 == 0 ? 7 : 1,
                        JudgePointCount = 4, LongChainId = chain, IsContinuation = j > 0, IsSlideCheckpoint = j > 0,
                        IsLongChainHead = j == 0, IsLongChainTail = j == 5, VisibleHead = j == 0,
                        HasVisualHead = j == 0, VisibleTail = j == 5 });
                }
            }
            notes = notes.OrderBy(n => n.TimeMs).ToList();
            return new ParsedChart(notes, 5000, 120, notes.Sum(n => n.JudgePointCount), new List<ScrollEvent>(),
                new Dictionary<int, List<ScrollEvent>>(), new List<ScrollAssignment>());
        }
        private static ChartPlaybackRenderer Create(GameObject root)
        {
            var renderer = root.AddComponent<ChartPlaybackRenderer>();
            if (((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Hidden).GetValue(renderer)).Count == 0)
                typeof(ChartPlaybackRenderer).GetMethod("Awake", Hidden).Invoke(renderer, null);
            renderer.VisualSpeed = OriginalNoteSpeed.VisualMultiplier(OriginalNoteSpeed.DefaultOption);
            return renderer;
        }
        private static Camera Camera(GameObject root)
        {
            var go = new GameObject("Audit gameplay camera", typeof(Camera)); go.transform.SetParent(root.transform);
            var camera = go.GetComponent<Camera>(); camera.transform.position = new Vector3(0, 11.45f, -9.2f);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0); camera.fieldOfView = 48;
            camera.nearClipPlane = .1f; camera.farClipPlane = 80; camera.aspect = 16f / 9;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            return camera;
        }
        private static void Replay(ParsedChart chart, string name, double from, double to, int fps,
            bool capture, string output, StringBuilder manifest)
        {
            string directory = Path.Combine(output, name); Directory.CreateDirectory(directory);
            var root = new GameObject(name);
            var time = new AudioTime(); var clock = new DspChartClock(time); clock.Play(from);
            var renderer = Create(root); renderer.Bind(chart, clock);
            var camera = Camera(root);
            var rt = new RenderTexture(960, 540, 24) { antiAliasing = Math.Max(1, QualitySettings.antiAliasing) };
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            camera.targetTexture = rt;
            var ticks = JudgePointScheduler.Build(chart).Where(p => p.TimeMs >= from && p.TimeMs <= to).OrderBy(p => p.TimeMs).ToArray();
            var late = typeof(ChartPlaybackRenderer).GetMethod("LateUpdate", Hidden);
            var render = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Hidden);
            var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Hidden).GetValue(renderer);
            string initial = CacheSignature(renderer);
            int events = 0, changes = 0, missing = 0, frames = (int)Math.Ceiling((to - from) * fps / 1000);
            float maxJump = 0, cacheError = 0;
            var previous = new Dictionary<int, float>();
            var csv = new StringBuilder("frame,timeMs,note,kind,vertices,frontX,frontScreenX,deltaPixels\n");
            try {
                for (int frame = 0; frame <= frames; frame++) {
                    time.Seconds = frame / (double)fps;
                    double now = clock.TimeMs;
                    late.Invoke(renderer, null);
                    var before = BodySignature(active);
                    int count = 0;
                    while (events < ticks.Length && ticks[events].TimeMs <= now) {
                        renderer.MarkJudged(ticks[events].NoteIndex, ticks[events].PointIndex); events++; count++;
                    }
                    if (count > 0) {
                        render.Invoke(renderer, new object[] { now });
                        if (before != BodySignature(active)) changes++;
                    }
                    if (frame % fps == 0 && initial != CacheSignature(renderer)) cacheError++;
                    foreach (DictionaryEntry entry in active) {
                        int index = (int)entry.Key;
                        var body = (GameObject)entry.Value.GetType().GetField("Body").GetValue(entry.Value);
                        if (!body.activeInHierarchy) continue;
                        Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
                        var vertices = mesh.vertices;
                        if (vertices.Length == 0) continue;
                        var candidates = new List<Vector3>();
                        for (int v = 0; v < vertices.Length; v += 2) {
                            Vector3 center = (vertices[v] + vertices[v + 1]) * .5f;
                            if (Math.Abs(center.z - ChartSpace.JudgementZ) < .0001f) candidates.Add(center);
                            if (float.IsNaN(center.x) || center.z < ChartSpace.JudgementZ - .0001f) missing++;
                        }
                        if (candidates.Count == 0) continue;
                        Vector3 front = candidates[0];
                        float screen = camera.WorldToViewportPoint(front).x * 960;
                        float jump = previous.TryGetValue(index, out float prev) ? screen - prev : 0;
                        previous[index] = screen; maxJump = Mathf.Max(maxJump, Mathf.Abs(jump));
                        csv.AppendLine(string.Format(Inv, "{0},{1:F6},{2},{3},{4},{5:F6},{6:F6},{7:F6}", frame, now, index, chart.Notes[index].Kind,
                            vertices.Length, front.x, screen, jump));
                    }
                    if (capture) {
                        camera.Render(); RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0, false); texture.Apply();
                        File.WriteAllBytes(Path.Combine(directory, frame.ToString("D4") + ".png"), texture.EncodeToPNG());
                    }
                }
                manifest.AppendLine(string.Format(Inv, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6:F6}\t{7}\t{8}",
                    name, fps, frames + 1, events, changes, missing, maxJump, cacheError, QualitySettings.antiAliasing));
                File.WriteAllText(Path.Combine(directory, "geometry.csv"), csv.ToString());
            } finally {
                RenderTexture.active = null; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static string BodySignature(IDictionary active)
        {
            var result = new StringBuilder();
            foreach (DictionaryEntry entry in active) {
                var body = (GameObject)entry.Value.GetType().GetField("Body").GetValue(entry.Value);
                if (!body.activeInHierarchy) continue;
                result.Append(entry.Key).Append(':');
                foreach (Vector3 v in body.GetComponent<MeshFilter>().sharedMesh.vertices) result.Append(v.ToString("F6"));
            }
            return result.ToString();
        }
        private static void BoundaryProbe(ParsedChart chart, string output)
        {
            var root = new GameObject("Boundary microstep probe"); var renderer = Create(root);
            renderer.Bind(chart, new DspChartClock());
            var render = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Hidden);
            var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Hidden).GetValue(renderer);
            var result = new StringBuilder("timeMs,note,frontXBefore,frontXAfter,absoluteDeltaWorld\n");
            try {
                var times = chart.Notes.Where(n => n.Kind == NoteKind.Slide && n.EndTimeMs > n.TimeMs)
                    .SelectMany(n => new[] { n.TimeMs, n.EndTimeMs }).Distinct().OrderBy(t => t);
                foreach (double time in times) {
                    render.Invoke(renderer, new object[] { time - .001 });
                    var before = Fronts(active);
                    render.Invoke(renderer, new object[] { time + .001 });
                    foreach (var entry in Fronts(active)) {
                        if (!before.TryGetValue(entry.Key, out float first)) continue;
                        result.AppendLine(string.Format(Inv, "{0:F6},{1},{2:F8},{3:F8},{4:F8}", time, entry.Key, first, entry.Value, Math.Abs(first-entry.Value)));
                    }
                }
                File.WriteAllText(Path.Combine(output, "boundary-microsteps.csv"), result.ToString());
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static Dictionary<int, float> Fronts(IDictionary active)
        {
            var result = new Dictionary<int, float>();
            foreach (DictionaryEntry entry in active) {
                var body = (GameObject)entry.Value.GetType().GetField("Body").GetValue(entry.Value);
                if (!body.activeInHierarchy) continue;
                var vertices = body.GetComponent<MeshFilter>().sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i += 2) {
                    if (Math.Abs(vertices[i].z - ChartSpace.JudgementZ) > .0001f) continue;
                    result[(int)entry.Key] = (vertices[i].x + vertices[i+1].x) * .5f; break;
                }
            }
            return result;
        }
        private static void ClockBackstepProbe(string output)
        {
            var root = new GameObject("Injected clock backstep"); var renderer = Create(root);
            var chart = Synthetic(); var source = new AudioTime(); var clock = new DspChartClock(source);
            var late = typeof(ChartPlaybackRenderer).GetMethod("LateUpdate", Hidden);
            var result = new StringBuilder("backstepMs,bodyVerticesAfterTail,bodyVerticesAfterBackstep\n");
            try {
                foreach (double step in new[] { 4.0, 8.0, 9.0, 16.0, 32.0 }) {
                    source.Seconds = 0; clock.Play(300); renderer.Bind(chart, clock); late.Invoke(renderer, null);
                    source.Seconds = (4001-300)/1000.0; late.Invoke(renderer, null);
                    int after = VisibleVertices(renderer);
                    source.Seconds -= step/1000.0; late.Invoke(renderer, null);
                    result.AppendLine(string.Format(Inv, "{0},{1},{2}", step, after, VisibleVertices(renderer)));
                }
                File.WriteAllText(Path.Combine(output, "injected-clock-backstep.csv"), result.ToString());
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static int VisibleVertices(ChartPlaybackRenderer renderer)
        {
            var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Hidden).GetValue(renderer);
            int sum = 0;
            foreach (DictionaryEntry entry in active) {
                var body = (GameObject)entry.Value.GetType().GetField("Body").GetValue(entry.Value);
                if (body.activeInHierarchy) sum += body.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            }
            return sum;
        }
        private static string CacheSignature(ChartPlaybackRenderer renderer)
        {
            return (string)typeof(LongNoteJudgementStabilitySelfTest).GetMethod("GeometrySignature", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { renderer, (Array)typeof(ChartPlaybackRenderer).GetField("geometry", Hidden).GetValue(renderer) });
        }
        private static void CaptureScale(string output)
        {
            var root = new GameObject("Perspective measurement"); var camera = Camera(root);
            var result = new StringBuilder("z,widthWorld,widthPixels,depthPixels,groundLinePixels,airLinePixels\n");
            foreach (float z in new[] { .25f, 4, 8, 12, 20, 32, 44 }) {
                Vector3 left = camera.WorldToViewportPoint(new Vector3(-2, .025f, z));
                Vector3 right = camera.WorldToViewportPoint(new Vector3(2, .025f, z));
                Vector3 near = camera.WorldToViewportPoint(new Vector3(0, .025f, z - .33f));
                Vector3 far = camera.WorldToViewportPoint(new Vector3(0, .025f, z + .33f));
                result.AppendLine(string.Format(Inv, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6}", z, 4,
                    (right.x - left.x) * 960, Math.Abs(near.y - far.y) * 540,
                    (right.x - left.x) * 960 * .064 / 4, (right.x - left.x) * 960 * .23 / 4));
            }
            File.WriteAllText(Path.Combine(output, "perspective.csv"), result.ToString());
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
