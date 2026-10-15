using System;
using System.Linq;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // Protects settings persistence, the version-25 speed migration boundary,
    // and the runtime pacing API. Physical device refresh rates are not tested.
    public static class FrameRateSettingsSelfTest
    {
        public static void Run()
        {
            const string key = "chunithm_mobile_settings_v1";
            bool had = PlayerPrefs.HasKey(key);
            string saved = PlayerPrefs.GetString(key);
            int previousTarget = Application.targetFrameRate;
            int previousSync = QualitySettings.vSyncCount;
            AppLanguage previousLanguage = UiLocalization.Current;
            try {
                Require(FrameRateSettings.Options.SequenceEqual(new[] {60,90,120,144,165,240,0}), "limit list changed");
                foreach (int limit in FrameRateSettings.Options)
                foreach (bool sync in new[] { false, true }) {
                    var settings = new AppSettings { FrameRateLimit = limit, VerticalSync = sync, NoteSpeedOption = 131 };
                    settings.Save();
                    settings = AppSettings.Load();
                    Require(settings.FrameRateLimit == limit && settings.VerticalSync == sync && settings.NoteSpeedOption == 131,
                        "frame settings or speed not preserved on reload");
                    FrameRateSettings.ApplyGameplayFrameRate(settings);
                    Require(Application.targetFrameRate == (limit == 0 ? int.MaxValue : limit) &&
                        QualitySettings.vSyncCount == (sync ? 1 : 0), "pacing API values incorrect");
                }
                FrameRateSettings.ApplyInterfaceFrameRate();
                Require(Application.targetFrameRate == int.MaxValue && QualitySettings.vSyncCount == 0,
                    "interface pacing was not restored after gameplay");
                PlayerPrefs.SetString(key, "{\"SettingsVersion\":25,\"NoteSpeedOption\":131}");
                var migrated = AppSettings.Load();
                Require(migrated.SettingsVersion == 26 && migrated.NoteSpeedOption == 131 &&
                    migrated.FrameRateLimit == 0 && !migrated.VerticalSync, "version-25 migration changed chart speed");
                Require(FrameRateSettings.NormalizeLimit(75) == 0, "unsupported cap accepted");
                Require(FrameRateSettings.Step(165, 1) == 240 && FrameRateSettings.Step(240, 1) == 0 &&
                    FrameRateSettings.Step(0, -1) == 240 && FrameRateSettings.Step(60, -1) == 60, "cap arrows skip options");
                foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) {
                    UiLocalization.SetLanguage(language);
                    Require(language == AppLanguage.SimplifiedChinese || UiLocalization.Translate("帧数上限") != "帧数上限",
                        "new frame setting translation missing: " + language);
                }
                Debug.Log("Frame rate settings regression passed: gameplay-only caps, interface restore, seven limits, both sync states, save/reload, migration, arrows, invalid cap and translations.");
            } finally {
                Application.targetFrameRate = previousTarget;
                QualitySettings.vSyncCount = previousSync;
                UiLocalization.SetLanguage(previousLanguage);
                if (had) PlayerPrefs.SetString(key, saved); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
