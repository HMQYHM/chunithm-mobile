using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    public static class SkinSettingsSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void BuildVerifiedApk()
        {
            SlideHeadSelfTest.Run();
            ModDialogSelfTest.Run();
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            Require(!JsonUtility.FromJson<AppSettings>("{\"SettingsVersion\":18}").HighContrastUi,
                "existing installations must keep the original palette");
            Require(JsonUtility.FromJson<AppSettings>(JsonUtility.ToJson(new AppSettings { HighContrastUi = true })).HighContrastUi,
                "palette selection was not serialized");
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            CheckSkinImportAndPreferences();
            CheckJudgementHud();
            CheckMultiplayerAddressPersistence();
            AppLanguage previous = UiLocalization.Current;
            try {
                foreach (bool themed in new[] { false, true })
                    foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) {
                        UiLocalization.SetLanguage(language);
                        CheckUi(themed, language);
                    }
            }
            finally { UiLocalization.SetLanguage(previous); }
            Debug.Log("Skin UI regression passed: five settings categories, both palettes, saved preferences, package import/activation/fallback, conditional controls, dropdowns and three-language landscape screenshots.");
        }

        private static void CheckSkinImportAndPreferences()
        {
            const string key = "chunithm_mobile_settings_v1";
            bool existed = PlayerPrefs.HasKey(key);
            string saved = PlayerPrefs.GetString(key);
            string previousSkin = GameplaySkinManager.Active.Id;
            string id = "ui_test_" + Guid.NewGuid().ToString("N");
            string package = Path.Combine("Build", id + ".cmgameplay");
            GameplaySkinInfo imported = null;
            Texture2D importedTexture = null;
            Texture2D importedJudgementTexture = null;
            var texture = new Texture2D(2, 2);
            try {
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Create)) {
                    using (var writer = new StreamWriter(archive.CreateEntry("skin.json").Open()))
                        writer.Write("{\"id\":\"" + id + "\",\"name\":\"UI test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    using (var output = archive.CreateEntry("notes/tap_01.png").Open()) {
                        byte[] png = texture.EncodeToPNG();
                        output.Write(png, 0, png.Length);
                    }
                    using (var output = archive.CreateEntry("ui/judgements/justice_critical.png").Open()) {
                        byte[] png = texture.EncodeToPNG();
                        output.Write(png, 0, png.Length);
                    }
                }
                imported = GameplaySkinManager.InstallPackage(package);
                Require(GameplaySkinManager.SetActive(id), "imported skin activation failed");
                importedTexture = GameplaySkinManager.LoadNoteTexture("tap_01");
                Require(importedTexture != null && importedTexture.width == 2, "external note did not load");
                Require(GameplaySkinManager.GetResourceSummary(imported).Missing.Count > 0, "missing assets were not reported");
                Require(GameplaySkinManager.LoadNoteTexture("tap_02") != null, "missing note did not fall back");
                importedJudgementTexture = GameplaySkinManager.LoadJudgementTexture(Chart.Judgement.JusticeCritical);
                Require(importedJudgementTexture.width == 2,
                    "external judgement image was ignored");
                Require(GameplaySkinManager.LoadJudgementTexture(Chart.Judgement.Miss).width > 2,
                    "missing judgement did not fall back to built-in image");
                CheckJudgementHud();
                PlayerPrefs.SetString(key, "{\"SettingsVersion\":18,\"GameplayComboX\":61,\"GameplayComboY\":65,\"GameplayComboScale\":120}");
                AppSettings migrated = AppSettings.Load();
                Require(migrated.GameplayJudgementX == 61 && migrated.GameplayJudgementY == 53 &&
                    migrated.GameplayJudgementScale == 120, "legacy judgement position was not migrated");
                migrated.GameplayJudgementX = 32;
                migrated.GameplayJudgementScale = 75;
                migrated.Save();
                migrated = AppSettings.Load();
                Require(migrated.GameplayComboX == 61 && migrated.GameplayComboScale == 120 &&
                    migrated.GameplayJudgementX == 32 && migrated.GameplayJudgementScale == 75,
                    "independent judgement preferences did not survive reload");
                foreach (bool palette in new[] { true, false }) {
                    new AppSettings { HighContrastUi = palette, GameplaySkinId = id, BackgroundSkinMode = 1 }.Save();
                    AppSettings loaded = AppSettings.Load();
                    Require(loaded.HighContrastUi == palette && loaded.GameplaySkinId == id && loaded.BackgroundSkinMode == 1,
                        "saved palette or skin selection lost on reload");
                }
            }
            finally {
                // Runtime cache disposal is deferred; edit-mode fixtures own immediate cleanup.
                if (importedTexture != null) UnityEngine.Object.DestroyImmediate(importedTexture);
                if (importedJudgementTexture != null) UnityEngine.Object.DestroyImmediate(importedJudgementTexture);
                GameplaySkinManager.SetActive(previousSkin);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                if (File.Exists(package)) File.Delete(package);
                UnityEngine.Object.DestroyImmediate(texture);
                if (existed) PlayerPrefs.SetString(key, saved); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        private static void CheckMultiplayerAddressPersistence()
        {
            const string serverKey = "chunithm_mobile_multiplayer_server";
            const string playerKey = "chunithm_mobile_multiplayer_player";
            bool hadServer = PlayerPrefs.HasKey(serverKey), hadPlayer = PlayerPrefs.HasKey(playerKey);
            string savedServer = PlayerPrefs.GetString(serverKey), savedPlayer = PlayerPrefs.GetString(playerKey);
            try {
                PlayerPrefs.DeleteKey(serverKey);
                PlayerPrefs.DeleteKey(playerKey);
                foreach (string address in new[] { "192.0.2.42:27960", "ws://192.0.2.43:34567/game" }) {
                    for (int pass = 0; pass < 2; pass++) {
                        var root = new GameObject("Multiplayer address check");
                        try {
                            AppUiShell shell = root.AddComponent<AppUiShell>();
                            Set(shell, "settings", new AppSettings());
                            Font font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                            Set(shell, "font", font); Set(shell, "titleFont", font);
                            var canvasObject = new GameObject("Canvas", typeof(Canvas));
                            canvasObject.transform.SetParent(root.transform, false);
                            Set(shell, "canvas", canvasObject.GetComponent<Canvas>());
                            Invoke(shell, "BuildMultiplayerV2");
                            InputField input = Get<InputField>(shell, "multiplayerServerInput");
                            if (pass == 0) {
                                Require(input.text == PlayerPrefs.GetString(serverKey, ""), "login did not restore saved address");
                                input.text = address;
                                Get<InputField>(shell, "multiplayerPlayerNameInput").text = "Tester";
                                Require(PlayerPrefs.GetString(serverKey) == address, "typing did not cache address draft");
                                Set(shell, "settings", null); Set(shell, "uiSession", null);
                                typeof(AppUiShell).GetMethod("OnApplicationPause", Private).Invoke(shell, new object[] { true });
                                Require(PlayerPrefs.GetString(serverKey) == address,
                                    "pause changed address: input=" + input.text + " saved=" + PlayerPrefs.GetString(serverKey));
                                // Exercise the shared restore path used by reopen, logout and timeout.
                                // uGUI edit mode ignores SetTextWithoutNotify's callback flag;
                                // suppress that editor-only callback while simulating a new empty field.
                                InputField.OnChangeEvent changed = input.onValueChanged;
                                input.onValueChanged = new InputField.OnChangeEvent();
                                input.SetTextWithoutNotify("");
                                input.onValueChanged = changed;
                                Require(PlayerPrefs.GetString(serverKey) == address,
                                    "non-notifying clear changed draft: " + PlayerPrefs.GetString(serverKey));
                                Invoke(shell, "RestoreMultiplayerServerV2");
                                Require(input.text == address, "return to login changed address: input=" + input.text +
                                    " saved=" + PlayerPrefs.GetString(serverKey) + " expected=" + address);
                                Invoke(shell, "OnApplicationQuit");
                            } else {
                                Require(input.text == address && Get<InputField>(shell, "multiplayerPlayerNameInput").text == "Tester",
                                    "recreated login lost host, port or player name");
                            }
                        } finally { UnityEngine.Object.DestroyImmediate(root); }
                    }
                }
                Debug.Log("Multiplayer address persistence passed: IP:port, WebSocket URL, edit cache, background flush and login reconstruction; no network connection.");
            } finally {
                if (hadServer) PlayerPrefs.SetString(serverKey, savedServer); else PlayerPrefs.DeleteKey(serverKey);
                if (hadPlayer) PlayerPrefs.SetString(playerKey, savedPlayer); else PlayerPrefs.DeleteKey(playerKey);
                PlayerPrefs.Save();
            }
        }

        private static void CheckJudgementHud()
        {
            var root = new GameObject("Judgement HUD check");
            var cameraObject = new GameObject("Judgement HUD camera");
            try {
                AppUiShell shell = root.AddComponent<AppUiShell>();
                var settings = new AppSettings();
                Set(shell, "settings", settings);
                Font font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font); Set(shell, "titleFont", font);
                var canvasObject = new GameObject("Canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Set(shell, "canvas", canvas);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.07f, .09f, .10f);
                canvas.worldCamera = camera;
                Invoke(shell, "BuildGameplay");
                canvas.transform.GetChild(0).gameObject.SetActive(true);
                Get<Text>(shell, "gameplayCombo").text = "128\nCOMBO";
                MethodInfo layout = typeof(AppUiShell).GetMethod("ApplyGameplayElementLayout", Private);
                layout.Invoke(shell, new object[] { settings });
                RectTransform combo = Get<Text>(shell, "gameplayCombo").rectTransform;
                Vector2 comboSize = combo.sizeDelta, comboAnchor = combo.anchorMin;
                settings.GameplayJudgementX = 35;
                settings.GameplayJudgementY = 35;
                settings.GameplayJudgementScale = 120;
                layout.Invoke(shell, new object[] { settings });
                Require(combo.sizeDelta == comboSize && combo.anchorMin == comboAnchor,
                    "judgement layout still changes COMBO");
                RectTransform block = Get<RectTransform>(shell, "gameplayJudgementBlock");
                Require(Vector2.Distance(block.anchorMin, new Vector2(.35f, .35f)) < .001f &&
                    Vector2.Distance(block.sizeDelta, new Vector2(516, 120)) < .01f,
                    "independent judgement layout not applied");
                MethodInfo show = typeof(AppUiShell).GetMethod("ShowGameplayJudgement", Private);
                foreach (Chart.Judgement grade in Enum.GetValues(typeof(Chart.Judgement))) {
                    show.Invoke(shell, new object[] { grade, 12.3, true });
                    RawImage image = Get<RawImage>(shell, "gameplayJudgementImage");
                    Require(image.texture == GameplaySkinManager.LoadJudgementTexture(grade) && image.gameObject.activeInHierarchy,
                        "HUD is not displaying the selected judgement texture");
                    string text = Get<Text>(shell, "gameplayTiming").text;
                    Require(!text.Contains("JUSTICE") && !text.Contains("ATTACK") && !text.Contains("MISS"),
                        "old grade text still drawn alongside image");
                    Require(grade == Chart.Judgement.Miss ? text.Length == 0 : text.Contains("LATE"),
                        "MISS timing text is incorrect");
                    if (GameplaySkinManager.Active.BuiltIn)
                        foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
                            Capture(camera, canvas, size, $"Build/judgement-hud-{grade}-{size.x}.png");
                }
                Invoke(shell, "BuildLayoutEditor");
                Require(Get<RectTransform>(shell, "editorComboHandle") != Get<RectTransform>(shell, "editorJudgementHandle"),
                    "layout editor does not have independent handles");
                Set(shell, "draftSettings", settings);
                Invoke(shell, "ResetLayoutDraft");
                Require(settings.GameplayJudgementX == 50 && settings.GameplayJudgementY == 41 &&
                    settings.GameplayJudgementScale == 100, "layout reset omitted judgement settings");
                Debug.Log("Judgement HUD passed: " + GameplaySkinManager.Active.Id);
            }
            finally {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void CheckUi(bool themed, AppLanguage language)
        {
            var root = new GameObject("Skin settings check");
            var cameraObject = new GameObject("Skin settings camera");
            try {
                AppUiShell shell = root.AddComponent<AppUiShell>();
                var settings = new AppSettings { HighContrastUi = themed };
                Set(shell, "settings", settings);
                Set(shell, "settingsCategory", "皮肤");
                Font font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font); Set(shell, "titleFont", font);
                var canvasObject = new GameObject("Canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Set(shell, "canvas", canvas);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = UiThemeContrast.Navy;
                canvas.worldCamera = camera;
                Invoke(shell, "BuildHome");
                RectTransform home = (RectTransform)canvas.transform.GetChild(0);
                Invoke(shell, "BuildSettings");
                RectTransform page = (RectTransform)canvas.transform.GetChild(1);
                RectTransform body = Get<RectTransform>(shell, "settingsBody");
                Require(page.Find("Categories").GetComponentsInChildren<Button>().Length == 5, "skin must remain a separate category");
                Require(body.Find("导入游玩皮肤").GetComponent<Button>() != null &&
                    body.Find("管理游玩皮肤").GetComponent<Button>() != null, "skin import/management actions missing");
                Require(body.Find("导入背景图片") == null && body.Find("清除自定义背景") == null,
                    "image controls leaked into note trails");
                Dropdown choices = body.Find("背景拖尾").GetComponentInChildren<Dropdown>();
                Require(choices.options.Count == 3 && choices.value == 0, "background modes must be a three-option list");
                foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(2340, 1080) }) {
                    home.gameObject.SetActive(false); page.gameObject.SetActive(true);
                    Capture(camera, canvas, size, $"Build/skin-{themed}-{language}-{size.x}.png");
                    page.gameObject.SetActive(false); home.gameObject.SetActive(true);
                    Capture(camera, canvas, size, $"Build/home-{themed}-{language}-{size.x}.png");
                }
                home.gameObject.SetActive(false); page.gameObject.SetActive(true);
                typeof(Dropdown).GetMethod("Start", Private).Invoke(choices, null);
                choices.Show();
                GameObject list = (GameObject)typeof(Dropdown).GetField("m_Dropdown", Private).GetValue(choices);
                list.GetComponent<CanvasGroup>().alpha = 1;
                Require(canvas.GetComponentsInChildren<Toggle>().Length >= 4, "background dropdown did not open");
                Capture(camera, canvas, new Vector2Int(1280, 720), $"Build/skin-dropdown-{themed}-{language}.png");
                foreach (string field in new[] { "m_Dropdown", "m_Blocker" }) {
                    FieldInfo info = typeof(Dropdown).GetField(field, Private);
                    var temporary = (GameObject)info.GetValue(choices);
                    info.SetValue(choices, null);
                    if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                }
                // Rebuild in a fresh body to avoid edit-mode deferred Destroy.
                foreach (Transform child in body.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                settings.BackgroundSkinMode = 2;
                Set(shell, "settingsCursor", 28f);
                Invoke(shell, "BuildSkinSettings");
                Require(body.Find("导入背景图片") != null && body.Find("清除自定义背景") != null,
                    "empty image mode must still expose import controls");
                float cursor = Get<float>(shell, "settingsCursor");
                body.sizeDelta = new Vector2(0, cursor + 24);
                Get<ScrollRect>(shell, "settingsScroll").verticalNormalizedPosition = 1;
                Capture(camera, canvas, new Vector2Int(1280, 720), $"Build/skin-images-{themed}-{language}.png");
                if (themed) {
                    Text title = body.Find("导入背景图片").GetComponentInChildren<Text>();
                    Require(UiThemeContrast.Ratio(title.color, Color.white) >= 4.5f, "white-list label contrast failed");
                }
                foreach (string category in new[] { "延迟", "画面", "音频", "更多" }) {
                    foreach (Transform child in body.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                    Set(shell, "settingsCategory", category);
                    typeof(AppUiShell).GetMethod("RenderSettingsCategory", Private).Invoke(shell, new object[] { true });
                    foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
                        Capture(camera, canvas, size, $"Build/settings-{category}-{themed}-{language}-{size.x}.png");
                    RectTransform[] rows = body.Cast<RectTransform>().ToArray();
                    for (int i = 1; i < rows.Length; i++)
                        Require(-rows[i].anchoredPosition.y >= -rows[i - 1].anchoredPosition.y + rows[i - 1].rect.height,
                            "settings rows overlap: " + category + "/" + rows[i].name);
                    if (category == "画面") {
                        Require(body.Find("遮罩高度") == null, "disabled mask still exposes controls");
                        Require(body.Find("帧率上限") == null, "removed frame limit still visible");
                        Require(body.Find("显示帧数").GetComponentInChildren<Toggle>() != null,
                            "FPS display toggle missing");
                    }
                    if (category == "音频") Require(body.Find("按键音").GetComponentInChildren<Toggle>() != null &&
                        body.Find("正解音").GetComponentInChildren<Slider>() != null, "combined audio controls missing");
                }
            }
            finally {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void Capture(Camera camera, Canvas canvas, Vector2Int size, string output)
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
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                Require(image.GetPixels32().Count(p => p.r > 100 && p.g > 100 && p.b > 100) > 500,
                    "skin UI render is blank");
                File.WriteAllBytes(output, image.EncodeToPNG());
            }
            finally {
                camera.targetTexture = null; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
