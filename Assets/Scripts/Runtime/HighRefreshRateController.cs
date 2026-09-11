using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace ChunithmMobile.Runtime
{
    /// <summary>Keeps Android frame pacing aligned with the active display mode.</summary>
    public sealed class HighRefreshRateController : MonoBehaviour
    {
        private const float RefreshPollSeconds = .5f;
        private float nextRefreshPoll;
        private int appliedTarget;
        private int requestedLimit;
        private double androidBestHz;
        private double activeHz;
        private int pacedLimit;
        private bool suspended;
        private readonly FrameRateDeadline deadline = new FrameRateDeadline();
        private double sampleStart;
        private int sampleFrames;
        private static HighRefreshRateController instance;

        public static void Configure(int value)
        {
            if (instance == null) return;
            int limit = AppSettings.NormalizeFrameRateLimit(value);
            if (instance.requestedLimit == limit) return;
            instance.requestedLimit = limit;
            instance.Apply(true);
        }

        private void Awake()
        {
            instance = this;
            requestedLimit = AppSettings.Load().FrameRateLimit;
            Apply(force: true);
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
        }

        private void Update()
        {
            double now = MonotonicSeconds;
            sampleFrames++;
            if (sampleStart == 0) sampleStart = now;
            if (now - sampleStart >= 10) {
                Debug.Log($"Frame pacing measured: fps={sampleFrames / (now - sampleStart):F1} " +
                          $"activeDisplay={activeHz:F2}Hz limit={requestedLimit} unityTarget={appliedTarget} " +
                          $"deadlineLimit={pacedLimit}");
                sampleStart = now;
                sampleFrames = 0;
            }
            if (Time.unscaledTime < nextRefreshPoll) return;
            Apply(force: false);
        }

        private void OnApplicationFocus(bool focused)
        {
            suspended = !focused;
            deadline.Reset();
            if (focused) Apply(force: true);
        }

        private void OnApplicationPause(bool paused)
        {
            suspended = paused;
            deadline.Reset();
            sampleStart = 0;
            sampleFrames = 0;
            if (!paused) Apply(true);
        }

        private void Apply(bool force)
        {
            nextRefreshPoll = Time.unscaledTime + RefreshPollSeconds;
            if (force) androidBestHz = RequestAndroidPreferredRefreshRate(requestedLimit);
            double displayHz = Math.Max(ReadBestRefreshRate(), androidBestHz);
            activeHz = ReadActiveRefreshRate();
            // Keep the requested cap in Unity's pacing path. Sleeping in the
            // PlayerLoop caused 60/90/120 limits to present as uneven 48/72
            // cadence on panels with fractional refresh rates.
            double pacingDisplayHz = androidBestHz > 0 ? androidBestHz : activeHz;
            int target = SelectTargetFrameRate(pacingDisplayHz, requestedLimit, Application.isMobilePlatform);
            int nextPacedLimit = 0;
            if (force || nextPacedLimit != pacedLimit) deadline.Reset();
            pacedLimit = nextPacedLimit;
            if (!force && target == appliedTarget && QualitySettings.vSyncCount == 0 &&
                OnDemandRendering.renderFrameInterval == 1 && Application.targetFrameRate == target) return;

            QualitySettings.vSyncCount = 0;
            OnDemandRendering.renderFrameInterval = 1;
            Application.targetFrameRate = target;
            Time.maximumDeltaTime = 1f / 30f;
            appliedTarget = target;
            Debug.Log($"Display frame pacing applied: active={activeHz:F2}Hz best={displayHz:F2}Hz " +
                      $"limit={requestedLimit} target={target} deadlineLimit={pacedLimit} vSync={QualitySettings.vSyncCount} " +
                      $"renderInterval={OnDemandRendering.renderFrameInterval}");
        }

        private static double MonotonicSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        // Frame pacing is delegated to Unity/Android Frame Pacing. A custom
        // PlayerLoop sleep fights VSYNC and produces uneven frame delivery on
        // devices whose refresh rate is not an integer multiple of the limit.

        public static bool NeedsDeadlinePacing(double hz, int limit)
        {
            if (limit <= 0 || double.IsNaN(hz) || double.IsInfinity(hz) || hz <= limit + .5) return false;
            double divisor = Math.Round(hz / limit);
            return divisor < 1 || Math.Abs(hz / divisor - limit) > .5;
        }

        public static int SelectDisplayMode(double[] rates, int limit)
        {
            int best = -1, compatible = -1, above = -1;
            for (int i = 0; i < rates.Length; i++) {
                double hz = rates[i];
                if (double.IsNaN(hz) || double.IsInfinity(hz) || hz <= 0) continue;
                if (best < 0 || hz > rates[best]) best = i;
                if (limit <= 0 || hz + .5 < limit) continue;
                if (above < 0 || hz < rates[above]) above = i;
                if (!NeedsDeadlinePacing(hz, limit) && (compatible < 0 || hz < rates[compatible])) compatible = i;
            }
            return limit == 0 ? best : compatible >= 0 ? compatible : above >= 0 ? above : best;
        }

        private static double ReadActiveRefreshRate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var manager = activity.Call<AndroidJavaObject>("getWindowManager");
                using var display = manager.Call<AndroidJavaObject>("getDefaultDisplay");
                float hz = display.Call<float>("getRefreshRate");
                if (hz > 0) return hz;
            } catch (Exception e) { Debug.LogWarning("Reading active refresh rate failed: " + e.Message); }
#endif
            return Screen.currentResolution.refreshRateRatio.value;
        }

        private static double ReadBestRefreshRate()
        {
            double best = Screen.currentResolution.refreshRateRatio.value;
            Resolution[] modes = Screen.resolutions;
            for (int i = 0; i < modes.Length; i++) {
                Resolution mode = modes[i];
                if (mode.width != Screen.currentResolution.width ||
                    mode.height != Screen.currentResolution.height) continue;
                double hz = mode.refreshRateRatio.value;
                if (!double.IsNaN(hz) && !double.IsInfinity(hz)) best = Math.Max(best, hz);
            }
            return best;
        }

        private static double RequestAndroidPreferredRefreshRate(int limit)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getWindowManager"))
                using (var display = manager.Call<AndroidJavaObject>("getDefaultDisplay"))
                using (var current = display.Call<AndroidJavaObject>("getMode")) {
                    int width = current.Call<int>("getPhysicalWidth"), height = current.Call<int>("getPhysicalHeight");
                    var rates = new List<double> { current.Call<float>("getRefreshRate") };
                    var ids = new List<int> { current.Call<int>("getModeId") };
                    foreach (var mode in display.Call<AndroidJavaObject[]>("getSupportedModes")) {
                        using (mode) {
                            if (mode.Call<int>("getPhysicalWidth") != width || mode.Call<int>("getPhysicalHeight") != height) continue;
                            float hz = mode.Call<float>("getRefreshRate");
                            int id = mode.Call<int>("getModeId");
                            rates.Add(hz);
                            ids.Add(id);
                        }
                    }
                    double[] supported = rates.ToArray();
                    int selected = SelectDisplayMode(supported, limit);
                    float preferredHz = (float)rates[selected];
                    int preferredId = ids[selected];
                    Debug.Log($"Android refresh request: active={rates[0]:F2}Hz supported=[{string.Join(",", rates)}] " +
                              $"limit={limit} selected={preferredHz:F2}Hz mode={preferredId}");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() => {
                        try {
                            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                            using var owner = player.GetStatic<AndroidJavaObject>("currentActivity");
                            using var window = owner.Call<AndroidJavaObject>("getWindow");
                            using var attributes = window.Call<AndroidJavaObject>("getAttributes");
                            attributes.Set("preferredDisplayModeId", preferredId);
                            attributes.Set("preferredRefreshRate", preferredHz);
                            window.Call("setAttributes", attributes);
                        } catch (Exception e) { Debug.LogWarning("Android display request failed: " + e.Message); }
                    }));
                    // Return the mode actually selected for this limit. The
                    // previous code returned the highest supported mode even
                    // after selecting 60/90Hz, so Unity could be asked to
                    // present at a rate the active surface did not use.
                    return preferredHz;
                }
            }
            catch (Exception exception) {
                Debug.LogWarning("Android preferred refresh rate was not accepted: " + exception.Message);
            }
#endif
            return 0;
        }

        public static int SelectTargetFrameRate(double displayHz, int limit, bool mobile)
        {
            limit = AppSettings.NormalizeFrameRateLimit(limit);
            if (!mobile) return limit == 0 ? -1 : limit;
            int highest = SelectTargetFrameRate(displayHz);
            return limit == 0 ? highest : Math.Min(limit, highest);
        }

        public static int SelectTargetFrameRate(double displayHz)
        {
            // Some Android devices report 0 Hz until the first surface is ready.
            // Requesting 120 keeps high-refresh panels eligible; a 60 Hz panel
            // still presents at its native rate through Android Frame Pacing.
            if (double.IsNaN(displayHz) || double.IsInfinity(displayHz) || displayHz < 59)
                return 120;
            return Mathf.Clamp(Mathf.CeilToInt((float)displayHz), 60, 1000);
        }
    }
}
