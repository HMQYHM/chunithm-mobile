using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>Reports capability and actual session values, never asserts that
    /// Unity obtained Android fast track. Device features are not a playback backend.</summary>
    public static class MobileAudioDiagnostics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr ChuAudioSessionReport(double preferredSeconds);
#endif
        public static string Report(int samples, int rate)
        {
            AudioSettings.GetDSPBufferSize(out int frames, out int buffers);
            string report = $"Unity mixer={rate}Hz, block={frames}x{buffers}, queue estimate={frames * buffers * 1000.0 / Math.Max(1,rate):F2}ms (not total output latency)";
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager")) {
                    string nativeRate = manager.Call<string>("getProperty", "android.media.property.OUTPUT_SAMPLE_RATE");
                    string burst = manager.Call<string>("getProperty", "android.media.property.OUTPUT_FRAMES_PER_BUFFER");
                    bool low = packageManager.Call<bool>("hasSystemFeature", "android.hardware.audio.low_latency");
                    bool pro = packageManager.Call<bool>("hasSystemFeature", "android.hardware.audio.pro");
                    report += $"; Android nativeRate={nativeRate}, burst={burst}, lowLatencyFeature={low}, proAudio={pro}; Unity backend/fast-track unknown";
                    var devices = manager.Call<AndroidJavaObject[]>("getDevices", 2);
                    foreach (var device in devices) {
                        using (device) report += "; outputType=" + device.Call<int>("getType");
                    }
                }
            } catch (Exception error) { report += "; Android capability query unavailable: " + error.GetType().Name; }
#elif UNITY_IOS && !UNITY_EDITOR
            try { report += "; " + Marshal.PtrToStringAnsi(ChuAudioSessionReport(samples / (double)Math.Max(1,rate))); }
            catch (Exception error) { report += "; iOS session query unavailable: " + error.GetType().Name; }
#else
            report += "; Editor/desktop: mobile output route not tested";
#endif
            Debug.Log("Audio latency diagnostics: " + report);
            return report;
        }
    }
}
