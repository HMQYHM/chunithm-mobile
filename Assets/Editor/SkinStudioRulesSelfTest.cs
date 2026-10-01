using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using ChunithmMobile.Input;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Verifies the Studio JSON contract against the actual Unity interpreter.</summary>
    public static class SkinStudioRulesSelfTest
    {
        public static void Run()
        {
            string id = "studio_test_" + Guid.NewGuid().ToString("N");
            string package = Path.Combine("Build", id + ".zip");
            GameplaySkinInfo imported = null;
            Texture2D png = new Texture2D(2, 2);
            GameObject root = null;
            GameObject inputRoot = null;
            GameObject backdropRoot = null;
            string requested = "";
            Action<string> handler = group => requested = group;
            try
            {
                GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
                {
                    Write(archive, "skin.json", "{\"id\":\"" + id + "\",\"name\":\"Studio test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    Write(archive, "ui/gameplay-ui.json", "{\"scene\":{\"background\":{\"image\":\"ui/bg.png\",\"color\":\"#FF0000\",\"opacity\":0.5},\"track\":{\"image\":\"ui/bg.png\",\"laneImage\":\"../bad.png\",\"color\":\"#00FF00\",\"laneColor\":\"#0000FF\",\"judgementLineColor\":\"#FFFFFF\"}},\"customDisplays\":[" +
                        "{\"id\":\"off\",\"enabled\":false,\"trigger\":\"combo\",\"image\":\"ui/bg.png\"}," +
                        "{\"id\":\"motion\",\"trigger\":\"gameplay\",\"value\":\"start\",\"action\":\"live2dMotion\",\"motion\":\"celebrate\"}," +
                        "{\"id\":\"bad\",\"trigger\":\"combo\",\"action\":\"runCode\",\"image\":\"ui/bg.png\"}]}");
                    byte[] data = png.EncodeToPNG();
                    using (Stream output = archive.CreateEntry("ui/bg.png").Open()) output.Write(data, 0, data.Length);
                }
                imported = GameplaySkinManager.InstallPackage(package);
                Require(GameplaySkinManager.SetActive(id), "skin activation failed");
                GameplaySkinStyle style = GameplaySkinManager.Style;
                Require(style.HasSceneBackground && style.SceneBackgroundColor == Color.red &&
                    style.SceneBackgroundOpacity == .5f && style.TrackColor == Color.green &&
                    style.LaneColor == Color.blue && style.JudgementLineColor == Color.white,
                    "scene colors/opacity did not reach the runtime");
                Require(style.LaneTexturePath == "", "scene traversal was accepted");
                Texture2D scene = GameplaySkinManager.LoadSceneTexture(style.TrackTexturePath);
                Require(scene != null && scene.width == 2 && scene == GameplaySkinManager.LoadSceneTexture(style.TrackTexturePath),
                    "scene image was not safely cached");
                Require(GameplaySkinManager.LoadSceneTexture("../outside.png") == null, "unsafe scene path accepted");
                backdropRoot = new GameObject("Studio background test", typeof(Camera));
                var backdrop = backdropRoot.AddComponent<GameplayBackdropRenderer>();
                // Non-ExecuteAlways components do not receive Awake when an
                // Editor self-test creates them outside play mode.
                backdrop.SendMessage("Awake");
                backdrop.Configure(0f, .42f);
                backdrop.ShowJacket(null);
                var background = (MeshRenderer)typeof(GameplayBackdropRenderer).GetField("skinBackgroundRenderer",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backdrop);
                Require(background.enabled && background.sharedMaterial.mainTexture == scene,
                    "custom background was not displayed without a song jacket");
                Require(background.GetComponent<MeshFilter>().sharedMesh.uv[0] == Vector2.zero,
                    "authored PNG background was inverted");
                backdrop.ClearJacket();
                Require(!background.enabled, "skin background remained visible after leaving gameplay");
                Require(style.CustomDisplays.Count == 1 && style.CustomDisplays[0].Motion == "celebrate",
                    "disabled/unknown actions were not rejected");
                root = new GameObject("Studio motion rule", typeof(RectTransform));
                root.AddComponent<GameplaySkinCustomDisplay>().Initialize(null);
                GameplaySkinEvents.SkinMotionRequested += handler;
                GameplaySkinEvents.PublishGameplayState(true);
                Require(requested == "celebrate", "gameplay rule did not publish the whitelisted motion request");
                // A hidden input surface still samples within its authored
                // bounds. Verify local coordinates after moving/shrinking it.
                var definition = new GameplaySkinElementDefinition { X = .2f, Y = .2f, Width = .4f, Height = .3f, Visible = false };
                MethodInfo areaMethod = typeof(AppUiShell).GetMethod("TrySkinArea", BindingFlags.Static | BindingFlags.NonPublic);
                object[] arguments = { definition, default(Rect) };
                Require((bool)areaMethod.Invoke(null, arguments), "hidden region disabled sampling");
                Rect area = (Rect)arguments[1];
                inputRoot = new GameObject("Studio input area test");
                var input = inputRoot.AddComponent<ArcadeTouchInput>();
                input.SetPlayArea(area);
                MethodInfo capture = typeof(ArcadeTouchInput).GetMethod("CaptureSample", BindingFlags.Instance | BindingFlags.NonPublic);
                var touch = new Touch { fingerId = 1, phase = TouchPhase.Began, position = new Vector2(400, 350) };
                capture.Invoke(input, new object[] { 0d, 1000, 1000, new[] { touch } });
                Require(input.Current.Ground.Count == 1, "authored region did not capture its center");
                touch.position = new Vector2(100, 100);
                input.ResetCaptureState();
                capture.Invoke(input, new object[] { 20d, 1000, 1000, new[] { touch } });
                Require(input.Current.Ground.Count == 0, "touch outside authored region was captured");
                Debug.Log("Skin Studio rules regression passed: scene colors/PNG cache, path rejection, disabled rules, motion dispatch.");
            }
            finally
            {
                GameplaySkinEvents.SkinMotionRequested -= handler;
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (inputRoot != null) UnityEngine.Object.DestroyImmediate(inputRoot);
                if (backdropRoot != null) UnityEngine.Object.DestroyImmediate(backdropRoot);
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                if (File.Exists(package)) File.Delete(package);
                UnityEngine.Object.DestroyImmediate(png);
            }
        }

        private static void Write(ZipArchive archive, string path, string json)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(path).Open())) writer.Write(json);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
