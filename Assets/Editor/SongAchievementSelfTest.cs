using System;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    // 成绩成就回归测试：确认 FC/AJ/ACJ 的判定、升级顺序和谱面键隔离。
    public static class SongAchievementSelfTest
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        public static void Run()
        {
            Require(SongAchievementRules.FromCounts(100, 0, 0, 0) == SongAchievement.ACJ,
                "all Justice Critical was not ACJ");
            Require(SongAchievementRules.FromCounts(99, 1, 0, 0) == SongAchievement.AJ,
                "Justice without Attack was not AJ");
            Require(SongAchievementRules.FromCounts(98, 0, 1, 0) == SongAchievement.FC,
                "Attack without Miss was not FC");
            Require(SongAchievementRules.FromCounts(98, 0, 0, 1) == SongAchievement.None,
                "Miss incorrectly produced an achievement");
            Require(SongAchievementRules.FromCounts(0, 0, 0, 0) == SongAchievement.None,
                "empty result incorrectly produced an achievement");
            Require(SongAchievementRules.FromCounts(-1, 0, 0, 0) == SongAchievement.None,
                "negative judgement count was accepted");

            Type shellType = typeof(AppUiShell);
            MethodInfo labelMethod = shellType.GetMethod("SongAchievementLabel", PrivateStatic);
            MethodInfo colorMethod = shellType.GetMethod("SongAchievementColor", PrivateStatic);
            MethodInfo configureMethod = shellType.GetMethod("ConfigureSongAchievement", PrivateStatic);
            Require((string)labelMethod.Invoke(null, new object[] { SongAchievement.ACJ }) == "ACJ" &&
                    (string)labelMethod.Invoke(null, new object[] { SongAchievement.AJ }) == "AJ" &&
                    (string)labelMethod.Invoke(null, new object[] { SongAchievement.FC }) == "FC",
                "achievement labels were changed");
            ResultViewModel result = new ResultViewModel {
                JusticeCritical = 100, Justice = 0, Attack = 0, Miss = 0
            };
            Require(result.Achievement == SongAchievement.ACJ, "result model did not expose ACJ");
            result.JusticeCritical = 99; result.Justice = 1;
            Require(result.Achievement == SongAchievement.AJ, "result model did not expose AJ");
            result.Justice = 0; result.Attack = 1;
            Require(result.Achievement == SongAchievement.FC, "result model did not expose FC");
            Color acjColor = (Color)colorMethod.Invoke(null, new object[] { SongAchievement.ACJ });
            Color ajColor = (Color)colorMethod.Invoke(null, new object[] { SongAchievement.AJ });
            Color fcColor = (Color)colorMethod.Invoke(null, new object[] { SongAchievement.FC });
            Require(acjColor.r > acjColor.b && acjColor.g > acjColor.b &&
                    ajColor.b > ajColor.r && ajColor.b > ajColor.g * .9f &&
                    fcColor.g > fcColor.r && fcColor.g > fcColor.b,
                "achievement colors do not match yellow/blue/green");
            var markerObject = new GameObject("Achievement marker", typeof(RectTransform), typeof(Text));
            try
            {
                Text marker = markerObject.GetComponent<Text>();
                configureMethod.Invoke(null, new object[] { marker, SongAchievement.ACJ });
                Require(marker.text == "ACJ" && marker.fontStyle == FontStyle.Bold,
                    "achievement marker is not bold or has the wrong text");
            }
            finally { UnityEngine.Object.DestroyImmediate(markerObject); }

            const string root = "achievement-self-test";
            const string difficulty = "MASTER";
            const string chart = "achievement-self-test.c2s";
            MethodInfo keyMethod = typeof(SongRecordStore).GetMethod("Key", PrivateStatic);
            string key = (string)keyMethod.Invoke(null, new object[] {
                "chunithm_mobile_achievement_v1_", root, difficulty, chart
            });
            bool hadKey = PlayerPrefs.HasKey(key);
            int oldValue = PlayerPrefs.GetInt(key, 0);
            try
            {
                PlayerPrefs.DeleteKey(key);
                Require(SongRecordStore.SaveAchievement(root, difficulty, chart, SongAchievement.FC),
                    "FC achievement was not saved");
                Require(SongRecordStore.TryGetAchievement(root, difficulty, chart, out SongAchievement saved) &&
                    saved == SongAchievement.FC, "saved FC achievement was not readable");
                Require(!SongRecordStore.SaveAchievement(root, difficulty, chart, SongAchievement.FC),
                    "same achievement was not idempotent");
                Require(SongRecordStore.SaveAchievement(root, difficulty, chart, SongAchievement.AJ),
                    "AJ did not upgrade FC");
                Require(SongRecordStore.SaveAchievement(root, difficulty, chart, SongAchievement.ACJ),
                    "ACJ did not upgrade AJ");
                Require(!SongRecordStore.SaveAchievement(root, difficulty, chart, SongAchievement.FC),
                    "lower achievement downgraded ACJ");
                Require(SongRecordStore.TryGetAchievement(root, difficulty, chart, out saved) &&
                    saved == SongAchievement.ACJ, "achievement priority was not retained");
            }
            finally
            {
                if (hadKey) PlayerPrefs.SetInt(key, oldValue);
                else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            Debug.Log("Song achievement regression passed: FC/AJ/ACJ rules, upgrade ordering and chart-key persistence.");
        }
    }
}
