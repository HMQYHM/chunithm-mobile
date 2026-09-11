using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class WorldEndAirNodeSelfTest
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private sealed class Source : IAudioTimeSource { public double Seconds => 0; }
        private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            string songRoot = Path.GetFullPath("../提取的歌曲/8311");
            SongEntry song = SongCatalog.ReadSongDirectory(songRoot);
            SongChartEntry entry = song.Charts.Single();
            Require(entry.Level == "狂", "8311 did not read worldsEndTagName");
            Require(entry.Difficulty == "WORLDSEND", "existing score identity changed");
            var bucket = typeof(AppUiShell).GetMethod("LevelBucketKey", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string name in new[] { "WORLDSEND", "WORLD'S END", "WorldsEnd" })
                Require((string)bucket.Invoke(null, new object[] { new SongChartEntry { Difficulty = name, Level = "光" } }) == "WD", "WD alias failed");
            Require((string)bucket.Invoke(null, new object[] { new SongChartEntry { Difficulty = "MASTER", Level = "14+" } }) == "14+", "numeric bucket changed");
            var shellRoot = new GameObject("WD selection test");
            shellRoot.SetActive(false);
            try {
                var shell = shellRoot.AddComponent<AppUiShell>();
                typeof(AppUiShell).GetField("catalog", Private).SetValue(shell, new System.Collections.Generic.List<SongEntry> { song });
                typeof(AppUiShell).GetField("selectedSong", Private).SetValue(shell, song);
                typeof(AppUiShell).GetField("selectedChart", Private).SetValue(shell, entry);
                typeof(AppUiShell).GetField("sortMode", Private).SetValue(shell, SongSortMode.Level);
                typeof(AppUiShell).GetMethod("SyncBucketToSelection", Private).Invoke(shell, null);
                var list = (IEnumerable)typeof(AppUiShell).GetMethod("SongsForCurrentBucket", Private).Invoke(shell, null);
                Require(list.Cast<object>().Count() == 1, "selected WD chart absent from WD list");
            } finally { UnityEngine.Object.DestroyImmediate(shellRoot); }

            ParsedChart chart = C2sParser.Parse(entry.Path);
            double measure = 240000d / chart.Bpm;
            var nodes = chart.Notes.Where(n => n.OriginalType == "ASD" && n.TimeMs >= 63 * measure - .01 && n.TimeMs < 64.25 * measure - .01).ToArray();
            Require(nodes.Length == 40, "real measure 63-64 fixture changed");
            Require(nodes.All(n => n.HasVisualHead), "interior ASD caps hidden by parser");
            Require(nodes.Skip(1).All(n => !n.VisibleHead), "visual fix changed scoring head ownership");
            Require(nodes.Select(n => n.LongChainId).Distinct().Count() == 1, "interleaved SXC split AIR chain");

            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            Render(chart, nodes[5].TimeMs - 30, "Build/wd-air-nodes-builtin.png", false);
            string package = "Build/air-node-test-" + Guid.NewGuid().ToString("N") + ".cmgameplay";
            string id = "air_node_test_" + Guid.NewGuid().ToString("N");
            GameplaySkinInfo imported = null;
            var texture = new Texture2D(16, 16);
            try {
                texture.SetPixels(Enumerable.Repeat(Color.magenta, 256).ToArray()); texture.Apply();
                using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) {
                    using (var writer = new StreamWriter(zip.CreateEntry("skin.json").Open()))
                        writer.Write("{\"id\":\"" + id + "\",\"name\":\"AIR node test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    using (var output = zip.CreateEntry("notes/ahact_06.png").Open()) {
                        byte[] bytes = texture.EncodeToPNG(); output.Write(bytes, 0, bytes.Length);
                    }
                }
                imported = GameplaySkinManager.InstallPackage(package);
                Require(GameplaySkinManager.SetActive(id), "skin activation failed");
                Render(chart, nodes[5].TimeMs - 30, "Build/wd-air-nodes-custom.png", true);
                Require(GameplaySkinManager.LoadNoteTexture("ahtapend_06") != null, "missing tail fallback failed");
            } finally {
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                File.Delete(package); UnityEngine.Object.DestroyImmediate(texture);
            }
            Debug.Log("WD AIR regression passed: real 8311 metadata, WD aliases/selection, 40 ASD caps, unchanged scoring heads, connected chain, built-in/custom skin rendered pixels.");
        }

        private static void Render(ParsedChart chart, double now, string path, bool custom)
        {
            var root = new GameObject("AIR actual chart regression");
            var cameraRoot = new GameObject("AIR test camera");
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Private).GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                renderer.Bind(chart, new DspChartClock(new Source()));
                typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private).Invoke(renderer, new object[] { now });
                var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Private).GetValue(renderer);
                int shown = 0;
                foreach (DictionaryEntry item in active) {
                    ChartNote note = chart.Notes[(int)item.Key];
                    if (note.OriginalType != "ASD") continue;
                    var head = (GameObject)item.Value.GetType().GetField("Head").GetValue(item.Value);
                    if (!head.activeInHierarchy) continue;
                    shown++;
                    Require(head.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0, "empty ASD mesh");
                    if (custom && note.Width == 6)
                        Require(GameplaySkinManager.IsExternalNoteTexture(head.GetComponent<MeshRenderer>().sharedMaterial.mainTexture), "custom cap ignored");
                }
                Require(shown >= 4, "renderer hid approaching interior ASD nodes");
                Camera camera = cameraRoot.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 11.45f, -9.2f);
                camera.transform.rotation = Quaternion.Euler(35, 0, 0);
                camera.fieldOfView = 48; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.targetTexture = target; RenderTexture.active = target;
                camera.Render(); pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                Require(pixels.GetPixels32().Count(p => p.r > 100 && p.b > 100 && p.g < p.r * .8f) > 100, "no visible purple caps");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            } finally {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraRoot);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
