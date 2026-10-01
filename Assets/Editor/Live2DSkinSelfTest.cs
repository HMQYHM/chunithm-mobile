using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Live2D.Cubism.Core;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Exercises ZIP import, native model loading, real pixels and touch poses.</summary>
    public static class Live2DSkinSelfTest
    {
        public static void Run()
        {
            string package = Path.Combine("Build", "BongoCatLive2DTouchSkin-v4.zip");
            string source = Path.Combine("Examples", "Skins", "BongoCatLive2DTouch");
            string studioExport = Environment.GetEnvironmentVariable("CHUNITHM_STUDIO_SKIN_ZIP");
            if (!string.IsNullOrEmpty(studioExport))
            {
                // Consume the browser's exact output, not a rebuilt look-alike.
                // All modifications below happen only in the disposable fixture.
                package = Path.GetFullPath(studioExport);
                Require(File.Exists(package), "Missing Skin Studio export: " + package);
                Debug.Log("Live2D testing actual Skin Studio ZIP: " + package);
            }
            else
            {
                Require(Directory.Exists(source), "Missing BongoCat example sources.");
                if (File.Exists(package)) File.Delete(package);
                ZipFile.CreateFromDirectory(source, package);
            }
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            string previous = GameplaySkinManager.Active.Id;
            string fixture = Path.Combine("Build", "live2d-test-fixture.zip");
            File.Copy(package, fixture, true);
            // Never collide with or delete a user's already installed cat skin.
            using (var archive = ZipFile.Open(fixture, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("skin.json");
                string json;
                using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
                entry.Delete();
                json = System.Text.RegularExpressions.Regex.Replace(json, "\"id\"\\s*:\\s*\"[^\"]+\"",
                    "\"id\":\"live2d_regression_" + Guid.NewGuid().ToString("N") + "\"");
                using (var writer = new StreamWriter(archive.CreateEntry("skin.json").Open())) writer.Write(json);
            }
            GameplaySkinInfo imported = null;
            GameObject canvas = null;
            try
            {
                imported = GameplaySkinManager.InstallPackage(fixture);
                Require(GameplaySkinManager.SetActive(imported.Id), "Skin activation failed.");
                if (!string.IsNullOrEmpty(studioExport))
                {
                    Require(GameplaySkinManager.Style.Live2DLeftXRange == new Vector2(.7f, .2f),
                        "Studio reversed calibration was lost during ZIP import.");
                    Require(GameplaySkinManager.Style.Live2DBackgroundPath == "ui/studio/tablet.png" &&
                        Mathf.Approximately(GameplaySkinManager.Style.Live2DBackgroundRect.x, -.53f),
                        "Studio background path/calibration was lost during ZIP import.");
                }
                canvas = new GameObject("Live2D regression canvas", typeof(RectTransform), typeof(Canvas));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
                var child = new GameObject("Live2D regression renderer", typeof(RectTransform));
                var renderer = child.AddComponent<GameplaySkinCubismRenderer>();
                renderer.Initialize(canvas.GetComponent<RectTransform>());
                var surface = typeof(GameplaySkinCubismRenderer).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(renderer) as GameplaySkinModelSurface;
                Require(surface != null, "Actual gameplay Live2D renderer failed to load.");
                Require(surface.Model.Parameters.FindById("CatParamStickLX") != null, "Missing cat paw parameters.");
                using (var writer = new StreamWriter(Path.Combine("Build", "live2d-model-parts.txt")))
                    foreach (var drawable in surface.Model.Drawables)
                        writer.WriteLine(drawable.Id + " part=" + (drawable.ParentPartIndex >= 0 ?
                            surface.Model.Parts[drawable.ParentPartIndex].Id : "root") + " bounds=" +
                            drawable.GetComponent<Live2D.Cubism.Rendering.CubismRenderer>().Mesh.bounds);
                Color32[] idle = Capture(renderer, surface, default, "idle");
                // Sweep the entire touch half: a middle-only pose can pass while
                // the outer endpoint still leaves a paw beside the tablet.
                foreach (float x in new[] { 0f, .25f, .5f })
                {
                    var probe = new GameplaySkinEvents.TouchTrack(true, 1, x, .5f, 0, 0);
                    Capture(renderer, surface, new GameplaySkinEvents.TouchTelemetrySnapshot(probe, default, 0, true),
                        "left-sweep-" + Mathf.RoundToInt(x * 100));
                    var axis = surface.Model.Parameters.FindById("CatParamStickRX");
                    Vector2 range = GameplaySkinManager.Style.Live2DLeftXRange;
                    float expected = Mathf.Lerp(axis.MinimumValue, axis.MaximumValue,
                        Mathf.Lerp(range.x, range.y, x * 2f));
                    Require(Mathf.Abs(axis.Value - expected) < .001f,
                        "Exported touch mapping did not reach the native model parameter.");
                    var paw = Array.Find(surface.Model.Drawables, drawable => drawable.Id == "ArtMesh30")
                        .GetComponent<Live2D.Cubism.Rendering.CubismRenderer>();
                    Debug.Log("Paw calibration x=" + x + " parameter=" + axis.Value + " bounds=" + paw.Mesh.bounds);
                }
                var left = new GameplaySkinEvents.TouchTrack(true, 1, .12f, .35f, 0, 0);
                var right = new GameplaySkinEvents.TouchTrack(true, 1, .86f, .7f, 0, 0);
                var empty = default(GameplaySkinEvents.TouchTrack);
                Color32[] l = Capture(renderer, surface, new GameplaySkinEvents.TouchTelemetrySnapshot(left, empty, 0, true), "left");
                Color32[] r = Capture(renderer, surface, new GameplaySkinEvents.TouchTelemetrySnapshot(empty, right, 0, true), "right");
                Color32[] both = Capture(renderer, surface, new GameplaySkinEvents.TouchTelemetrySnapshot(left, right, 0, true), "both");
                // Isolate paw/body layers when calibrating an imported model;
                // different BongoCat rigs do not share the same drawable layout.
                var hidden = (System.Collections.Generic.HashSet<CubismDrawable>)typeof(GameplaySkinModelSurface)
                    .GetField("hidden", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(surface);
                var previousHidden = new System.Collections.Generic.HashSet<CubismDrawable>(hidden);
                foreach (string id in new[] { "cat2", "cat3", "rightup", "ArtMesh30" })
                {
                    hidden.Clear();
                    foreach (var drawable in surface.Model.Drawables) if (drawable.Id != id) hidden.Add(drawable);
                    Capture(renderer, surface, new GameplaySkinEvents.TouchTelemetrySnapshot(left, right, 0, true), "layer-" + id);
                }
                hidden.Clear();
                hidden.UnionWith(previousHidden);
                Require(Differences(idle, l) > 100, "Left touch did not change rendered model pixels.");
                Require(Differences(idle, r) > 100, "Right touch did not change rendered model pixels.");
                Require(Differences(l, both) > 100 && Differences(r, both) > 100, "Both paws cannot move independently.");
                Color32[] paused = Capture(renderer, surface,
                    new GameplaySkinEvents.TouchTelemetrySnapshot(left, right, 0, false), "paused");
                Require(Differences(idle, paused) < 50, "Paused input did not restore idle paws.");
                Require(GameplaySkinManager.LoadLive2DAsset(imported, typeof(byte[]), "../outside.moc3") == null,
                    "Model paths can escape their skin directory.");
                Require(GameplaySkinManager.LoadLive2DAsset(imported, typeof(byte[]), Path.GetFullPath(package)) == null,
                    "Absolute model paths are accepted.");
                // Also exercise an independent SDK model with real clipping masks.
                var clipping = new GameplaySkinInfo { Id = "sdk_clipping_fixture", BuiltIn = false,
                    RootPath = Path.GetFullPath("Assets/Live2D/Cubism/Samples/Models/Clipping") };
                using (var masked = new GameplaySkinModelSurface(clipping, "Clipping.model3.json", 1f, new GameplaySkinStyle()))
                {
                    Require(Array.Exists(masked.Model.Drawables, drawable => drawable.IsMasked), "Missing masked-model fixture.");
                    Capture(null, masked, default, "clipping");
                }
                var tablet = new GameObject("Skin regression tablet", typeof(RectTransform));
                tablet.transform.SetParent(canvas.transform, false);
                tablet.transform.SetAsFirstSibling();
                tablet.AddComponent<GameplaySkinTouchOverlay>().Initialize(canvas.GetComponent<RectTransform>());
                CaptureLayout(canvas.GetComponent<Canvas>(), renderer, 1920, 1080);
                CaptureLayout(canvas.GetComponent<Canvas>(), renderer, 2560, 1600);
                surface = typeof(GameplaySkinCubismRenderer).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(renderer) as GameplaySkinModelSurface;
                // Switching to the built-in skin must dispose the old camera/model.
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                renderer.RefreshSkin();
                Require(surface.Model == null && surface.Texture == null, "Skin switch leaked model resources.");
                Debug.Log("Live2D skin regression passed: ZIP import, native model, nonblank transparent render, left/right/both/paused poses and cleanup.");
            }
            finally
            {
                if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
                GameplaySkinManager.SetActive(previous);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                File.Delete(fixture);
            }
        }

        private static Color32[] Capture(GameplaySkinCubismRenderer renderer, GameplaySkinModelSurface surface,
            GameplaySkinEvents.TouchTelemetrySnapshot input, string name)
        {
            if (renderer != null)
            {
                GameplaySkinEvents.PublishTouchTelemetry(input);
                typeof(GameplaySkinCubismRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(renderer, null);
            }
            // Explicitly finish native evaluation before reading a camera in edit mode.
            surface.Model.ForceUpdateNow();
            surface.Model.ForceUpdateNow();
            surface.UpdateFrame();
            surface.Camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(surface.Texture.width, surface.Texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = surface.Texture;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                int opaque = 0, transparent = 0, magenta = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.a > 20) opaque++;
                    else transparent++;
                    if (pixel.r > 200 && pixel.g < 30 && pixel.b > 200 && pixel.a > 20) magenta++;
                }
                Require(opaque > 1000 && transparent > 1000 && magenta < opaque / 10,
                    "Blank, opaque or missing-shader model render: " + name + " opaque=" + opaque + " magenta=" + magenta);
                File.WriteAllBytes(Path.Combine("Build", "live2d-" + name + ".png"), image.EncodeToPNG());
                Debug.Log("Live2D pixel check " + name + ": " + opaque + " visible pixels.");
                return pixels;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }

        private static int Differences(Color32[] a, Color32[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].a-b[i].a) + Math.Abs(a[i].r-b[i].r) + Math.Abs(a[i].g-b[i].g) + Math.Abs(a[i].b-b[i].b) > 20) count++;
            return count;
        }

        private static void CaptureLayout(Canvas canvas, GameplaySkinCubismRenderer renderer, int width, int height)
        {
            var camera = new GameObject("Live2D layout test camera").AddComponent<Camera>();
            var target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .14f, .16f, 1);
                camera.cullingMask = 1 << 29;
                foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 29;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                renderer.RefreshSkin();
                var surface = typeof(GameplaySkinCubismRenderer).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(renderer) as GameplaySkinModelSurface;
                Require(surface != null, "Model reload for layout failed.");
                surface.UpdateFrame();
                surface.Camera.Render();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                var withCat = image.GetPixels32();
                File.WriteAllBytes(Path.Combine("Build", "live2d-layout-" + width + "x" + height + ".png"), image.EncodeToPNG());
                var output = renderer.GetComponentInChildren<UnityEngine.UI.RawImage>();
                output.gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                Require(Differences(withCat, image.GetPixels32()) > 1000,
                    "Model offscreen pixels never reached the actual skin UI at " + width + "x" + height);
                output.gameObject.SetActive(true);
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.worldCamera = null;
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
