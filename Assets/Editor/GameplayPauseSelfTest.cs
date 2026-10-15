using System;
using System.Collections;
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
    // 暂停回归测试：验证切出应用、失去焦点和恢复时 DSP 时钟/输入状态的一致性。
    public static class GameplayPauseSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class TestTime : IAudioTimeSource { public double Seconds { get; set; } }

        public static void Run()
        {
            VerifyBluetoothDspInterpolation();
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

                // Run the restart entry up to its first loading yield. The old
                // paused session must be released before any new file reads.
                Set(shell, "playedSong", new SongEntry { Name = "Restart test" });
                Set(shell, "playedChart", new SongChartEntry { Path = "restart-test.c2s" });
                Set(session, "loadedChart", chart);
                clock.Play(5000);
                panel.SetActive(true);
                var restart = (IEnumerator)typeof(AppUiShell).GetMethod("RestartGameplayThroughLoading", Private)
                    .Invoke(shell, null);
                Require(restart.MoveNext(), "restart bypassed loading transition");
                Require(!clock.Playing && Get(session, "loadedChart") == null && !panel.activeSelf &&
                    (bool)Get(shell, "transitioning"), "restart retained old playback or pause controls");
                Require(restart.Current is WaitForSecondsRealtime || restart.Current is IEnumerator,
                    "restart did not yield to a loading frame");
                Set(shell, "transitioning", false);

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
            Debug.Log("Gameplay pause regression passed: Bluetooth DSP interpolation, route rebuild, focus/pause ordering, frozen clock, manual resume, preparation, deferred loading, pause menu and countdown clearing.");
        }

        private static void VerifyBluetoothDspInterpolation()
        {
            var interpolator = new AudioDspClockInterpolator();
            double raw = 100;
            double host = 200;
            double previous = interpolator.Sample(raw, host);

            // Simulate an A2DP backend publishing dspTime once every three
            // display frames. The chart clock must still advance each frame
            // and must remain close to the authoritative DSP sample.
            for (int frame = 1; frame <= 12; frame++) {
                host += 1.0 / 120.0;
                if (frame % 3 == 0) raw += 3.0 / 120.0;
                double current = interpolator.Sample(raw, host);
                Require(current > previous, "Bluetooth DSP interpolation froze between mixer callbacks");
                Require(current >= raw && current - raw <= .075001,
                    "Bluetooth DSP interpolation escaped its bounded audio window");
                previous = current;
            }

            // A suspended app/debugger gap must not advance from host time
            // alone. It can only adopt a newer audio-domain sample.
            host += 2;
            double beforeGap = previous;
            double afterGap = interpolator.Sample(raw, host);
            Require(Math.Abs(afterGap - beforeGap) < .000001,
                "host suspension advanced the chart while DSP was stopped");

            // Android can restart the raw DSP epoch when Bluetooth is attached
            // or removed. The translated public clock must stay monotonic.
            host += .01;
            raw = 1;
            double afterRouteChange = interpolator.Sample(raw, host);
            Require(afterRouteChange >= afterGap,
                "audio route rebuild moved the gameplay clock backwards");
            Require(interpolator.RawDomainOffset > 0,
                "audio route rebuild did not translate the restarted DSP epoch");
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
