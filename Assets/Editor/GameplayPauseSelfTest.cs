using System;
using System.Reflection;
using System.IO;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Platform;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    public static class GameplayPauseSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class TestTime : IAudioTimeSource { public double Seconds { get; set; } }

        public static void Run()
        {
            const string settingsKey = "chunithm_mobile_settings_v1";
            bool hadSettings = PlayerPrefs.HasKey(settingsKey);
            string savedSettings = PlayerPrefs.GetString(settingsKey);
            var root = new GameObject("Gameplay background pause regression");
            root.SetActive(false);
            try {
                using var chartData = new MemoryStream(Encoding.UTF8.GetBytes("RESOLUTION\t384\nBPM_DEF\t120\nTAP\t0\t0\t0\t1\n"));
                ParsedChart chart = C2sParser.Parse(chartData);
                var session = root.AddComponent<LocalChartPlaybackSession>();
                var time = new TestTime { Seconds = 10 };
                var clock = new DspChartClock(time);
                Set(session, "clock", clock);
                Set(session, "loadedChart", chart);
                var shell = root.AddComponent<AppUiShell>();
                Set(shell, "gameplaySession", session);
                Set(shell, "currentPage", AppPage.Gameplay);
                Set(shell, "settings", new AppSettings { PauseRequiresDoubleTap = true });
                var panel = new GameObject("Pause", typeof(RectTransform));
                panel.transform.SetParent(root.transform);
                Set(shell, "gameplayPausePanel", panel.GetComponent<RectTransform>());
                var label = new GameObject("Resume countdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                label.transform.SetParent(root.transform);
                var text = label.GetComponent<Text>();
                Set(shell, "gameplayCountdown", text);
                session.PlaybackPaused += () => Invoke(shell, "ShowGameplayPause");

                foreach (bool preparation in new[] { false, true })
                foreach (bool focusFirst in new[] { false, true }) {
                    time.Seconds = 10;
                    if (preparation) clock.PlayScheduled(12, 0, 12);
                    else clock.Play(5000);
                    panel.SetActive(false);
                    text.text = "2";
                    if (focusFirst) Invoke(session, "OnApplicationFocus", false);
                    Invoke(shell, "OnApplicationPause", true);
                    Invoke(session, "OnApplicationPause", true);
                    if (!focusFirst) Invoke(session, "OnApplicationFocus", false);
                    double pausedAt = clock.TimeMs;
                    Require(!clock.Playing && panel.activeSelf && text.text == "",
                        "background did not freeze gameplay/show pause/cancel countdown");
                    time.Seconds = 90;
                    session.ResumePlayback();
                    Require(!clock.Playing, "resumed while backgrounded");
                    Invoke(session, "OnApplicationPause", false);
                    session.ResumePlayback();
                    Require(!clock.Playing, "resumed before focus returned");
                    Invoke(session, "OnApplicationFocus", true);
                    Require(!clock.Playing && clock.TimeMs == pausedAt && panel.activeSelf,
                        "foreground resumed automatically or changed chart time");
                    session.ResumePlayback();
                    Require(clock.Playing && Math.Abs(clock.TimeMs - pausedAt) < .001,
                        "manual resume changed position");
                }

                // A load completing after focus has returned must still start paused.
                clock = new DspChartClock();
                Set(session, "clock", clock);
                Set(session, "loadedChart", null);
                Invoke(session, "OnApplicationPause", true);
                Invoke(session, "OnApplicationPause", false);
                Require((bool)Get(session, "pauseRequested"), "loading lost background pause request");
                var renderer = root.AddComponent<Rendering.ChartPlaybackRenderer>();
                Set(session, "chartRenderer", renderer);
                var source = root.AddComponent<AudioSource>();
                Set(session, "songSource", source);
                Set(session, "loadedChart", chart);
                source.clip = AudioClip.Create("Pause regression", 48000, 1, 48000, false);
                try {
                    Invoke(session, "SchedulePreparedPlayback", 0, 2f);
                    Require(!clock.Playing && panel.activeSelf && clock.Preparing,
                        "deferred start bypassed background pause");
                    session.ResumePlayback();
                    Require(clock.Playing && clock.Preparing, "manual resume lost opening preparation");
                } finally {
                    source.Stop();
                    UnityEngine.Object.DestroyImmediate(source.clip);
                    Set(session, "songSource", null);
                }

                clock.Play();
                panel.SetActive(false);
                Set(shell, "currentPage", AppPage.Home);
                Invoke(shell, "PauseGameplay");
                Require(clock.Playing && !panel.activeSelf, "non-gameplay page opened pause menu");
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
                if (hadSettings) PlayerPrefs.SetString(settingsKey, savedSettings);
                else PlayerPrefs.DeleteKey(settingsKey);
                PlayerPrefs.Save();
            }
            Debug.Log("Gameplay pause regression passed: focus/pause ordering, frozen clock, manual resume, preparation, deferred loading, pause menu and countdown clearing.");
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
