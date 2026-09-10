using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    public static class ModDialogSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void BuildVerifiedApk()
        {
            SlideHeadSelfTest.Run();
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            var root = new GameObject("MOD UI regression");
            var cameraObject = new GameObject("MOD camera");
            AppLanguage previous = UiLocalization.Current;
            try {
                AppUiShell shell = root.AddComponent<AppUiShell>();
                Font font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font);
                Set(shell, "titleFont", font);
                var canvasObject = new GameObject("Test canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Set(shell, "canvas", canvas);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.16f, .19f, .18f);
                canvas.worldCamera = camera;
                Invoke(shell, "BuildMods");
                Invoke(shell, "BuildMultiplayerModPickerV2");
                RectTransform single = Get<RectTransform>(shell, "modsLayer");
                RectTransform multi = Get<RectTransform>(shell, "multiplayerModPickerLayer");
                ModSettings settings = Get<ModSettings>(shell, "mods");
                var legacy = JsonUtility.FromJson<ModSettings>("{\"FlickSimpleMode\":true}");
                Require(!legacy.HasAnyMod && legacy.RecordsScore, "legacy FLICK flag still changes score status");
                Get<Button>(shell, "mirrorChartButton").onClick.Invoke();
                Require(settings.HasAnyMod && settings.RecordsScore, "mirror must preserve scores");
                Get<Button>(shell, "nonMissJusticeCriticalButton").onClick.Invoke();
                Require(!settings.RecordsScore, "simple judgement must disable scores");
                Invoke(shell, "ResetSinglePlayerMods");
                Require(!settings.HasAnyMod && settings.RecordsScore, "reset left an active MOD");
                List<Button> speeds = Get<List<Button>>(shell, "rateButtons");
                speeds[0].onClick.Invoke();
                speeds[2].onClick.Invoke();
                Require(settings.MusicRate == 1.5f, "speeds are not mutually exclusive");
                speeds[2].onClick.Invoke();
                Require(settings.MusicRate == 1f, "reselecting a speed did not clear it");

                List<string> keys = Get<List<string>>(shell, "multiplayerModPickerKeys");
                List<Button> buttons = Get<List<Button>>(shell, "multiplayerModPickerButtons");
                Require(keys.Count == 8 && !keys.Contains("flickSimpleMode") && !keys.Contains("autoPlay"),
                    "multiplayer exposes a retired or forbidden MOD");
                Set(shell, "multiplayerModPickerDraft", new MultiplayerModsMessage());
                Set(shell, "multiplayerCreateMode", "normal");
                Invoke(shell, "RefreshMultiplayerModPickerV2");
                for (int i = 0; i < keys.Count; i++) {
                    bool scoring = keys[i] == "mirrorChart" || keys[i] == "airLongNoMotion";
                    Require(buttons[i].interactable == scoring, "normal-room restriction missing: " + keys[i]);
                    buttons[i].onClick.Invoke();
                }
                MultiplayerModsMessage draft = Get<MultiplayerModsMessage>(shell, "multiplayerModPickerDraft");
                Require(draft.mirrorChart && draft.airLongNoMotion && !draft.airAreaAuto &&
                    !draft.directionalAirAuto && !draft.mineImmunity && draft.musicRate == 1f,
                    "normal room accepted a non-scoring MOD");
                Set(shell, "multiplayerCreateMode", "casual");
                Invoke(shell, "RefreshMultiplayerModPickerV2");
                Require(buttons.All(b => b.interactable), "casual-room options remain locked");
                buttons[keys.IndexOf("musicRate:0.8")].onClick.Invoke();
                buttons[keys.IndexOf("musicRate:1.5")].onClick.Invoke();
                Require(draft.musicRate == 1.5f, "multiplayer speed exclusivity failed");
                buttons[keys.IndexOf("musicRate:1.5")].onClick.Invoke();
                Require(draft.musicRate == 1f, "multiplayer speed reset failed");
                var copyMethod = typeof(AppUiShell).GetMethod("CopyMultiplayerModsV2", BindingFlags.Static | BindingFlags.NonPublic);
                var copy = (MultiplayerModsMessage)copyMethod.Invoke(null, new object[] { new MultiplayerModsMessage { flickSimpleMode = true } });
                Require(!copy.flickSimpleMode, "legacy multiplayer FLICK flag was forwarded");

                foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) {
                    UiLocalization.SetLanguage(language);
                    // Rebuild static headings in the chosen language, just as app reload does.
                    UnityEngine.Object.DestroyImmediate(single.gameObject);
                    UnityEngine.Object.DestroyImmediate(multi.gameObject);
                    Invoke(shell, "BuildMods");
                    Invoke(shell, "BuildMultiplayerModPickerV2");
                    single = Get<RectTransform>(shell, "modsLayer");
                    multi = Get<RectTransform>(shell, "multiplayerModPickerLayer");
                    foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(2340, 1080) }) {
                        single.gameObject.SetActive(true);
                        multi.gameObject.SetActive(false);
                        Invoke(shell, "RefreshMods");
                        Capture(camera, canvas, single, size, $"Build/mods-single-{language}-{size.x}.png");
                        single.gameObject.SetActive(false);
                        multi.gameObject.SetActive(true);
                        foreach (string mode in new[] { "normal", "casual" }) {
                            Set(shell, "multiplayerCreateMode", mode);
                            Invoke(shell, "RefreshMultiplayerModPickerV2");
                            Capture(camera, canvas, multi, size, $"Build/mods-{mode}-{language}-{size.x}.png");
                        }
                    }
                }
                Debug.Log("MOD dialog regression passed: score classes, reset, exclusive/deselectable speeds, legacy migration, normal/casual permissions, 27 UI captures across three languages and three landscape aspect ratios.");
            }
            finally {
                UiLocalization.SetLanguage(previous);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Capture(Camera camera, Canvas canvas, RectTransform layer, Vector2Int size, string path)
        {
            float scale = Mathf.Pow(size.x / 1920f, .55f) * Mathf.Pow(size.y / 1080f, .45f);
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(size.x / scale, size.y / scale);
            camera.orthographicSize = size.y / scale / 2;
            camera.aspect = (float)size.x / size.y;
            var target = new RenderTexture(size.x, size.y, 24);
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                RectTransform body = (RectTransform)layer.GetChild(0);
                var content = body.Cast<Transform>().Where(t => t.GetComponent<Button>() != null || t.GetComponent<Text>() != null)
                    .Select(t => (RectTransform)t).ToArray();
                for (int i = 0; i < content.Length; i++)
                    for (int j = i + 1; j < content.Length; j++)
                        Require(!Bounds(content[i]).Overlaps(Bounds(content[j])), "MOD controls overlap: " + path);
                foreach (Text text in layer.GetComponentsInChildren<Text>()) {
                    TextGenerationSettings settings = text.GetGenerationSettings(text.rectTransform.rect.size);
                    text.cachedTextGenerator.Populate(text.text, settings);
                    Require(text.cachedTextGenerator.characterCountVisible > 0, "MOD label is blank: " + text.text);
                }
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                Require(image.GetPixels32().Count(p => p.r > 100 && p.g > 100 && p.b > 100) > 300,
                    "MOD screenshot has no visible labels");
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
