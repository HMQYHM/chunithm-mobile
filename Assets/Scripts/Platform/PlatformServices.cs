using UnityEngine;

namespace ChunithmMobile.Platform
{
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

    internal sealed class UnityDspTimeSource : IAudioTimeSource
    {
        public double Seconds => AudioSettings.dspTime;
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
