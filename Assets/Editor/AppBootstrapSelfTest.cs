using System;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // Launch regression: removing the settings UI must not restore mobile 30 FPS.
    // Tests startup policy and engine assignment, not actual device throughput.
    public static class AppBootstrapSelfTest
    {
        public static void Run()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var resolve = typeof(AppBootstrap).GetMethod("ResolveStartupFrameRate", flags);
            Func<double, int> target = hz => (int)resolve.Invoke(null, new object[] { hz });
            foreach (int hz in new[] { 30, 48, 60, 75, 90, 120, 144, 165, 240, 360 })
                Require(target(hz) == hz, "launch lost display rate " + hz);
            Require(target(59.94) == 60 && target(119.88) == 120 && target(143.855) == 144,
                "fractional display rounded down into a lower pacing divisor");
            foreach (double hz in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.MaxValue })
                Require(target(hz) == 60, "invalid refresh rate falls back to mobile 30 FPS");
            int previous = Application.targetFrameRate, sync = QualitySettings.vSyncCount;
            try {
                Application.targetFrameRate = 30;
                typeof(AppBootstrap).GetMethod("ApplyStartupFrameRate", flags).Invoke(null, null);
                Require(Application.targetFrameRate == target(Screen.currentResolution.refreshRateRatio.value),
                    "startup did not replace default mobile pacing with current display rate");
                Require(QualitySettings.vSyncCount == sync, "startup changed vsync settings");
            } finally { Application.targetFrameRate = previous; }
            Debug.Log("App bootstrap regression passed: display rates 30–360Hz, fractional rates, invalid refresh fallback, startup assignment and unchanged vsync.");
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
