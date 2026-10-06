using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ChunithmMobile.Runtime
{
    // UI 数据模型集中定义页面、设置、MOD、歌曲选择和结算所需的数据。
    // 这些对象描述状态，不直接操作 Unity 控件；页面刷新由 AppUiShell 负责。
    public enum AppPage { Loading, Home, SongSelect, DataManagement, Multiplayer, MultiplayerBrowser, MultiplayerRoom, MultiplayerGroupResults, Gameplay, Settings, Results, DelayCalibrationResults }
    public enum SongSortMode { Category, Level }
    public enum AppLanguage { SimplifiedChinese = 0, TraditionalChinese = 1, English = 2, Japanese = 3 }

    /// <summary>
    /// Persists song-level pins independently from chart difficulty and list sort mode.
    /// A song ID is the stable key, so every chart belonging to the same song follows
    /// the pin in category, level, search and duplicate-difficulty lists.
    /// </summary>
    public static class SongPinStore
    {
        public const string PreferenceKey = "chunithm_mobile_pinned_songs_v1";

        [Serializable]
        private sealed class State
        {
            public string[] SongIds = Array.Empty<string>();
        }

        private static HashSet<string> pinnedIds;

        public static bool IsPinned(SongEntry song)
        {
            string key = KeyFor(song);
            return !string.IsNullOrWhiteSpace(key) && Pins().Contains(key);
        }

        public static bool Toggle(SongEntry song)
        {
            string key = KeyFor(song);
            if (string.IsNullOrWhiteSpace(key)) return false;
            HashSet<string> pins = Pins();
            bool pinned;
            if (pins.Contains(key)) { pins.Remove(key); pinned = false; }
            else { pins.Add(key); pinned = true; }
            Save();
            return pinned;
        }

        /// <summary>
        /// Drops pins for songs no longer present after an import or deletion.
        /// Returning the change state lets callers avoid an unnecessary preference write.
        /// </summary>
        public static bool Prune(IEnumerable<SongEntry> songs)
        {
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SongEntry song in songs ?? Array.Empty<SongEntry>())
            {
                string key = KeyFor(song);
                if (!string.IsNullOrWhiteSpace(key)) available.Add(key);
            }
            HashSet<string> pins = Pins();
            int before = pins.Count;
            pins.RemoveWhere(key => !available.Contains(key));
            if (pins.Count == before) return false;
            Save();
            return true;
        }

        // Stable fallback keeps hand-created/test songs pin-able even without Music.xml.
        public static string KeyFor(SongEntry song)
        {
            if (song == null) return "";
            return !string.IsNullOrWhiteSpace(song.Id) ? song.Id.Trim() : (song.Root ?? "").Trim();
        }

        private static HashSet<string> Pins()
        {
            if (pinnedIds != null) return pinnedIds;
            pinnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string json = UnityEngine.PlayerPrefs.GetString(PreferenceKey, "");
            if (string.IsNullOrWhiteSpace(json)) return pinnedIds;
            try
            {
                State state = UnityEngine.JsonUtility.FromJson<State>(json);
                foreach (string id in state?.SongIds ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(id)) pinnedIds.Add(id.Trim());
            }
            catch { pinnedIds.Clear(); }
            return pinnedIds;
        }

        private static void Save()
        {
            var state = new State { SongIds = new List<string>(Pins()).ToArray() };
            UnityEngine.PlayerPrefs.SetString(PreferenceKey, UnityEngine.JsonUtility.ToJson(state));
            UnityEngine.PlayerPrefs.Save();
        }
    }

    [Serializable]
    public sealed class ModSettings
    {
        public bool AutoPlay;
        public bool AirAreaAuto;
        public bool DirectionalAirAuto;
        public bool AirLongNoMotion;
        public bool NonMissJusticeCritical;
        public bool MirrorChart;
        public bool FlickAllTap;
        public bool MineImmunity;
        public bool FullscreenTouchpad;
        public bool UniformSpeed;
        public bool FullscreenTouchpadRestoreAirAreaAuto;
        public bool FullscreenTouchpadRestoreDirectionalAirAuto;
        public float MusicRate = 1f;
        public bool HasAnyMod => AutoPlay || AirAreaAuto || DirectionalAirAuto || AirLongNoMotion || NonMissJusticeCritical ||
                                  MirrorChart || FlickAllTap || MineImmunity || FullscreenTouchpad || UniformSpeed || Math.Abs(MusicRate - 1f) >= .001f;
        public bool RecordsScore => !AutoPlay && !AirAreaAuto && !DirectionalAirAuto &&
                                    !MineImmunity && !NonMissJusticeCritical && !FlickAllTap && !FullscreenTouchpad &&
                                    Math.Abs(MusicRate - 1f) < .001f;
    }

    [Serializable]
    public sealed class AppSettings
    {
        public int SettingsVersion = 25;
        // -1 follows the system. 0 is Simplified Chinese, 1 is Traditional Chinese, 2 is English, 3 is Japanese.
        public int LanguageMode = -1;
        public int MusicVolume = 100;
        public int BackgroundMusicVolume = 100;
        public bool KeySoundEnabled = true;
        public int KeySoundVolume = 80;
        public bool AnswerSoundEnabled = true;
        public int AnswerSoundVolume = 80;
        public int AnswerSoundOffsetMs;
        public int DspBufferSize = 512;
        public bool ShowGameplayFps = true;
        // MusicOffsetMs moves the song audio relative to the chart clock.
        // JudgeOffsetMs moves only the input judgement reference point.
        public int MusicOffsetMs;
        public int JudgeOffsetMs;
        public int GameplayPauseX = 5;
        public int GameplayPauseY = 93;
        public int GameplayPauseScale = 100;
        public bool PauseRequiresDoubleTap;
        public int BackgroundBrightness = 0;
        public bool ShowJusticeCriticalMs = true;
        public bool ShowJusticeMs = true;
        public bool ShowAttackMs = true;
        public bool ExcludeMissFromAverage = true;
        public bool ShowTouchFeedback = true;
        public bool ShowAirGuide = true;
        public bool ShowInputBoundary = true;
        public bool ShowTouchpadVisual = true;
        public bool ShowGameplaySongInfo = true;
        // 0 current combo, 1 remaining combo, 2 current score,
        // 3..8 remaining allowed loss for SSS+, SSS, SS+, SS, S+, S;
        // 9 accumulated score loss from the 1,010,000 full-JC result;
        // 10 distance to the saved personal best score.
        public int GameplayBelowNoteMetricMode;
        // 0 SSS+, 1 SSS, 2 SS+, 3 SS, 4 S+, 5 S.
        public int GameplayTargetRank;
        public int JacketBackgroundOpacity = 42;
        public int TouchpadHeight = 17;
        public int TouchpadWidth = 100;
        public int TouchpadOpacity = 100;
        public int TouchEffectOpacity = 100;
        public int TouchHighlightBrightness = 100;
        // Logical contact width in tenths of one mobile touchpad lane (5..30).
        public int TouchContactRangeTenths = 10;
        // Shared display length for every draggable guide line in the layout editor.
        public int EditorGuideLengthPercent = 100;
        public int GroundAreaHeight = 17;
        public int AirAreaBottom = 17;
        public int AirAreaTop = 96;
        public int TrackVerticalOffset = 12;
        public int TrackWidth = 100;
        public int TrackTopWidth = 100;
        public bool TrackMaskEnabled;
        public int TrackMaskHeight = 16;
        public int TrackMaskOpacity = 90;
        public int NoteSpeedOption = OriginalNoteSpeed.DefaultOption;
        // Retained only to migrate settings written by builds before the original speed table was restored.
        public int NoteSpeed = 100;
        public int GameplayHudX = 88;
        public int GameplayHudY = 89;
        public int GameplayScoreScale = 100;
        public int GameplayComboX = 50;
        public int GameplayComboY = 51;
        public int GameplayComboScale = 100;
        public int GameplayJudgementX = 50;
        public int GameplayJudgementY = 41;
        public int GameplayJudgementScale = 100;
        public int GameplaySongInfoX = 16;
        public int GameplaySongInfoY = 91;
        public int GameplaySongInfoScale = 100;
        public bool EmojiBackground;
        public bool HighContrastUi;
        // 0 note trails, 1 emoji trails, 2 imported image trails.
        public int BackgroundSkinMode;
        public string GameplaySkinId = GameplaySkinManager.BuiltinId;
        // Per-skin layout edits made in the in-game visual editor. The package
        // remains read-only; an override is applied after the package layout
        // and can be removed with “恢复默认布局”.
        public List<GameplayLayoutOverride> GameplayLayoutOverrides = new List<GameplayLayoutOverride>();

        private const string Key = "chunithm_mobile_settings_v1";

        public static AppSettings Load()
        {
            string json = UnityEngine.PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrWhiteSpace(json)) return new AppSettings();
            try {
                AppSettings value = UnityEngine.JsonUtility.FromJson<AppSettings>(json) ?? new AppSettings();
                int loadedSettingsVersion = value.SettingsVersion;
                if (value.SettingsVersion < 2) {
                    value.NoteSpeedOption = OriginalNoteSpeed.FromLegacyPercent(value.NoteSpeed <= 0 ? 100 : value.NoteSpeed);
                }
                if (loadedSettingsVersion >= 2 && loadedSettingsVersion < 25)
                    value.NoteSpeedOption = OriginalNoteSpeed.FromLegacyOption(value.NoteSpeedOption);
                if (value.SettingsVersion < 3) {
                    value.KeySoundEnabled = true;
                    value.KeySoundVolume = 80;
                    value.AnswerSoundEnabled = true;
                value.AnswerSoundVolume = 80;
                }
                if (value.SettingsVersion < 4) value.GameplaySongInfoScale = 100;
                if (value.SettingsVersion < 5) {
                    value.TrackTopWidth = 100;
                    value.TrackMaskEnabled = false;
                    value.TrackMaskHeight = 16;
                    value.TrackMaskOpacity = 90;
                }
                if (value.SettingsVersion < 6) value.EmojiBackground = false;
                if (value.SettingsVersion < 7) value.BackgroundMusicVolume = 100;
                if (value.SettingsVersion < 8) value.LanguageMode = -1;
                if (value.SettingsVersion < 9 && value.LanguageMode >= 0) {
                    // Builds before the traditional-Chinese option used 0 for
                    // simplified Chinese and 1 for English. Keep simplified
                    // at 0 and move only the old English value to its new slot.
                    if (value.LanguageMode == 1) value.LanguageMode = (int)AppLanguage.English;
                }
                if (value.SettingsVersion < 10) value.TouchpadWidth = 100;
                if (value.SettingsVersion < 11) value.DspBufferSize = 512;
                if (value.SettingsVersion < 12) {
                    // Preserve the old single offset as music latency. The
                    // new judgement offset starts neutral.
                    value.MusicOffsetMs = value.JudgeOffsetMs;
                    value.JudgeOffsetMs = 0;
                    value.GameplayPauseX = 5;
                    value.GameplayPauseY = 93;
                    value.GameplayPauseScale = 100;
                    value.PauseRequiresDoubleTap = false;
                }
                if (value.SettingsVersion < 14)
                    value.BackgroundSkinMode = value.EmojiBackground ? 1 : 0;
                if (value.SettingsVersion < 16) {
                    value.TouchpadOpacity = 100;
                    value.TouchEffectOpacity = 100;
                    value.TouchContactRangeTenths = 10;
                }
                if (value.SettingsVersion < 17) value.GameplaySkinId = GameplaySkinManager.BuiltinId;
                if (value.SettingsVersion < 19) {
                    value.GameplayJudgementX = value.GameplayComboX;
                    value.GameplayJudgementScale = value.GameplayComboScale > 0 ? value.GameplayComboScale : 100;
                    value.GameplayJudgementY = UnityEngine.Mathf.Clamp(value.GameplayComboY -
                        UnityEngine.Mathf.RoundToInt(10 * value.GameplayJudgementScale / 100f), 5, 95);
                }
                if (value.SettingsVersion < 20) value.ShowGameplayFps = false;
                if (value.SettingsVersion < 21) value.BackgroundBrightness = 0;
                if (value.SettingsVersion < 22) value.EditorGuideLengthPercent = 100;
                if (value.SettingsVersion < 23) {
                    value.ShowGameplayFps = true;
                    value.GameplayBelowNoteMetricMode = 3;
                }
                if (value.SettingsVersion < 24) value.GameplayBelowNoteMetricMode = 3;
                value.SettingsVersion = 25;
                value.BackgroundBrightness = UnityEngine.Mathf.Clamp(value.BackgroundBrightness, 0, 100);
                value.GameplayJudgementX = UnityEngine.Mathf.Clamp(value.GameplayJudgementX, 5, 95);
                value.GameplayJudgementY = UnityEngine.Mathf.Clamp(value.GameplayJudgementY, 5, 95);
                value.GameplayJudgementScale = UnityEngine.Mathf.Clamp(value.GameplayJudgementScale, 50, 180);
                value.LanguageMode = UnityEngine.Mathf.Clamp(value.LanguageMode, -1, 3);
                value.NoteSpeedOption = OriginalNoteSpeed.ClampOption(value.NoteSpeedOption);
                value.MusicVolume = UnityEngine.Mathf.Clamp(value.MusicVolume, 0, 100);
                value.BackgroundMusicVolume = UnityEngine.Mathf.Clamp(value.BackgroundMusicVolume, 0, 100);
                value.KeySoundVolume = UnityEngine.Mathf.Clamp(value.KeySoundVolume, 0, 100);
                value.AnswerSoundVolume = UnityEngine.Mathf.Clamp(value.AnswerSoundVolume, 0, 100);
                value.DspBufferSize = NormalizeDspBufferSize(value.DspBufferSize);
                if (value.NoteSpeed <= 0) {
                    value.NoteSpeed = 100;
                    value.TrackVerticalOffset = 12;
                    value.AirAreaTop = 96;
                }
                if (value.TrackWidth <= 0) value.TrackWidth = 100;
                if (value.TrackTopWidth <= 0) value.TrackTopWidth = 100;
                if (value.TrackMaskHeight <= 0) value.TrackMaskHeight = 16;
                if (value.TrackMaskOpacity <= 0) value.TrackMaskOpacity = 90;
                if (value.GameplayScoreScale <= 0) value.GameplayScoreScale = 100;
                if (value.GameplayComboScale <= 0) value.GameplayComboScale = 100;
                if (value.GameplaySongInfoScale <= 0) value.GameplaySongInfoScale = 100;
                value.TrackWidth = UnityEngine.Mathf.Clamp(value.TrackWidth, 60, 140);
                value.TrackTopWidth = UnityEngine.Mathf.Clamp(value.TrackTopWidth, 45, 160);
                value.TrackMaskHeight = UnityEngine.Mathf.Clamp(value.TrackMaskHeight, 4, 45);
                value.TrackMaskOpacity = UnityEngine.Mathf.Clamp(value.TrackMaskOpacity, 20, 100);
                value.GameplayScoreScale = UnityEngine.Mathf.Clamp(value.GameplayScoreScale, 50, 180);
                value.GameplayComboScale = UnityEngine.Mathf.Clamp(value.GameplayComboScale, 50, 180);
                value.GameplaySongInfoScale = UnityEngine.Mathf.Clamp(value.GameplaySongInfoScale, 50, 180);
                value.AirAreaTop = UnityEngine.Mathf.Clamp(value.AirAreaTop, value.AirAreaBottom + 5, 100);
                int sharedBoundary = UnityEngine.Mathf.Clamp(value.TouchpadHeight, 8, 45);
                value.TouchpadHeight = sharedBoundary;
                value.TouchpadWidth = UnityEngine.Mathf.Clamp(value.TouchpadWidth, 60, 100);
                value.TouchpadOpacity = UnityEngine.Mathf.Clamp(value.TouchpadOpacity, 0, 100);
                value.TouchEffectOpacity = UnityEngine.Mathf.Clamp(value.TouchEffectOpacity, 0, 100);
                value.TouchHighlightBrightness = UnityEngine.Mathf.Clamp(value.TouchHighlightBrightness, 0, 200);
                value.TouchContactRangeTenths = UnityEngine.Mathf.Clamp(value.TouchContactRangeTenths, 5, 30);
                value.EditorGuideLengthPercent = UnityEngine.Mathf.Clamp(value.EditorGuideLengthPercent, 50, 150);
                value.GroundAreaHeight = sharedBoundary;
                value.AirAreaBottom = sharedBoundary;
                value.AirAreaTop = UnityEngine.Mathf.Clamp(value.AirAreaTop, sharedBoundary + 5, 100);
                value.MusicOffsetMs = UnityEngine.Mathf.Clamp(value.MusicOffsetMs, -500, 500);
                value.JudgeOffsetMs = UnityEngine.Mathf.Clamp(value.JudgeOffsetMs, -500, 500);
                value.MusicOffsetMs = NormalizeGameplayLatencyOffset(value.MusicOffsetMs);
                value.JudgeOffsetMs = NormalizeGameplayLatencyOffset(value.JudgeOffsetMs);
                value.AnswerSoundOffsetMs = UnityEngine.Mathf.Clamp(value.AnswerSoundOffsetMs, -500, 500);
                value.AnswerSoundOffsetMs = NormalizeGameplayLatencyOffset(value.AnswerSoundOffsetMs);
                value.BackgroundSkinMode = UnityEngine.Mathf.Clamp(value.BackgroundSkinMode, 0, 2);
                if (string.IsNullOrWhiteSpace(value.GameplaySkinId)) value.GameplaySkinId = GameplaySkinManager.BuiltinId;
                if (value.GameplayLayoutOverrides == null) value.GameplayLayoutOverrides = new List<GameplayLayoutOverride>();
                value.GameplayPauseX = UnityEngine.Mathf.Clamp(value.GameplayPauseX, 5, 95);
                value.GameplayPauseY = UnityEngine.Mathf.Clamp(value.GameplayPauseY, 8, 95);
                value.GameplayPauseScale = UnityEngine.Mathf.Clamp(value.GameplayPauseScale, 50, 180);
                value.GameplayBelowNoteMetricMode = UnityEngine.Mathf.Clamp(value.GameplayBelowNoteMetricMode, 0, 10);
                value.GameplayTargetRank = UnityEngine.Mathf.Clamp(value.GameplayTargetRank, 0, 5);
                return value;
            }
            catch { return new AppSettings(); }
        }

        public AppLanguage EffectiveLanguage
        {
            get {
                if (LanguageMode == (int)AppLanguage.SimplifiedChinese) return AppLanguage.SimplifiedChinese;
                if (LanguageMode == (int)AppLanguage.TraditionalChinese) return AppLanguage.TraditionalChinese;
                if (LanguageMode == (int)AppLanguage.English) return AppLanguage.English;
                if (LanguageMode == (int)AppLanguage.Japanese) return AppLanguage.Japanese;
                if (UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseTraditional)
                    return AppLanguage.TraditionalChinese;
                if (UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.ChineseSimplified)
                    return AppLanguage.SimplifiedChinese;
                if (UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Japanese)
                    return AppLanguage.Japanese;
                return AppLanguage.English;
            }
        }

        public string LanguageModeLabel
        {
            get {
                if (LanguageMode == (int)AppLanguage.SimplifiedChinese) return "简体中文";
                if (LanguageMode == (int)AppLanguage.TraditionalChinese) return "繁體中文";
                if (LanguageMode == (int)AppLanguage.English) return "English";
                if (LanguageMode == (int)AppLanguage.Japanese) return "日本語";
                if (EffectiveLanguage == AppLanguage.TraditionalChinese) return "跟隨系統（繁體中文）";
                if (EffectiveLanguage == AppLanguage.SimplifiedChinese) return "跟隨系統（简体中文）";
                if (EffectiveLanguage == AppLanguage.Japanese) return "システム（日本語）";
                return "跟隨系統（English）";
            }
        }
        public void Save()
        {
            UnityEngine.PlayerPrefs.SetString(Key, UnityEngine.JsonUtility.ToJson(this));
            UnityEngine.PlayerPrefs.Save();
        }

        public void Reset()
        {
            LanguageMode = -1;
            MusicVolume = 100;
            BackgroundMusicVolume = 100;
            KeySoundEnabled = true;
            KeySoundVolume = 80;
            AnswerSoundEnabled = true;
            AnswerSoundVolume = 80;
            DspBufferSize = 512;
            MusicOffsetMs = 0;
            JudgeOffsetMs = 0;
            GameplayPauseX = 5;
            GameplayPauseY = 93;
            GameplayPauseScale = 100;
            PauseRequiresDoubleTap = false;
            BackgroundBrightness = 0;
            ShowJusticeCriticalMs = true;
            ShowJusticeMs = true;
            ShowAttackMs = true;
            ExcludeMissFromAverage = true;
            ShowTouchFeedback = true;
            ShowAirGuide = true;
            ShowGameplaySongInfo = true;
            JacketBackgroundOpacity = 42;
            TouchpadHeight = 17;
            TouchpadWidth = 100;
            TouchpadOpacity = 100;
            TouchEffectOpacity = 100;
            TouchContactRangeTenths = 10;
            GroundAreaHeight = 17;
            AirAreaBottom = 17;
            AirAreaTop = 96;
            TrackVerticalOffset = 12;
            TrackWidth = 100;
            TrackTopWidth = 100;
            TrackMaskEnabled = false;
            TrackMaskHeight = 16;
            TrackMaskOpacity = 90;
            NoteSpeedOption = OriginalNoteSpeed.DefaultOption;
            NoteSpeed = 100;
            GameplayHudX = 88;
            GameplayHudY = 89;
            GameplayScoreScale = 100;
            GameplayComboX = 50;
            GameplayComboY = 51;
            GameplayComboScale = 100;
            GameplayJudgementX = 50;
            GameplayJudgementY = 41;
            GameplayJudgementScale = 100;
            GameplaySongInfoX = 16;
            GameplaySongInfoY = 91;
            GameplaySongInfoScale = 100;
            GameplayBelowNoteMetricMode = 3;
            GameplayTargetRank = 0;
            EmojiBackground = false;
            BackgroundSkinMode = 0;
            HighContrastUi = false;
            GameplaySkinId = GameplaySkinManager.BuiltinId;
            Save();
        }

        public static int NormalizeLatencyOffset(int value)
        {
            return UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(value / 5f) * 5, -500, 500);
        }

        public static int NormalizeGameplayLatencyOffset(int value)
        {
            return UnityEngine.Mathf.Clamp(value, -500, 500);
        }

        public static int NormalizeDspBufferSize(int value)
        {
            if (value <= 128) return 128;
            if (value <= 256) return 256;
            if (value <= 512) return 512;
            return 1024;
        }
    }

    [Serializable]
    public sealed class GameplayLayoutOverride
    {
        public string SkinId;
        public string ElementId;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Visible = true;
    }

    [Serializable]
    public sealed class UiSessionState
    {
        public string SongId = "";
        public string SongRoot = "";
        public string ChartPath = "";
        public string Difficulty = "";
        public int SortMode;

        private const string Key = "chunithm_mobile_ui_session_v1";

        public static UiSessionState Load()
        {
            string json = UnityEngine.PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrWhiteSpace(json)) return new UiSessionState();
            try { return UnityEngine.JsonUtility.FromJson<UiSessionState>(json) ?? new UiSessionState(); }
            catch { return new UiSessionState(); }
        }

        public void Save()
        {
            UnityEngine.PlayerPrefs.SetString(Key, UnityEngine.JsonUtility.ToJson(this));
            UnityEngine.PlayerPrefs.Save();
        }
    }

    [Serializable]
    public sealed class ResultViewModel
    {
        public string SongName = "ユクエシレズ";
        public string Artist = "こじろー";
        public string ChartDesigner = "-";
        public string Difficulty = "MASTER";
        public string Level = "11";
        public int Score = 1009000;
        public int JusticeCritical = 1080;
        public int Justice = 32;
        public int Attack = 8;
        public int Miss = 4;
        public double FastAverageMs = -12.4;
        public double LateAverageMs = 15.7;
        public double TotalAverageMs = 1.8;
        public string ModLabel = "NORMAL";

        public SongAchievement Achievement
        {
            get { return SongAchievementRules.FromCounts(JusticeCritical, Justice, Attack, Miss); }
        }

        public string Rank
        {
            get { return RankForScore(Score); }
        }

        public static string RankForScore(int score)
        {
            if (score >= 1009000) return "SSS+";
            if (score >= 1007500) return "SSS";
            if (score >= 1005000) return "SS+";
            if (score >= 1000000) return "SS";
            if (score >= 990000) return "S+";
            if (score >= 975000) return "S";
            if (score >= 950000) return "AAA";
            if (score >= 925000) return "AA";
            if (score >= 900000) return "A";
            if (score >= 800000) return "BBB";
            if (score >= 700000) return "BB";
            if (score >= 600000) return "B";
            if (score >= 500000) return "C";
            return "D";
        }
    }

    /// <summary>
    /// Best completed judgement achievement for one song chart. The numeric
    /// order is intentional so an achievement can only move upward over time.
    /// </summary>
    public enum SongAchievement
    {
        None = 0,
        FC = 1,
        AJ = 2,
        ACJ = 3
    }

    public static class SongAchievementRules
    {
        public static SongAchievement FromCounts(int justiceCritical, int justice, int attack, int miss)
        {
            if (justiceCritical < 0 || justice < 0 || attack < 0 || miss < 0) return SongAchievement.None;
            int total = justiceCritical + justice + attack + miss;
            if (total <= 0 || miss > 0) return SongAchievement.None;
            if (justiceCritical == total) return SongAchievement.ACJ;
            if (attack == 0) return SongAchievement.AJ;
            return SongAchievement.FC;
        }
    }

    public static class SongRecordStore
    {
        private const string LegacyPrefix = "chunithm_mobile_best_v1_";
        private const string BestPrefix = "chunithm_mobile_best_v2_";
        private const string LastPrefix = "chunithm_mobile_last_v2_";
        private const string AchievementPrefix = "chunithm_mobile_achievement_v1_";

        public static bool TryGet(string songRoot, string difficulty, string chartPath, out int score)
        {
            string key = Key(BestPrefix, songRoot, difficulty, chartPath);
            if (!UnityEngine.PlayerPrefs.HasKey(key)) {
                // Import the old per-difficulty record once. New records always
                // include chartPath so duplicate difficulties stay independent.
                string legacyKey = LegacyKey(songRoot, difficulty);
                if (!UnityEngine.PlayerPrefs.HasKey(legacyKey)) { score = 0; return false; }
                score = UnityEngine.PlayerPrefs.GetInt(legacyKey, 0);
                if (score > 0) {
                    UnityEngine.PlayerPrefs.SetInt(key, score);
                    UnityEngine.PlayerPrefs.Save();
                }
                return score > 0;
            }
            score = UnityEngine.PlayerPrefs.GetInt(key, 0);
            return score > 0;
        }

        public static bool SaveResult(string songRoot, string difficulty, string chartPath, int score,
            out int previousBest, out int previousLast)
        {
            TryGet(songRoot, difficulty, chartPath, out previousBest);
            string lastKey = Key(LastPrefix, songRoot, difficulty, chartPath);
            previousLast = UnityEngine.PlayerPrefs.GetInt(lastKey, 0);
            if (previousLast <= 0) previousLast = previousBest;
            if (score <= 0) return false;
            bool newBest = score > previousBest;
            UnityEngine.PlayerPrefs.SetInt(lastKey, score);
            if (newBest)
                UnityEngine.PlayerPrefs.SetInt(Key(BestPrefix, songRoot, difficulty, chartPath), score);
            UnityEngine.PlayerPrefs.Save();
            return newBest;
        }

        public static bool TryGetAchievement(string songRoot, string difficulty, string chartPath,
            out SongAchievement achievement)
        {
            string key = Key(AchievementPrefix, songRoot, difficulty, chartPath);
            int raw = UnityEngine.PlayerPrefs.GetInt(key, 0);
            if (raw < (int)SongAchievement.None || raw > (int)SongAchievement.ACJ)
            {
                achievement = SongAchievement.None;
                return false;
            }
            achievement = (SongAchievement)raw;
            return achievement != SongAchievement.None;
        }

        /// <summary>
        /// Stores the highest judgement achievement independently of the best
        /// score. A lower-scoring AJ/ACJ run must still upgrade an older FC.
        /// </summary>
        public static bool SaveAchievement(string songRoot, string difficulty, string chartPath,
            SongAchievement achievement)
        {
            if (achievement <= SongAchievement.None || achievement > SongAchievement.ACJ) return false;
            TryGetAchievement(songRoot, difficulty, chartPath, out SongAchievement previous);
            if (achievement <= previous) return false;
            UnityEngine.PlayerPrefs.SetInt(Key(AchievementPrefix, songRoot, difficulty, chartPath), (int)achievement);
            UnityEngine.PlayerPrefs.Save();
            return true;
        }

        private static string Key(string prefix, string songRoot, string difficulty, string chartPath)
        {
            string value = (songRoot ?? "") + "\n" + (difficulty ?? "") + "\n" + (chartPath ?? "");
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return prefix + text;
            }
        }

        private static string LegacyKey(string songRoot, string difficulty)
        {
            string value = (songRoot ?? "") + "\n" + (difficulty ?? "");
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return LegacyPrefix + text;
            }
        }
    }
}
