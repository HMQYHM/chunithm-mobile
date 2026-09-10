using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class OpeningNoteRenderSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class TimeSource : IAudioTimeSource { public double Seconds { get; set; } }

        public static void BuildVerifiedApk()
        {
            Run();
            PreparationAndLongHeadSelfTest.Run();
            AdaptiveFrameRateSelfTest.Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            var root = new GameObject("Opening notes integration test");
            var cameraObject = new GameObject("Gameplay Camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 11.45f, -9.2f);
            camera.transform.rotation = Quaternion.Euler(35, 0, 0);
            camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var field = new GameObject("Shared chart-space playfield");
            field.transform.SetParent(root.transform, false);
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            try {
                var track = field.AddComponent<PlayfieldRenderer>();
                if (!(bool)typeof(PlayfieldRenderer).GetField("initialized", Private).GetValue(track))
                    typeof(PlayfieldRenderer).GetMethod("Awake", Private).Invoke(track, null);
                var renderer = field.AddComponent<ChartPlaybackRenderer>();
                var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Private).GetValue(renderer);
                if (((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Private).GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                var clockSource = new TimeSource { Seconds = 10 };
                var clock = new DspChartClock(clockSource);
                string records = "RESOLUTION\t384\nBPM_DEF\t120\n" +
                    "TAP\t0\t0\t0\t3\nHLD\t0\t0\t4\t2\t192\n" +
                    "SLD\t0\t48\t7\t3\t192\t10\t2\nTAP\t0\t192\t12\t3\nTAP\t1\t0\t2\t3\n";
                using var data = new MemoryStream(Encoding.UTF8.GetBytes(records));
                ParsedChart chart = C2sParser.Parse(data);
                int cases = 0;
                foreach (double rate in new[] { 1d, 2d, 1d })
                foreach (int offset in new[] { 0, -500, 500 }) {
                    clock.Pause(); clockSource.Seconds = 10; clock.SetRate(rate);
                    renderer.Bind(chart, clock);
                    renderer.PrepareFirstFrame(-Math.Max(0, -offset) * rate, camera);
                    Require(camera.targetTexture == null, "warmup changed camera render target");
                    Require(!clock.Playing, "warmup started chart clock");
                    clock.PlayScheduled(12 + Math.Max(0, -offset) / 1000d, 0, 12);
                    clockSource.Seconds = 11.9;
                    Tick(renderer);
                    foreach (var value in active.Values) Require(!NoteRoot(value).activeSelf, "notes leaked during preparation");
                    clockSource.Seconds = 12;
                    Tick(renderer);
                    Require(active.Count > 0, "no opening notes");
                    var first = First(active);
                    GameObject firstRoot = NoteRoot(first);
                    track.RefreshSkin();
                    track.ConfigureTopWidth(80);
                    Tick(renderer);
                    Require(firstRoot != null && firstRoot.activeInHierarchy, "track rebuild destroyed opening note");
                    int pixels = VisibleNotePixels(camera, field.transform.Find("Note geometry"),
                        cases == 0 ? "Build/opening-notes-after-rebuild.png" : null);
                    Require(pixels > 30, "opening frame has no drawn note pixels, case=" + cases);
                    renderer.Clear();
                    // Rebuild with live pooled objects, the original corruption
                    // path when settings are applied before the next song.
                    track.RefreshSkin();
                    Require(firstRoot != null, "track rebuild destroyed pooled note");
                    UnityEngine.Object.DestroyImmediate(firstRoot);
                    renderer.Bind(chart, clock);
                    renderer.PrepareFirstFrame(0, camera);
                    clock.Play(0);
                    Tick(renderer);
                    Require(VisibleNotePixels(camera, field.transform.Find("Note geometry"), null) > 30,
                        "replay failed to recover invalid pooled object");
                    UnityEngine.Object.DestroyImmediate(NoteRoot(First(active)));
                    Tick(renderer);
                    Require(VisibleNotePixels(camera, field.transform.Find("Note geometry"), null) > 30,
                        "active object recovery left an empty frame");
                    renderer.Clear();
                    cases++;
                }
                Debug.Log($"Opening-note render integration passed: {cases} rate/offset/replay cases; track rebuild isolation, invalid pool/active recovery, GPU warmup, first-frame pixel checks.");
            } finally {
                ChartSpace.SetFarWidthPercent(100);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static object First(IDictionary values)
        {
            foreach (object value in values.Values) return value;
            throw new InvalidOperationException("No active note");
        }
        private static GameObject NoteRoot(object visual) => (GameObject)visual.GetType().GetField("Root").GetValue(visual);
        private static void Tick(ChartPlaybackRenderer renderer) => typeof(ChartPlaybackRenderer).GetMethod("LateUpdate", Private).Invoke(renderer, null);

        private static int VisibleNotePixels(Camera camera, Transform notes, string output)
        {
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture cameraTarget = camera.targetTexture;
            try {
                camera.targetTexture = target; RenderTexture.active = target;
                camera.Render(); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                if (output != null) File.WriteAllBytes(output, image.EncodeToPNG());
                Color32[] withNotes = image.GetPixels32();
                notes.gameObject.SetActive(false);
                camera.Render(); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Color32[] without = image.GetPixels32();
                int count = 0;
                for (int i = 0; i < withNotes.Length; i++)
                    if (Math.Abs(withNotes[i].r - without[i].r) + Math.Abs(withNotes[i].g - without[i].g) +
                        Math.Abs(withNotes[i].b - without[i].b) > 40) count++;
                return count;
            } finally {
                notes.gameObject.SetActive(true);
                camera.targetTexture = cameraTarget; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
