using System;
using System.Collections.Generic;

namespace ChunithmMobile.Platform
{
    /// <summary>
    /// Stable boundary for a future Unity/OpenHarmony native runtime.
    /// The default implementation deliberately reports unsupported instead of
    /// pretending that the current Stage shell can run the Unity player.
    /// </summary>
    public enum OpenHarmonyCapability
    {
        File,
        Audio,
        Network,
        Lifecycle,
        ExternalUrl,
        Log
    }

    public readonly struct OpenHarmonyCapabilityState
    {
        public OpenHarmonyCapabilityState(OpenHarmonyCapability capability, bool available, string detail)
        {
            Capability = capability;
            Available = available;
            Detail = detail ?? string.Empty;
        }

        public OpenHarmonyCapability Capability { get; }
        public bool Available { get; }
        public string Detail { get; }
    }

    public interface IOpenHarmonyBridge
    {
        IReadOnlyList<OpenHarmonyCapabilityState> GetCapabilities();
        bool TryOpenExternalUrl(string url);
        bool TryPickFile(string kind, out string path);
        void WriteLog(string level, string message);
    }

    public static class OpenHarmonyBridge
    {
        private static IOpenHarmonyBridge current = new UnsupportedOpenHarmonyBridge();

        public static IOpenHarmonyBridge Current => current;

        public static void OverrideForTests(IOpenHarmonyBridge bridge)
        {
            current = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }
    }

    internal sealed class UnsupportedOpenHarmonyBridge : IOpenHarmonyBridge
    {
        private static readonly IReadOnlyList<OpenHarmonyCapabilityState> States = new[]
        {
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.File, false, "等待原生文件桥接"),
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.Audio, false, "等待 Unity/OpenHarmony 音频运行时"),
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.Network, false, "由 Unity 网络层回退实现"),
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.Lifecycle, false, "等待 UIAbility 生命周期桥接"),
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.ExternalUrl, false, "等待系统浏览器桥接"),
            new OpenHarmonyCapabilityState(OpenHarmonyCapability.Log, false, "等待 hilog/NAPI 桥接")
        };

        public IReadOnlyList<OpenHarmonyCapabilityState> GetCapabilities() => States;

        public bool TryOpenExternalUrl(string url) => false;

        public bool TryPickFile(string kind, out string path)
        {
            path = string.Empty;
            return false;
        }

        public void WriteLog(string level, string message)
        {
            // Keep the fallback side-effect free in editor and Android/iOS builds.
        }
    }
}
