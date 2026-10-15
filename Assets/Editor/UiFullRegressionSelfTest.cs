using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Runs the UI audit and independent rendering/input regressions in one Editor session.</summary>
    public static class UiFullRegressionSelfTest
    {
        public static void Run()
        {
            var results = new List<string>();
            int failed = 0;
            Action<string, Action> run = (name, test) => {
                try { test(); results.Add(name + "\tPASS"); }
                catch (Exception e) { failed++; results.Add(name + "\tFAIL\t" + e); Debug.LogWarning("UI suite: " + name + " failed: " + e); }
            };
            run(nameof(DdsTextureDecoderSelfTest), DdsTextureDecoderSelfTest.Run);
            run(nameof(LayoutFeedbackSelfTest), LayoutFeedbackSelfTest.Run);
            run(nameof(ModDialogSelfTest), ModDialogSelfTest.Run);
            run(nameof(SkinSettingsSelfTest), SkinSettingsSelfTest.Run);
            run(nameof(GameplayPauseSelfTest), GameplayPauseSelfTest.Run);
            run(nameof(SongListBucketSelfTest), SongListBucketSelfTest.Run);
            run(nameof(SongAchievementSelfTest), SongAchievementSelfTest.Run);
            run(nameof(GameplaySkinCustomDisplaySelfTest), GameplaySkinCustomDisplaySelfTest.Run);
            run(nameof(GameplaySkinFallbackSelfTest), GameplaySkinFallbackSelfTest.Run);
            run(nameof(GameplaySkinDigitsSelfTest), GameplaySkinDigitsSelfTest.Run);
            run(nameof(UiButtonAuditSelfTest), UiButtonAuditSelfTest.Run);
            Directory.CreateDirectory("Build/ui-audit");
            File.WriteAllLines("Build/ui-audit/suite.tsv", results);
            if (failed != 0) throw new Exception("UI regression suite failed " + failed + " test(s)");
            Debug.Log("UI full regression passed: " + results.Count + " independent test suites; results in Build/ui-audit/suite.tsv");
        }
    }
}
