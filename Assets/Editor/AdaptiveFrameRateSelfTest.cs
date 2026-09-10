using System;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class AdaptiveFrameRateSelfTest
    {
        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            double[] all = { 144, 60, 120, 90 };
            foreach (int cap in new[] { 60, 90, 120 })
                Require(all[HighRefreshRateController.SelectDisplayMode(all, cap)] == cap, "exact mode not selected");
            Require(all[HighRefreshRateController.SelectDisplayMode(all, 0)] == 144, "unlimited no longer highest");
            double[] divided = { 144, 120 };
            Require(divided[HighRefreshRateController.SelectDisplayMode(divided, 60)] == 120, "60 should use 120/2");
            Require(HighRefreshRateController.SelectDisplayMode(new[] { 60d }, 120) == 0, "low refresh fallback");
            Require(!HighRefreshRateController.NeedsDeadlinePacing(119.88, 60), "fractional refresh should allow native pacing");
            Require(HighRefreshRateController.NeedsDeadlinePacing(144, 60), "144/60 was allowed to round down to 48");
            Require(HighRefreshRateController.NeedsDeadlinePacing(144, 90), "144/90 was allowed to round down to 72");
            Require(HighRefreshRateController.NeedsDeadlinePacing(144, 120), "144/120 was allowed to round down to 72");
            int cases = 0;
            foreach (double hz in new[] { 60d, 75, 90, 100, 119.88, 120, 144, 165, 180, 240 })
            foreach (int cap in new[] { 60, 90, 120 }) {
                if (HighRefreshRateController.NeedsDeadlinePacing(hz, cap)) {
                    var schedule = new FrameRateDeadline();
                    double now = 0;
                    const int count = 3000;
                    // Model frames presented on native VSYNC plus 2ms of CPU
                    // work. An absolute schedule must alternate refresh slots,
                    // instead of continually choosing ceil(refresh/cap).
                    for (int i = 0; i < count; i++) {
                        double due = schedule.Next(now, cap);
                        now = Math.Ceiling((due + .002) * hz - 1e-8) / hz;
                    }
                    double measured = count / now;
                    Require(Math.Abs(measured - cap) < .2, $"schedule drift at {hz}Hz/{cap}: {measured}");
                } else {
                    int native = HighRefreshRateController.SelectTargetFrameRate(hz, cap, true);
                    Require(native <= cap, "cap exceeded on low-refresh display");
                }
                Require(!HighRefreshRateController.NeedsDeadlinePacing(hz, 0), "unlimited uses software wait");
                cases++;
            }
            var recovery = new FrameRateDeadline();
            recovery.Next(0, 60);
            Require(recovery.Next(10, 60) == 10, "stall retained obsolete deadline");
            Require(recovery.Next(10, 90) == 10, "cap switch retained obsolete deadline");
            recovery.Reset();
            Require(recovery.Next(30, 90) == 30, "resume retained obsolete deadline");
            Debug.Log($"Adaptive frame pacing regression passed: {cases} display/cap pairs, 48/72fps rounding prevention, mode selection, unlimited, pause/stall reset. Simulation only; physical-device verification still required.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
