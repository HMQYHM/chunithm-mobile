using System;
using UnityEngine;

namespace ChunithmMobile.Platform
{
    // 平台抽象层：把 Unity/Android 的时间、存储和系统能力封装给业务代码。
    // 上层不应直接散落调用平台 API，便于编辑器自测和不同设备回退。
    public enum PlatformFamily
    {
        Editor,
        Android,
        Apple,
        HarmonyOS,
        Other
    }

    /// <summary>
    /// High precision time source shared by audio playback and chart judgement.
    /// Native platform audio backends may replace this without changing game logic.
    /// </summary>
    public interface IAudioTimeSource
    {
        double Seconds { get; }
    }

    public interface IAudioSchedulingTimeSource : IAudioTimeSource
    {
        // AudioSource.PlayScheduled accepts Unity's raw DSP epoch. Gameplay
        // normally uses Seconds, which may interpolate sparse backend updates;
        // these conversion hooks keep scheduling in the engine's native epoch.
        double RawSeconds { get; }
        double ToRawSeconds(double seconds);
        double FromRawSeconds(double rawSeconds);
    }

    public static class AudioTimeSourceExtensions
    {
        public static double ToRawSeconds(this IAudioTimeSource source, double seconds) =>
            source is IAudioSchedulingTimeSource scheduling
                ? scheduling.ToRawSeconds(seconds) : seconds;

        public static double FromRawSeconds(this IAudioTimeSource source, double rawSeconds) =>
            source is IAudioSchedulingTimeSource scheduling
                ? scheduling.FromRawSeconds(rawSeconds) : rawSeconds;
    }

    public interface IPlatformServices
    {
        PlatformFamily Family { get; }
        string DisplayName { get; }
        string PersistentDataPath { get; }
        Rect SafeAreaPixels { get; }
        IAudioTimeSource AudioTime { get; }
    }

    public static class PlatformServices
    {
        private static IPlatformServices current;

        public static IPlatformServices Current => current ??= new UnityPlatformServices();

        public static void OverrideForTests(IPlatformServices services)
        {
            current = services ?? throw new System.ArgumentNullException(nameof(services));
        }
    }

    /// <summary>
    /// Reconstructs a frame-smooth value from Unity's DSP clock without
    /// replacing its audio-domain reference. Android audio backends may expose
    /// dspTime only when a mixer buffer is submitted; Bluetooth A2DP commonly
    /// makes that interval span several display frames. The host clock fills
    /// only those short gaps and is capped close to the last DSP observation,
    /// so it cannot free-run when the audio device is stopped or rebuilt.
    /// </summary>
    public sealed class AudioDspClockInterpolator
    {
        // This is deliberately shorter than an audible route-change pause but
        // longer than the normal callback spacing of Bluetooth output. It
        // removes two/three-frame stair steps without guessing through a lost
        // or suspended audio device.
        private const double MaximumProjectionSeconds = .075;

        private bool initialized;
        private double lastRawSeconds;
        private double lastHostSeconds;
        private double rawDomainOffset;
        private double interpolatedSeconds;

        public double RawDomainOffset => rawDomainOffset;

        public double Sample(double rawSeconds, double hostSeconds)
        {
            if (!IsFinite(rawSeconds) || !IsFinite(hostSeconds))
                return initialized ? interpolatedSeconds : 0;

            if (!initialized) {
                initialized = true;
                lastRawSeconds = rawSeconds;
                lastHostSeconds = hostSeconds;
                interpolatedSeconds = rawSeconds;
                return interpolatedSeconds;
            }

            // Some Android route rebuilds restart the backend DSP epoch. Keep
            // the public clock monotonic by translating the new raw epoch into
            // the previous one; scheduled audio and touch timestamps continue
            // to share this translated domain after the rebuild.
            if (rawSeconds + .001 < lastRawSeconds)
                rawDomainOffset = interpolatedSeconds - rawSeconds;

            double effectiveRaw = rawSeconds + rawDomainOffset;
            double hostDelta = hostSeconds - lastHostSeconds;
            if (hostDelta < 0 || hostDelta > .5) {
                // A long host gap is an app suspension or debugger stop, not a
                // Bluetooth mixer interval. Do not advance the chart through it.
                interpolatedSeconds = Math.Max(interpolatedSeconds, effectiveRaw);
            } else {
                double projected = interpolatedSeconds + hostDelta;
                // A newly published DSP sample is authoritative when it is in
                // front. While it is stale, host interpolation may lead it only
                // by the bounded amount above.
                projected = Math.Max(projected, effectiveRaw);
                projected = Math.Min(projected, effectiveRaw + MaximumProjectionSeconds);
                interpolatedSeconds = Math.Max(interpolatedSeconds, projected);
            }

            lastRawSeconds = rawSeconds;
            lastHostSeconds = hostSeconds;
            return interpolatedSeconds;
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal sealed class UnityDspTimeSource : IAudioSchedulingTimeSource
    {
        private readonly AudioDspClockInterpolator interpolator = new AudioDspClockInterpolator();

        // Time.realtimeSinceStartupAsDouble is sampled only to interpolate
        // between adjacent DSP observations. AudioSettings.dspTime remains the
        // authority, so music, judgement, touches and multiplayer scheduling
        // stay in one clock domain.
        public double Seconds => interpolator.Sample(
            AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);

        public double RawSeconds => AudioSettings.dspTime;

        public double ToRawSeconds(double seconds) => seconds - interpolator.RawDomainOffset;

        public double FromRawSeconds(double rawSeconds) => rawSeconds + interpolator.RawDomainOffset;
    }

    internal sealed class UnityPlatformServices : IPlatformServices
    {
        private readonly IAudioTimeSource audioTime = new UnityDspTimeSource();

        public PlatformFamily Family
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return PlatformFamily.Android;
#elif UNITY_IOS && !UNITY_EDITOR
                return PlatformFamily.Apple;
#elif UNITY_OPENHARMONY && !UNITY_EDITOR
                // Tuanjie/OpenHarmony player symbol. Kept outside shared game code.
                return PlatformFamily.HarmonyOS;
#elif UNITY_EDITOR
                return PlatformFamily.Editor;
#else
                return PlatformFamily.Other;
#endif
            }
        }

        public string DisplayName => Family switch
        {
            PlatformFamily.Android => "Android",
            PlatformFamily.Apple => "iOS/iPadOS",
            PlatformFamily.HarmonyOS => "HarmonyOS NEXT",
            PlatformFamily.Editor => "Unity Editor",
            _ => Application.platform.ToString()
        };

        public string PersistentDataPath => Application.persistentDataPath;
        public Rect SafeAreaPixels => Screen.safeArea;
        public IAudioTimeSource AudioTime => audioTime;
    }
}
