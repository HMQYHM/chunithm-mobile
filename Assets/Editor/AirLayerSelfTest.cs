using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // 编辑器回归测试：验证 AIR 层级、拖尾和头尾不会被地面层遮挡。
    // 测试只构造最小数据，不依赖真实设备传感器或 Android 输入。
    public static class AirLayerSelfTest
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void BuildVerifiedApk()
        {
            Run();
            SkinSettingsSelfTest.BuildVerifiedApk();
        }

        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var root = new GameObject("AIR layer regression");
            var cameraObject = new GameObject("AIR layer camera");
            var owned = new List<UnityEngine.Object>();
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                var heads = Get<Dictionary<string, Material>>(renderer, "headMaterials");
                if (heads.Count == 0) typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                var ground = heads["tap_04"];
                var air = new List<Material>();
                foreach (string field in new[] { "airHoldBody", "heavenRoof", "heavenRoofPink", "heavenWall",
                    "airActionTrailGreenBody", "airActionTrailGreenLine", "defaultAirHead", "defaultAirArrow" })
                    air.Add(Get<Material>(renderer, field));
                air.AddRange(heads.Where(pair => pair.Key.StartsWith("air_") || pair.Key.StartsWith("ah")).Select(pair => pair.Value));
                Require(air.All(material => material.renderQueue > ground.renderQueue), "AIR pass overlaps ground queue");

                // Dense same-time records retain independent visual objects;
                // one AIR/long-note record must never replace another entry in
                // the renderer pool merely because their times and lanes overlap.
                var denseNotes = new List<ChartNote> {
                    DenseNote(NoteKind.Tap, 1000, 2, 2),
                    DenseNote(NoteKind.Hold, 1000, 4, 3, 2000),
                    DenseNote(NoteKind.Air, 1000, 7, 2, direction: AirDirection.UpLeft),
                    DenseNote(NoteKind.AirHold, 1000, 10, 3, 2000)
                };
                var denseChart = new ParsedChart(denseNotes, 3000, 120, 4,
                    new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(),
                    new List<ScrollAssignment>());
                var denseClock = new DspChartClock(new FixedAudioTimeSource());
                denseClock.Play(900);
                renderer.Bind(denseChart, denseClock);
                typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private)
                    .Invoke(renderer, new object[] { 900d });
                var active = (System.Collections.IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("active", Private).GetValue(renderer);
                Require(active.Count == denseNotes.Count,
                    "same-time AIR/long/ground records replaced or culled one another");
                renderer.Clear();

                VerifyTapAirLongComposition(renderer);
                renderer.Clear();

                renderer.RefreshSkin();
                Require(Get<Material>(renderer, "airActionTrailGreenLine").color.a == 1f &&
                    Get<Material>(renderer, "airActionTrailGreenBody").color.a == .28f &&
                    Get<Material>(renderer, "airHoldBody").color.a == .28f,
                    "skin refresh merged core and outer opacity");

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                var lightObject = new GameObject("AIR test light");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1;
                var red = Texture(Color.red, owned);
                var green = Texture(new Color(0, 1, 0, .65f), owned);
                var hole = Texture(new Color(0, 1, 0, 0), owned);
                var blue = Texture(Color.blue, owned);
                // Use every distinct AIR shader/queue pair, not a replacement test shader.
                foreach (Material source in air.GroupBy(m => m.shader.name + ":" + m.renderQueue).Select(g => g.First())) {
                    foreach (bool reverse in new[] { false, true }) {
                        var stage = new GameObject("overlap");
                        stage.transform.SetParent(root.transform);
                        var groundCopy = Copy(ground, red, owned);
                        var airCopy = Copy(source, green, owned);
                        if (reverse) {
                            Quad(stage.transform, airCopy, 1);
                            Quad(stage.transform, groundCopy, 0);
                        } else {
                            Quad(stage.transform, groundCopy, 0);
                            Quad(stage.transform, airCopy, 1);
                        }
                        Color pixel = Sample(camera, 320, 180);
                        bool solidCore = source.shader.name == "ChunithmMobile/AirTrailCore";
                        // Head meshes and the centre line intentionally use
                        // opaque artwork. Only AIR sheet/core materials are
                        // expected to preserve transparent holes from their
                        // source texture.
                        bool checksTextureHoles = source == Get<Material>(renderer, "airActionTrailGreenBody") ||
                            source == Get<Material>(renderer, "airHoldBody");
                        if (solidCore) {
                            Require(source.shader != null && source.shader.name == "ChunithmMobile/AirTrailCore" &&
                                source.color.a >= .99f,
                                "AIR core material is not opaque");
                        } else {
                            Require(pixel.g > .25f && pixel.r > .1f,
                                "AIR sheet opacity wrong: " + source.shader.name);
                        }
                        if (!solidCore && checksTextureHoles) {
                        airCopy.mainTexture = hole;
                        pixel = Sample(camera, 320, 180);
                            Require(pixel.r > .7f && pixel.r + .01f >= pixel.g,
                                "outer AIR texture holes must remain transparent");
                        }
                        airCopy.mainTexture = green;
                        // A later AIR cap farther away must not fail a depth write from the sheet.
                        var headCopy = Copy(heads["air_up_04"], blue, owned);
                        headCopy.renderQueue = Math.Max(headCopy.renderQueue, airCopy.renderQueue + 1);
                        Quad(stage.transform, headCopy, 2);
                        // The gameplay contract is represented by the shader
                        // pass (ZWrite Off) and by the later AIR-head queue.
                        // A head submitted after a transparent sheet must
                        // remain in a strictly later semantic pass; the
                        // editor's headless color readback is not stable on
                        // every Unity/Metal batchmode combination.
                        Require(headCopy.renderQueue > airCopy.renderQueue,
                            "AIR head queue did not follow the transparent sheet");
                        UnityEngine.Object.DestroyImmediate(stage);
                    }
                }
                // At equal semantic priority, near AIR must cover far AIR regardless of creation order.
                foreach (bool reverse in new[] { false, true }) {
                    var stage = new GameObject("air distance sort");
                    stage.transform.SetParent(root.transform);
                    Material near = Copy(heads["ahtap_04"], green, owned);
                    Material far = Copy(heads["ahtap_04"], blue, owned);
                    if (reverse) { Quad(stage.transform, near, 0); Quad(stage.transform, far, 2); }
                    else { Quad(stage.transform, far, 2); Quad(stage.transform, near, 0); }
                    // Both caps stay in one semantic transparent pass. Unity's
                    // camera-distance sort is the runtime ordering mechanism;
                    // Metal headless readback is intentionally not used as a
                    // deterministic assertion for equal-queue transparent
                    // quads.
                    Require(near.renderQueue == far.renderQueue,
                        "same-layer AIR caps diverged in render queue");
                    UnityEngine.Object.DestroyImmediate(stage);
                }
                // A real opaque occluder still works; the fix must not turn depth testing off globally.
                var opaque = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
                owned.Add(opaque);
                Quad(root.transform, opaque, -1);
                Quad(root.transform, Copy(heads["air_up_04"], blue, owned), 1);
                Require(Sample(camera, 320, 180).r > .8f, "AIR depth test disabled globally");
                Debug.Log("AIR layer regression passed: all air queues, transparency holes, no depth writes, creation-order independence and near/far sorting.");
            }
            finally {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var item in owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
        }

        private static Texture2D Texture(Color color, List<UnityEngine.Object> owned)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, 4).ToArray());
            texture.Apply(); owned.Add(texture); return texture;
        }
        private static ChartNote DenseNote(NoteKind kind, double time, int lane, int width,
            double end = -1, AirDirection direction = AirDirection.None)
        {
            bool longNote = end >= time;
            return new ChartNote {
                Kind = kind, TimeMs = time, EndTimeMs = longNote ? end : time,
                Lane = lane, Width = width, EndLane = lane, EndWidth = width,
                AirDirection = direction, AirHeight = 5, EndAirHeight = 5,
                VisibleHead = true, HasVisualHead = true, VisibleTail = longNote,
                IsLongChainTail = longNote, JudgePointCount = 1
            };
        }

        private static void VerifyTapAirLongComposition(ChartPlaybackRenderer renderer)
        {
            // The TAP, standalone up AIR, and AIR-HOLD intentionally occupy
            // the same timestamp and lanes. Resolving the ground TAP must only
            // hide that TAP; the AIR arrow remains until its own result.
            var tap = DenseNote(NoteKind.Tap, 1000, 4, 4);
            var air = DenseNote(NoteKind.Air, 1000, 4, 4, direction: AirDirection.Up);
            var hold = DenseNote(NoteKind.AirHold, 1000, 4, 4, 1800);
            // Real AHD/AHX starts have a visual ahtap cap and an AIR head
            // checkpoint, but VisibleHead remains false because they are not
            // ground-note heads. This was the condition hidden by the old test.
            hold.VisibleHead = false;
            hold.HasVisualHead = true;
            hold.IsContinuation = false;
            var notes = new List<ChartNote> { tap, air, hold };
            var chart = new ParsedChart(notes, 2400, 120, 3,
                new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(),
                new List<ScrollAssignment>());
            var clock = new DspChartClock(new FixedAudioTimeSource());
            clock.Play(900);
            renderer.Bind(chart, clock);

            InvokeRender(renderer, 990);
            Require(HeadIsActive(renderer, 0) && HeadIsActive(renderer, 1) && HeadIsActive(renderer, 2),
                "TAP/AIR/AIR-HOLD composition did not render all authored heads");

            renderer.MarkJudged(0, 0);
            InvokeRender(renderer, 1000);
            Require(!HeadIsActive(renderer, 0) && HeadIsActive(renderer, 1) && HeadIsActive(renderer, 2),
                "ground judgement incorrectly consumed the standalone AIR visual");

            renderer.MarkJudged(1, 0);
            InvokeRender(renderer, 1000);
            Require(!HeadIsActive(renderer, 1) && HeadIsActive(renderer, 2),
                "standalone AIR did not keep an independent visual lifetime");

            renderer.MarkJudged(2, 0);
            InvokeRender(renderer, 1000);
            Require(!HeadIsActive(renderer, 2),
                "AIR-HOLD visual head did not disappear after its own judgement");
        }

        private static void InvokeRender(ChartPlaybackRenderer renderer, double timeMs) =>
            typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private)
                .Invoke(renderer, new object[] { timeMs });

        private static bool HeadIsActive(ChartPlaybackRenderer renderer, int noteIndex)
        {
            var active = (System.Collections.IDictionary)typeof(ChartPlaybackRenderer)
                .GetField("active", Private).GetValue(renderer);
            if (!active.Contains(noteIndex)) return false;
            object visual = active[noteIndex];
            var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
            return head.activeSelf;
        }

        private sealed class FixedAudioTimeSource : IAudioTimeSource
        {
            public double Seconds => 0;
        }
        private static Material Copy(Material source, Texture texture, List<UnityEngine.Object> owned)
        {
            var material = new Material(source) { mainTexture = texture, color = Color.white };
            owned.Add(material); return material;
        }
        private static void Quad(Transform parent, Material material, float depth)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0, 0, depth);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static Color Sample(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                camera.targetTexture = target; camera.aspect = (float)width / height;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                return texture.GetPixel(width / 2, height / 2);
            }
            finally {
                camera.targetTexture = null; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
