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
    // MOD 页面回归测试：验证计分与不计分分组、互斥选择和联机模式限制显示。
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
                Set(shell, "settings", new AppSettings { MusicOffsetMs = 20, AnswerSoundOffsetMs = 20 });
                Require(shell.AnswerSoundOffsetMs == 40, "answer sound offset is not added to music latency");
                Require(AppSettings.NormalizeGameplayLatencyOffset(-13) == -13,
                    "gameplay latency still rounds to 5 ms increments");
                var legacy = JsonUtility.FromJson<ModSettings>("{\"FlickSimpleMode\":true}");
                Require(!legacy.HasAnyMod && legacy.RecordsScore, "legacy FLICK flag still changes score status");
                ToggleFromDetail(Get<Button>(shell, "mirrorChartButton"));
                Require(settings.HasAnyMod && settings.RecordsScore, "mirror must preserve scores");
                ToggleFromDetail(Get<Button>(shell, "nonMissJusticeCriticalButton"));
                Require(!settings.RecordsScore, "simple judgement must disable scores");
                Invoke(shell, "ResetSinglePlayerMods");
                Require(!settings.HasAnyMod && settings.RecordsScore, "reset left an active MOD");
                ToggleFromDetail(Get<Button>(shell, "flickAllTapButton"));
                Require(!settings.RecordsScore, "clickable FLICK must disable score saving");
                Invoke(shell, "ResetSinglePlayerMods");
                ToggleFromDetail(Get<Button>(shell, "fullscreenTouchpadButton"));
                Require(settings.FullscreenTouchpad && settings.AirAreaAuto && settings.DirectionalAirAuto && !settings.RecordsScore,
                    "fullscreen touchpad must force the two AIR helpers and disable score saving");
                ToggleFromDetail(Get<Button>(shell, "fullscreenTouchpadButton"));
                Require(!settings.FullscreenTouchpad && !settings.AirAreaAuto && !settings.DirectionalAirAuto,
                    "fullscreen touchpad did not restore prior AIR helper states");
                List<Button> speeds = Get<List<Button>>(shell, "rateButtons");
                ToggleFromDetail(speeds[0]);
                ToggleFromDetail(speeds[2]);
                Require(settings.MusicRate == 1.5f, "speeds are not mutually exclusive");
                ToggleFromDetail(speeds[2]);
                Require(settings.MusicRate == 1f, "reselecting a speed did not clear it");

                List<string> keys = Get<List<string>>(shell, "multiplayerModPickerKeys");
                List<Button> buttons = Get<List<Button>>(shell, "multiplayerModPickerButtons");
                string[] expectedOrder = {
                    "fullscreenTouchpad", "autoPlay", "airAreaAuto", "directionalAirAuto",
                    "mineImmunity", "airLongNoMotion", "nonMissJusticeCritical", "mirrorChart",
                    "flickAllTap", "uniformSpeed"
                };
                Require(keys.Count == expectedOrder.Length + 3 &&
                    keys.Take(expectedOrder.Length).SequenceEqual(expectedOrder) &&
                    keys.Skip(expectedOrder.Length).SequenceEqual(new[] { "musicRate:0.8", "musicRate:1.2", "musicRate:1.5" }),
                    "single and multiplayer MOD order is inconsistent");
                Set(shell, "multiplayerModPickerDraft", new MultiplayerModsMessage());
                Set(shell, "multiplayerCreateMode", "normal");
                Invoke(shell, "RefreshMultiplayerModPickerV2");
                for (int i = 0; i < keys.Count; i++) {
                    bool scoring = keys[i] == "mirrorChart" || keys[i] == "airLongNoMotion" || keys[i] == "uniformSpeed";
                    Require(buttons[i].interactable == scoring, "normal-room restriction missing: " + keys[i]);
                    ToggleFromDetail(buttons[i]);
                }
                MultiplayerModsMessage draft = Get<MultiplayerModsMessage>(shell, "multiplayerModPickerDraft");
                Require(draft.mirrorChart && draft.airLongNoMotion && !draft.airAreaAuto && !draft.flickAllTap &&
                    !draft.fullscreenTouchpad &&
                    !draft.directionalAirAuto && !draft.mineImmunity && draft.uniformSpeed && draft.musicRate == 1f,
                    "normal room accepted a non-scoring MOD");
                Set(shell, "multiplayerCreateMode", "casual");
                Invoke(shell, "RefreshMultiplayerModPickerV2");
                Require(buttons.All(b => b.interactable), "casual-room options remain locked");
                ToggleFromDetail(buttons[keys.IndexOf("fullscreenTouchpad")]);
                Require(draft.fullscreenTouchpad, "casual room did not allow fullscreen touchpad");
                ToggleFromDetail(buttons[keys.IndexOf("musicRate:0.8")]);
                ToggleFromDetail(buttons[keys.IndexOf("musicRate:1.5")]);
                Require(draft.musicRate == 1.5f, "multiplayer speed exclusivity failed");
                ToggleFromDetail(buttons[keys.IndexOf("musicRate:1.5")]);
                Require(draft.musicRate == 1f, "multiplayer speed reset failed");
                var copyMethod = typeof(AppUiShell).GetMethod("CopyMultiplayerModsV2", BindingFlags.Static | BindingFlags.NonPublic);
                var copy = (MultiplayerModsMessage)copyMethod.Invoke(null, new object[] { new MultiplayerModsMessage { flickAllTap = true } });
                Require(copy.flickAllTap, "multiplayer FLICK option was not copied");

                foreach (bool highContrast in new[] { false, true })
                foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) {
                    Set(shell, "settings", new AppSettings { HighContrastUi = highContrast });
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
                        Capture(camera, canvas, single, size, $"Build/mods-single-{highContrast}-{language}-{size.x}.png");
                        single.gameObject.SetActive(false);
                        multi.gameObject.SetActive(true);
                        foreach (string mode in new[] { "normal", "casual" }) {
                            Set(shell, "multiplayerCreateMode", mode);
                            Invoke(shell, "RefreshMultiplayerModPickerV2");
                            Capture(camera, canvas, multi, size, $"Build/mods-{mode}-{highContrast}-{language}-{size.x}.png");
                        }
                    }
                }
                Debug.Log("MOD dialog regression passed: score classes, reset, exclusive/deselectable speeds, legacy migration, normal/casual permissions, 54 UI captures across two palettes, three languages and three landscape aspect ratios.");
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
                RectTransform list = (RectTransform)body.Find("MOD list panel");
                RectTransform details = (RectTransform)body.Find("MOD details");
                Require(!Bounds(list).Overlaps(Bounds(details)), "MOD list overlaps detail pane: " + path);
                foreach (Button button in list.GetComponentsInChildren<Button>())
                    Require(button.transform.Find("Status dot") != null, "MOD list entry is missing its state dot");
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
        // The production UI deliberately separates item selection and changing its value.
        // Tests use the same two gestures that a player uses in the dialog.
        private static void ToggleFromDetail(Button item)
        {
            item.onClick.Invoke();
            for (Transform parent = item.transform; parent != null; parent = parent.parent) {
                Transform details = parent.Find("MOD details");
                if (details == null) continue;
                Button toggle = details.GetComponentInChildren<Button>();
                if (toggle == null) throw new InvalidOperationException("MOD details toggle is missing");
                toggle.onClick.Invoke();
                return;
            }
            throw new InvalidOperationException("MOD details panel is missing");
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
