using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ChunithmMobile.Editor
{
    /// <summary>Isolated control callbacks, settings reloads and a generated UI inventory.
    /// Does not replace Android touch, file-picker, audio-route or real-server tests.</summary>
    public static class UiButtonAuditSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Key = "chunithm_mobile_settings_v1";
        [Serializable] public sealed class Entry {
            public string page, path, kind, label, result, detail;
            public bool enabled, active;
        }
        [Serializable] public sealed class Output { public List<Entry> entries = new List<Entry>(); }
        private static readonly Output output = new Output();
        private static int failures;
        private static AppUiShell shell;
        private static GameObject root;
        private static Canvas canvas;

        public static void Run()
        {
            output.entries.Clear(); failures = 0;
            string[] keys = { Key, "chunithm_mobile_multiplayer_server", "chunithm_mobile_multiplayer_player" };
            bool[] had = keys.Select(PlayerPrefs.HasKey).ToArray();
            string[] saved = keys.Select(k => PlayerPrefs.GetString(k)).ToArray();
            AppLanguage language = UiLocalization.Current;
            string skin = GameplaySkinManager.Active.Id;
            try {
                UiLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                MakeFixture();
                foreach (string method in new[] { "BuildHome", "BuildSongSelect", "BuildDataManagement",
                    "BuildGameplay", "BuildResults", "BuildDelayCalibrationResults", "BuildModal", "BuildMods",
                    "BuildLayoutEditor", "BuildMetadataEditor", "BuildImportOverlay", "BuildCredits", "BuildUpdateNotice", "BuildMultiplayerV2" }) {
                    Check("构建/" + method, () => Call(method));
                }
                Call("RefreshMods"); Call("RefreshMultiplayerFiltersV2"); Call("RefreshCreateMultiplayerOverlayV2");
                Set("multiplayerModPickerDraft", new MultiplayerModsMessage()); Call("RefreshMultiplayerModPickerV2");
                Inventory(canvas.transform, "全界面");
                foreach (var page in Get<Dictionary<AppPage, RectTransform>>("pages")) {
                    // Room action visibility is determined by a server snapshot.
                    // An uninitialized room has both placeholder actions built.
                    if (page.Key == AppPage.MultiplayerRoom) continue;
                    Check("截图/" + page.Key, () => Capture(page.Value, "page-" + page.Key, false));
                }
                Call("BuildSettings");
                Set("currentPage", AppPage.Settings);
                foreach (string category in new[] { "延迟", "画面", "皮肤", "音频", "更多" }) {
                    RenderCategory(category);
                    Inventory(Get<RectTransform>("settingsBody"), "设置/" + category);
                    Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.Settings], "settings-" + category + "-top", false);
                    Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.Settings], "settings-" + category + "-bottom", true);
                    if (category == "皮肤" || category == "更多") continue;
                    ExerciseSettings(category);
                }
                // Conditional rows must be audited as well as the default state.
                var settings = Get<AppSettings>("settings");
                settings.TrackMaskEnabled = true;
                settings.GameplayBelowNoteMetricMode = 2;
                RenderCategory("画面");
                Inventory(Get<RectTransform>("settingsBody"), "设置/画面/条件选项");
                ExerciseSettings("画面/条件选项");
                Check("流速/完整范围与重新读取", VerifySpeed);
                Check("延迟/上限下限与后台保存", VerifyLatencyAndSuspend);
                Check("布局/拖动缩放保存取消重开", VerifyLayout);
                Check("皮肤布局/全局显示开关优先级", VerifyVisibility);
                Check("选歌置顶图标/渲染与点击组件", () => {
                    var pin = Get<Button>("songPinButton");
                    Require(pin.targetGraphic != null && pin.targetGraphic.GetComponent<CanvasRenderer>() != null,
                        "pin target graphic is missing its CanvasRenderer");
                });
                Check("通用确认框/确认与取消", VerifyModal);
                Check("音频/DSP 选择与重读", VerifyDsp);
                Check("在线大厅/筛选及创建表单", VerifyOnlineForms);
                Check("更新日志/展开收起和版本循环", VerifyUpdateLogs);
                Check("元数据编辑/空输入拒绝", () => {
                    Call("ApplyMetadataEdits");
                    Require(Get<Text>("metadataHint").text.Contains("至少填写"), "empty metadata submission was accepted");
                });
                Check("在线房间/按钮状态", VerifyRoomButtons);
                Check("恢复默认/全部序列化字段", VerifyReset);
                Check("语言/循环重建保存及恢复默认", VerifyLanguage);
            }
            finally {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                shell = null;
                for (int i = 0; i < keys.Length; i++) {
                    if (had[i]) PlayerPrefs.SetString(keys[i], saved[i]); else PlayerPrefs.DeleteKey(keys[i]);
                }
                PlayerPrefs.Save();
                GameplaySkinManager.SetActive(skin);
                UiLocalization.SetLanguage(language);
                Directory.CreateDirectory("Build/ui-audit");
                File.WriteAllText("Build/ui-audit/controls.json", JsonUtility.ToJson(output, true));
            }
            if (failures > 0) throw new Exception("UI audit failed " + failures + " checks; inspect Build/ui-audit/controls.json");
            Debug.Log("UI button audit passed: control inventory, settings callbacks/reloads, full speed range, latency bounds, suspend save, layout save/reopen/discard, modal, logs and default reset.");
        }

        private static void MakeFixture()
        {
            root = new GameObject("Isolated UI control audit");
            shell = root.AddComponent<AppUiShell>();
            shell.enabled = false;
            Set("settings", new AppSettings());
            var font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
            Set("font", font); Set("titleFont", font);
            var obj = new GameObject("Audit canvas", typeof(Canvas), typeof(GraphicRaycaster));
            obj.transform.SetParent(root.transform, false);
            canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)obj.transform).sizeDelta = new Vector2(1920, 1080);
            Set("canvas", canvas);
            if (EventSystem.current == null) root.AddComponent<EventSystem>();
        }

        private static void RenderCategory(string category)
        {
            RectTransform body = Get<RectTransform>("settingsBody");
            if (body != null) foreach (Transform child in body.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            Set("settingsCategory", category.Split('/')[0]);
            Call("RenderSettingsCategory", true);
        }

        private static void ExerciseSettings(string category)
        {
            var body = Get<RectTransform>("settingsBody");
            foreach (Slider slider in body.GetComponentsInChildren<Slider>().ToArray()) {
                string path = PathOf(slider.transform);
                Check(category + "/" + path, () => {
                    foreach (float value in new[] { slider.minValue, slider.maxValue, (slider.minValue + slider.maxValue) / 2 }) {
                        string before = JsonUtility.ToJson(Get<AppSettings>("settings"));
                        float oldValue = slider.value;
                        slider.value = value;
                        FlushQueuedApply();
                        RequirePersistedChanges(before, oldValue != slider.value);
                    }
                });
            }
            foreach (UiHoldRepeatButton button in body.GetComponentsInChildren<UiHoldRepeatButton>().ToArray()) {
                Check(category + "/" + PathOf(button.transform), () => {
                    string before = JsonUtility.ToJson(Get<AppSettings>("settings"));
                    Require(button.Pressed != null, "hold button has no callback");
                    button.Pressed(); RequirePersistedChanges(before);
                });
            }
            foreach (Toggle toggle in body.GetComponentsInChildren<Toggle>().ToArray()) {
                Check(category + "/" + PathOf(toggle.transform), () => {
                    string before = JsonUtility.ToJson(Get<AppSettings>("settings"));
                    toggle.isOn = !toggle.isOn; RequirePersistedChanges(before, true);
                });
            }
            // A toggle can rebuild rows, so inspect the live content afterwards.
            foreach (Dropdown dropdown in body.GetComponentsInChildren<Dropdown>().ToArray()) {
                if (dropdown.transform.parent == null || !dropdown.gameObject.activeInHierarchy) continue;
                Check(category + "/" + PathOf(dropdown.transform), () => {
                    for (int i = 0; i < dropdown.options.Count; i++) {
                        string before = JsonUtility.ToJson(Get<AppSettings>("settings"));
                        int oldValue = dropdown.value;
                        dropdown.value = i; RequirePersistedChanges(before, oldValue != dropdown.value);
                    }
                });
            }
        }

        private static void FlushQueuedApply()
        {
            // Edit-mode tests drive the production iterator, including its deferred
            // Save call. This verifies the callback path, not real device scheduling.
            Set("runtimeSettingsApplyDeadline", -1f);
            var routine = (IEnumerator)Call("ApplyQueuedRuntimeSettings");
            for (int i = 0; i < 4 && routine.MoveNext(); i++) { }
        }

        private static void RequirePersistedChanges(string beforeJson, bool expectChange = false)
        {
            AppSettings before = JsonUtility.FromJson<AppSettings>(beforeJson);
            AppSettings current = Get<AppSettings>("settings");
            AppSettings loaded = AppSettings.Load();
            int changedFields = 0;
            foreach (FieldInfo field in typeof(AppSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string)) continue;
                object value = field.GetValue(current);
                if (!Equals(value, field.GetValue(before))) {
                    changedFields++;
                    Require(Equals(value, field.GetValue(loaded)), "reload lost " + field.Name + ": expected " + value + ", got " + field.GetValue(loaded));
                }
            }
            Require(!expectChange || changedFields > 0, "control changed but no setting field changed");
        }

        private static void VerifySpeed()
        {
            var settings = Get<AppSettings>("settings");
            settings.NoteSpeedOption = 0;
            for (int i = 1; i < OriginalNoteSpeed.Count; i++) {
                Call("ChangeNoteSpeed", 1);
                Require(settings.NoteSpeedOption == i && AppSettings.Load().NoteSpeedOption == i, "speed increment/reload failed: " + i);
            }
            Call("ChangeNoteSpeed", 1);
            Require(settings.NoteSpeedOption == OriginalNoteSpeed.Count - 1, "speed exceeded max");
            for (int i = settings.NoteSpeedOption - 1; i >= 0; i--) {
                Call("ChangeNoteSpeed", -1);
                Require(settings.NoteSpeedOption == i && AppSettings.Load().NoteSpeedOption == i, "speed decrement failed: " + i);
            }
        }

        private static void VerifyLatencyAndSuspend()
        {
            RenderCategory("延迟");
            var settings = Get<AppSettings>("settings");
            Transform row = Get<RectTransform>("settingsBody").Find("音乐延迟");
            var minus = row.GetComponentsInChildren<UiHoldRepeatButton>().First(b => b.GetComponentInChildren<Text>().text == "-1");
            var plus = row.GetComponentsInChildren<UiHoldRepeatButton>().First(b => b.GetComponentInChildren<Text>().text == "+1");
            settings.MusicOffsetMs = -500; minus.Pressed();
            Require(settings.MusicOffsetMs == -500, "latency lower bound");
            settings.MusicOffsetMs = 500; plus.Pressed();
            Require(settings.MusicOffsetMs == 500, "latency upper bound");
            plus.Pressed(); minus.Pressed();
            Require(AppSettings.Load().MusicOffsetMs == 499, "latency no longer has 1ms precision");
            settings.BackgroundBrightness = 73;
            Call("QueueRuntimeSettingsApply");
            Call("OnApplicationPause", true);
            Require(AppSettings.Load().BackgroundBrightness == 73, "suspending before deferred apply loses setting");
        }

        private static void VerifyLayout()
        {
            Set("currentPage", AppPage.Settings);
            Call("OpenLayoutEditor");
            var draft = Get<AppSettings>("draftSettings");
            var drag = Get<RectTransform>("editorComboHandle").GetComponent<UiDragHandle>();
            drag.Changed(new Vector2(.42f, .55f));
            var item = draft.GameplayLayoutOverrides.First(i => i.ElementId == "comboHud");
            Require(Mathf.Abs(item.X + item.Width / 2 - .42f) < .001f &&
                Mathf.Abs(item.Y + item.Height / 2 - .55f) < .001f, "combo drag ignored");
            float width = item.Width;
            var resize = Get<RectTransform>("editorComboHandle").GetComponentInChildren<UiResizeHandle>();
            resize.Changed(-22f);
            Require(item.Width < width, "inward resize did not shrink");
            string savedItem = JsonUtility.ToJson(item);
            Call("SaveLayoutDraft");
            Require(Get<AppSettings>("draftSettings") == null &&
                JsonUtility.ToJson(AppSettings.Load().GameplayLayoutOverrides.First(i => i.ElementId == "comboHud")) == savedItem,
                "layout save lost drag/resize");
            Call("OpenLayoutEditor");
            Require((bool)Call("EquivalentLayoutSettings", Get<AppSettings>("draftSettings"), Get<AppSettings>("settings")), "saved layout reopens as dirty");
            Get<AppSettings>("draftSettings").GameplayLayoutOverrides.First(i => i.ElementId == "comboHud").X = .1f;
            Call("CloseLayoutEditor");
            Require(Get<RectTransform>("modalLayer").gameObject.activeSelf, "changed layout closed without confirmation");
            Call("HideModal");
            Require(Get<RectTransform>("layoutEditorLayer").gameObject.activeSelf, "cancel confirmation closes editor");
            Call("CloseLayoutEditor"); Call("ConfirmModal");
            Require(Get<AppSettings>("draftSettings") == null && JsonUtility.ToJson(Get<AppSettings>("settings").GameplayLayoutOverrides.First(i => i.ElementId == "comboHud")) == savedItem, "discard overwrote saved layout");
        }

        private static void VerifyVisibility()
        {
            var settings = Get<AppSettings>("settings");
            foreach (var item in settings.GameplayLayoutOverrides) item.Visible = true;
            settings.ShowTouchpadVisual = false; settings.ShowInputBoundary = false;
            settings.ShowGameplaySongInfo = false; settings.ShowGameplayFps = false; settings.ShowAirGuide = false;
            Call("ApplyRuntimeSettings");
            Require(!Get<RectTransform>("gameplayTouchpad").gameObject.activeSelf &&
                !Get<Image>("gameplayAirBoundaryBottom").gameObject.activeSelf &&
                !Get<Image>("gameplayAirBoundaryTop").gameObject.activeSelf &&
                !Get<RectTransform>("gameplaySongInfoBlock").gameObject.activeSelf &&
                !Get<Text>("gameplayFpsLabel").gameObject.activeSelf, "skin overrides re-enable globally hidden panels");
            settings.ShowTouchpadVisual = true; settings.ShowInputBoundary = true; settings.ShowGameplaySongInfo = true;
            Call("ApplyRuntimeSettings");
            Require(Get<RectTransform>("gameplayTouchpad").gameObject.activeSelf &&
                Get<Image>("gameplayAirBoundaryBottom").gameObject.activeSelf &&
                Get<Image>("gameplayAirBoundaryTop").gameObject.activeSelf &&
                Get<RectTransform>("gameplaySongInfoBlock").gameObject.activeSelf, "global visibility switches do not recover");
        }

        private static void VerifyModal()
        {
            bool invoked = false;
            Call("ShowModal", "Audit", "Confirmation", (UnityEngine.Events.UnityAction)(() => invoked = true), true);
            Call("HideModal"); Require(!invoked, "cancel executed action");
            Call("ShowModal", "Audit", "Confirmation", (UnityEngine.Events.UnityAction)(() => invoked = true), true);
            Call("ConfirmModal"); Require(invoked && !Get<RectTransform>("modalLayer").gameObject.activeSelf, "confirm didn't execute/close");
        }

        private static void VerifyUpdateLogs()
        {
            RenderCategory("更多");
            Call("ToggleUpdateLogs"); Require(Get<bool>("updateLogsExpanded"), "logs did not expand");
            Call("ChangeUpdateLog", 1); Require(Get<int>("updateLogIndex") == 1, "next version failed");
            Call("ChangeUpdateLog", -1); Require(Get<int>("updateLogIndex") == 0, "previous version failed");
            Call("ToggleUpdateLogs"); Require(!Get<bool>("updateLogsExpanded"), "logs did not collapse");
        }

        private static void VerifyRoomButtons()
        {
            Set("multiplayerGameId", "audit-self");
            Set("currentPage", AppPage.MultiplayerRoom);
            var self = new MultiplayerRoomMemberDto { playerId = "audit-self", name = "Audit", connected = true };
            var room = new MultiplayerRoomDto { roomId = "123", roomName = "常驻按钮测试", mode = "casual",
                state = "lobby", persistent = true, maxPlayers = 20, memberCount = 1,
                members = new[] { self }, mods = new MultiplayerModsMessage(),
                librarySelection = new MultiplayerLibrarySelectionDto { mode = "levels", levels = new[] { "13", "13+", "WD" } },
                song = new MultiplayerSongDto { songId = "audit-song", songName = "测试歌曲", difficulty = "WORLDSEND", level = "狂☆3", transferReady = true },
                songChangeVote = new MultiplayerSongChangeVoteDto { active = true, count = 1, eligible = 2, threshold = 2, requiredRatio = .75f }
            };
            Set("activeMultiplayerRoom", room);
            Call("RenderMultiplayerRoomV2");
            Require(!Get<Button>("multiplayerRoomSongButton").gameObject.activeSelf, "persistent room exposes manual sync");
            Require(!Get<Button>("multiplayerRoomModeButton").gameObject.activeSelf &&
                !Get<Button>("multiplayerRoomSelectionButton").gameObject.activeSelf, "persistent room exposes host settings");
            Require(Get<Button>("multiplayerRoomReadyButton").interactable, "persistent member cannot prepare without songReady");
            Require(Get<Button>("multiplayerRoomVoteButton").GetComponentInChildren<Text>().text.Contains("1/2"), "vote progress missing");
            Require(Get<Text>("multiplayerRoomSongLabel").text.Contains("WD 狂☆3"), "WD level missing from song title");
            Inventory(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "常驻房间/未准备");
            Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "room-persistent-lobby", false);
            if (!Get<Button>("multiplayerRoomModsButton").interactable)
                output.entries.Add(new Entry { page = "已发现问题", path = "常驻房间/MOD", result = "未实现",
                    detail = "休闲常驻房间的成员 MOD 入口仍被 host 权限禁用；客户端没有成员独立 MOD 提交协议。本轮保留并报告。" });
            self.ready = true; Call("RenderMultiplayerRoomV2");
            Require(!Get<Button>("multiplayerRoomVoteButton").interactable && !Get<Button>("multiplayerRoomLayoutButton").interactable,
                "ready state still permits vote/layout");
            Require(Get<Button>("multiplayerRoomReadyButton").GetComponentInChildren<Text>().text == "取消准备", "cancel-ready label missing");
            Inventory(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "常驻房间/已准备");
            Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "room-persistent-ready", false);
            room.state = "readyCountdown"; Call("RenderMultiplayerRoomV2");
            Require(!Get<Button>("multiplayerRoomReadyButton").interactable, "countdown can be cancelled");
            Inventory(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "常驻房间/倒计时");
            Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "room-persistent-countdown", false);
            room.persistent = false; room.state = "lobby"; room.hostPlayerId = "audit-self";
            room.roomId = "000123"; room.roomName = "个人按钮测试"; room.maxPlayers = 8; room.selectionMode = "host";
            room.song = null; self.ready = false; self.isHost = true;
            Call("RenderMultiplayerRoomV2");
            Require(Get<Button>("multiplayerRoomSongButton").interactable, "personal room host cannot select first song");
            Inventory(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "个人房间/首次选歌");
            Capture(Get<Dictionary<AppPage, RectTransform>>("pages")[AppPage.MultiplayerRoom], "room-personal-first-song", false);
            Set("activeMultiplayerRoom", null); Set("currentPage", AppPage.Settings);
        }

        private static void VerifyDsp()
        {
            Set("currentPage", AppPage.Settings); RenderCategory("音频");
            var settings = Get<AppSettings>("settings"); settings.DspBufferSize = 128;
            foreach (int expected in new[] { 256, 512, 1024, 1024 }) {
                Call("ChangeDspBuffer", 1);
                Require(settings.DspBufferSize == expected && AppSettings.Load().DspBufferSize == expected, "DSP next/reload");
            }
            foreach (int expected in new[] { 512, 256, 128, 128 }) {
                Call("ChangeDspBuffer", -1);
                Require(settings.DspBufferSize == expected && AppSettings.Load().DspBufferSize == expected, "DSP previous/reload");
            }
            foreach (string name in new[] { "Audio/delay_ready", "Audio/se_touch_answer" })
                Require(Resources.Load<AudioClip>(name) != null, "missing settings preview sound " + name);
        }

        private static void VerifyOnlineForms()
        {
            foreach (string method in new[] { "ToggleMultiplayerFilterIdleV2", "ToggleMultiplayerFilterNotFullV2", "ToggleMultiplayerFilterUnencryptedV2" }) Call(method);
            Require(Get<bool>("multiplayerFilterIdleOnly") && Get<bool>("multiplayerFilterNotFullOnly") && Get<bool>("multiplayerFilterUnencryptedOnly"), "filter toggles failed");
            foreach (string expected in new[] { "personal", "persistent", "" }) {
                Call("CycleMultiplayerFilterRoomTypeV2"); Require(Get<string>("multiplayerFilterRoomType") == expected, "room-type filter cycle failed");
            }
            foreach (string expected in new[] { "casual", "normal", "" }) {
                Call("CycleMultiplayerFilterModeV2"); Require(Get<string>("multiplayerFilterMode") == expected, "mode filter cycle failed");
            }
            Call("ClearMultiplayerFiltersV2"); Require(!Get<bool>("multiplayerFilterIdleOnly"), "clear filters failed");
            Set("multiplayerCreateMaxPlayers", 2);
            for (int i = 0; i < 10; i++) Call("ChangeMultiplayerCreatePlayersV2", 1);
            Require(Get<int>("multiplayerCreateMaxPlayers") == 8, "personal capacity exceeds eight");
            for (int i = 0; i < 10; i++) Call("ChangeMultiplayerCreatePlayersV2", -1);
            Require(Get<int>("multiplayerCreateMaxPlayers") == 2, "personal capacity falls below two");
            string mode = Get<string>("multiplayerCreateMode"); Call("CycleMultiplayerCreateModeV2");
            Require(Get<string>("multiplayerCreateMode") != mode, "create mode toggle failed");
            string selection = Get<string>("multiplayerCreateSelection"); Call("CycleMultiplayerCreateSelectionV2");
            Require(Get<string>("multiplayerCreateSelection") != selection, "selection toggle failed");
            bool listed = Get<bool>("multiplayerCreateListed"); Call("ToggleMultiplayerCreateListedV2");
            Require(Get<bool>("multiplayerCreateListed") != listed, "listed toggle failed");
            Get<InputField>("multiplayerCreateRoomNameInput").SetTextWithoutNotify("");
            Call("SubmitCreateMultiplayerRoomV2"); Require(Get<Text>("multiplayerBrowserStatus").text.Contains("房间名称"), "empty room name accepted");
            Get<InputField>("multiplayerCreateRoomNameInput").SetTextWithoutNotify("Audit");
            Get<InputField>("multiplayerCreatePasswordInput").SetTextWithoutNotify("123");
            Call("SubmitCreateMultiplayerRoomV2"); Require(Get<Text>("multiplayerBrowserStatus").text.Contains("4 位"), "short password accepted");
        }

        private static void VerifyLanguage()
        {
            Set("currentPage", AppPage.Settings);
            Get<AppSettings>("settings").LanguageMode = -1;
            foreach (int expected in new[] { 0, 1, 2, 3, -1 }) {
                Call("ChangeLanguage", 1); canvas = Get<Canvas>("canvas");
                Require(AppSettings.Load().LanguageMode == expected &&
                    UiLocalization.Current == Get<AppSettings>("settings").EffectiveLanguage, "language cycle/save/rebuild failed");
            }
            Call("ChangeLanguage", -1); canvas = Get<Canvas>("canvas");
            Require(AppSettings.Load().LanguageMode == 3, "previous language wrap failed");
            Call("ResetSettings"); canvas = Get<Canvas>("canvas");
            Require(UiLocalization.Current == Get<AppSettings>("settings").EffectiveLanguage &&
                AppSettings.Load().LanguageMode == -1, "reset leaves UI in old language");
        }

        private static void VerifyReset()
        {
            var dirty = new AppSettings { AnswerSoundOffsetMs = 43, ShowGameplayFps = false,
                ShowInputBoundary = false, ShowTouchpadVisual = false, TouchHighlightBrightness = 176,
                EditorGuideLengthPercent = 57, GameplayBelowNoteMetricMode = 10 };
            // Dirty every primitive preference so the reset regression cannot pass
            // just because an untested field already held its default value.
            foreach (FieldInfo field in typeof(AppSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (field.FieldType == typeof(bool)) field.SetValue(dirty, !(bool)field.GetValue(dirty));
                else if (field.FieldType == typeof(int)) field.SetValue(dirty, (int)field.GetValue(dirty) + 17);
                else if (field.FieldType == typeof(string)) field.SetValue(dirty, "audit-reset-sentinel");
            }
            dirty.GameplayLayoutOverrides.Add(new GameplayLayoutOverride { SkinId = "audit", ElementId = "comboHud", X = .1f });
            dirty.Reset();
            var defaults = new AppSettings();
            foreach (FieldInfo field in typeof(AppSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                    Require(Equals(field.GetValue(dirty), field.GetValue(defaults)), "reset left stale " + field.Name);
            }
            Require(dirty.GameplayLayoutOverrides.Count == 0, "reset left skin layout overrides");
            Require(JsonUtility.ToJson(AppSettings.Load()) == JsonUtility.ToJson(defaults), "reset did not persist defaults");
        }

        private static void Capture(RectTransform page, string name, bool bottom)
        {
            Directory.CreateDirectory("Build/ui-audit/screenshots");
            Transform[] children = canvas.transform.Cast<Transform>().ToArray();
            bool[] states = children.Select(t => t.gameObject.activeSelf).ToArray();
            var obj = new GameObject("Audit screenshot camera", typeof(Camera));
            Camera camera = obj.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
            camera.orthographicSize = 540; camera.aspect = 1920f / 1080;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            canvas.worldCamera = camera;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            Vector2 scroll = Get<RectTransform>("settingsBody") != null ? Get<RectTransform>("settingsBody").anchoredPosition : Vector2.zero;
            try {
                foreach (Transform child in children) child.gameObject.SetActive(false);
                page.gameObject.SetActive(true);
                var group = page.GetComponent<CanvasGroup>();
                float oldAlpha = group != null ? group.alpha : 1;
                bool oldInteractable = group != null && group.interactable, oldBlocks = group != null && group.blocksRaycasts;
                if (group != null) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
                if (bottom && Get<RectTransform>("settingsBody") != null) {
                    var body = Get<RectTransform>("settingsBody");
                    body.anchoredPosition = new Vector2(0, Mathf.Max(0, body.rect.height - Get<ScrollRect>("settingsScroll").viewport.rect.height));
                }
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                // Graphic.depth/culling is populated by the render; sampling it
                // before the first camera pass creates false no-target failures.
                camera.Render();
                VerifyPointerTargets(page, camera, name);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes("Build/ui-audit/screenshots/" + name + ".png", image.EncodeToPNG());
                if (group != null) { group.alpha = oldAlpha; group.interactable = oldInteractable; group.blocksRaycasts = oldBlocks; }
            }
            finally {
                for (int i = 0; i < children.Length; i++) children[i].gameObject.SetActive(states[i]);
                if (Get<RectTransform>("settingsBody") != null) Get<RectTransform>("settingsBody").anchoredPosition = scroll;
                RenderTexture.active = previous; canvas.worldCamera = null;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private static void VerifyPointerTargets(RectTransform page, Camera camera, string name)
        {
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            foreach (Selectable control in page.GetComponentsInChildren<Selectable>()) {
                if (!control.IsInteractable()) continue;
                var rect = (RectTransform)control.transform;
                Vector2 pointer = camera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
                if (pointer.x < 0 || pointer.y < 0 || pointer.x > 1280 || pointer.y > 720) continue;
                if (control.GetComponentsInParent<RectMask2D>().Any(mask => !RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)mask.transform, pointer, camera))) continue;
                Check("点击命中/" + name + "/" + PathOf(rect), () => {
                    var hits = new List<RaycastResult>();
                    raycaster.Raycast(new PointerEventData(EventSystem.current) { position = pointer }, hits);
                    Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Selectable>() == control,
                        "button center blocked by " + (hits.Count == 0 ? "no raycast target" : PathOf(hits[0].gameObject.transform)));
                });
            }
        }

        private static void Inventory(Transform parent, string page)
        {
            foreach (Selectable control in parent.GetComponentsInChildren<Selectable>(true)) {
                if (control.GetComponentInParent<Dropdown>() != null && !(control is Dropdown)) continue;
                string detail = "";
                if (control is Slider s) detail = "范围 " + s.minValue + ".." + s.maxValue;
                if (control is Dropdown d) detail = string.Join(" / ", d.options.Select(o => o.text));
                if (control is InputField i) detail = "字符上限 " + i.characterLimit + "; " + i.contentType;
                output.entries.Add(new Entry { page = page, path = PathOf(control.transform), kind = control.GetType().Name,
                    label = string.Join(" | ", control.GetComponentsInChildren<Text>().Select(t => t.text)),
                    enabled = control.interactable, active = control.gameObject.activeSelf,
                    result = "已生成；功能结果看独立测试记录", detail = detail });
            }
        }
        private static void Check(string name, Action action)
        {
            try { action(); output.entries.Add(new Entry { page = "测试", path = name, result = "通过", detail = "Unity Editor 隔离回调/状态/持久化测试" }); }
            catch (Exception e) { failures++; output.entries.Add(new Entry { page = "测试", path = name, result = "失败", detail = e.ToString() }); Debug.LogWarning(name + ": " + e.GetBaseException().Message); }
        }
        private static string PathOf(Transform t) { string path = t.name; while (t.parent != null && t.parent != canvas.transform) { t = t.parent; path = t.name + "/" + path; } return path; }
        private static T Get<T>(string name) => (T)typeof(AppUiShell).GetField(name, Private).GetValue(shell);
        private static void Set(string name, object value) => typeof(AppUiShell).GetField(name, Private).SetValue(shell, value);
        private static object Call(string name, params object[] values) => typeof(AppUiShell).GetMethod(name, Private).Invoke(shell, values);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
