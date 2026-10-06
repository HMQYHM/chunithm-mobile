using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 设备内存信息工具：为导入、解码和皮肤加载提供保守的内存预算。
    // 预算只是提前保护，不能代替每一步的异常处理和临时对象释放。
    /// <summary>
    /// Provides an OS-level free-memory estimate for optional preflight checks.
    /// It intentionally returns -1 where the platform does not expose a useful
    /// value rather than treating managed heap size as available system memory.
    /// </summary>
    public static class PlatformMemoryInfo
    {
        public static long GetAvailableMemoryBytes()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject service = activity.Call<AndroidJavaObject>("getSystemService", "activity"))
                using (var memoryInfo = new AndroidJavaObject("android.app.ActivityManager$MemoryInfo"))
                {
                    service.Call("getMemoryInfo", memoryInfo);
                    return Math.Max(0, memoryInfo.Get<long>("availMem"));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to query Android available memory: " + exception.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                return Math.Max(0, ChunithmGetAvailableMemory());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Unable to query iOS available memory: " + exception.Message);
            }
#endif
            return -1;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern long ChunithmGetAvailableMemory();
#endif
    }
}
