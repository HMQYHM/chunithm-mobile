using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ChunithmMobile.Platform;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 跨平台存储路径：集中生成歌曲、联机歌曲、皮肤、背景、日志和索引目录。
    // 目录创建和路径规范化必须集中处理，避免不同功能把数据写到互不兼容的位置。
    /// <summary>
    /// Keeps platform storage APIs out of chart, timing, judgement and rendering code.
    /// The HarmonyOS NEXT port only needs a new provider for its app sandbox or picker.
    /// </summary>
    public interface IPlatformStoragePaths
    {
        IReadOnlyList<string> GetSongRoots();
    }

    public static class PlatformStoragePaths
    {
        private static IPlatformStoragePaths current;

        public static IPlatformStoragePaths Current => current ??= CreateDefault();

        public static void OverrideForTests(IPlatformStoragePaths provider)
        {
            current = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static IReadOnlyList<string> EnsureAppDirectories()
        {
            IReadOnlyList<string> songRoots = Current.GetSongRoots();
            var appRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                PlatformServices.Current.PersistentDataPath
            };
            foreach (string songRoot in songRoots)
            {
                if (string.IsNullOrWhiteSpace(songRoot)) continue;
                Directory.CreateDirectory(songRoot);
                string parent = Directory.GetParent(Path.GetFullPath(songRoot))?.FullName;
                if (!string.IsNullOrWhiteSpace(parent)) appRoots.Add(parent);
            }

            foreach (string appRoot in appRoots)
            {
                Directory.CreateDirectory(appRoot);
                Directory.CreateDirectory(Path.Combine(appRoot, "Songs"));
                Directory.CreateDirectory(Path.Combine(appRoot, "MultiplayerSongs"));
                Directory.CreateDirectory(Path.Combine(appRoot, "Import"));
                Directory.CreateDirectory(Path.Combine(appRoot, "Logs"));
                Directory.CreateDirectory(Path.Combine(appRoot, "BackgroundSkins"));
                Directory.CreateDirectory(Path.Combine(appRoot, "BackgroundSkins", "Images"));
                Directory.CreateDirectory(Path.Combine(appRoot, "GameplaySkins"));
            }
            Directory.CreateDirectory(Path.Combine(Application.temporaryCachePath, "Imports"));
            return songRoots;
        }

        public static void OpenSongImportPicker()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.zhongermate.chunithmmobile.ImportPickerActivity");
            bridge.CallStatic("openPicker");
#elif UNITY_IOS && !UNITY_EDITOR
            ChunithmOpenDocumentPicker("zip");
#else
            throw new PlatformNotSupportedException("当前平台没有文件选择器");
#endif
        }

        public static void OpenBackgroundImagePicker()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.zhongermate.chunithmmobile.ImportPickerActivity");
            bridge.CallStatic("openBackgroundImagePicker");
#elif UNITY_IOS && !UNITY_EDITOR
            ChunithmOpenDocumentPicker("images");
#else
            throw new PlatformNotSupportedException("当前平台没有图片选择器");
#endif
        }

        public static void OpenGameplaySkinPicker()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.zhongermate.chunithmmobile.ImportPickerActivity");
            bridge.CallStatic("openGameplaySkinPicker");
#elif UNITY_IOS && !UNITY_EDITOR
            ChunithmOpenDocumentPicker("skin");
#else
            throw new PlatformNotSupportedException("当前平台没有文件选择器");
#endif
        }

        public static bool TryShareFile(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.zhongermate.chunithmmobile.LogExportActivity");
            bridge.CallStatic("exportFile", path);
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            ChunithmShareFile(path);
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Returns free bytes on the volume containing <paramref name="path"/>.
        /// DriveInfo is not reliable inside the iOS app container, so Apple
        /// builds query NSFileManager through the native document bridge.
        /// </summary>
        public static long GetAvailableDiskSpace(string path)
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { return ChunithmGetAvailableDiskSpace(path); }
            catch (Exception exception) {
                Debug.LogWarning("Unable to query iOS free disk space: " + exception.Message);
                return -1;
            }
#else
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                return string.IsNullOrWhiteSpace(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return -1; }
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ChunithmOpenDocumentPicker(string mode);

        [DllImport("__Internal")]
        private static extern void ChunithmShareFile(string path);

        [DllImport("__Internal")]
        private static extern long ChunithmGetAvailableDiskSpace(string path);
#endif

        public static IReadOnlyList<string> GetMultiplayerSongRoots()
        {
            return Current.GetSongRoots()
                .Where(path => string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)), "MultiplayerSongs", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public static string PreferredMultiplayerSongRoot()
        {
            string root = GetMultiplayerSongRoots().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(PlatformServices.Current.PersistentDataPath, "MultiplayerSongs");
            Directory.CreateDirectory(root);
            return root;
        }

        private static IPlatformStoragePaths CreateDefault()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidStoragePaths();
#else
            // Unity persistentDataPath is also available to the future
            // OpenHarmony/Tuanjie player, so it remains the portable fallback.
            return new PortableStoragePaths();
#endif
        }
    }

    internal class PortableStoragePaths : IPlatformStoragePaths
    {
        public virtual IReadOnlyList<string> GetSongRoots()
        {
            return new[] {
                Path.Combine(PlatformServices.Current.PersistentDataPath, "Songs"),
                Path.Combine(PlatformServices.Current.PersistentDataPath, "MultiplayerSongs")
            };
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidStoragePaths : PortableStoragePaths
    {
        public override IReadOnlyList<string> GetSongRoots()
        {
            var roots = new List<string>(base.GetSongRoots());
            try {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject directory = activity.Call<AndroidJavaObject>(
                    "getExternalFilesDir", new object[] { null });
                string externalRoot = directory?.Call<string>("getAbsolutePath");
                if (!string.IsNullOrWhiteSpace(externalRoot)) {
                    string externalSongs = Path.Combine(externalRoot, "Songs");
                    if (!roots.Contains(externalSongs)) roots.Insert(0, externalSongs);
                    string externalMultiplayerSongs = Path.Combine(externalRoot, "MultiplayerSongs");
                    if (!roots.Contains(externalMultiplayerSongs)) roots.Insert(1, externalMultiplayerSongs);
                }
            }
            catch (Exception exception) {
                Debug.LogWarning("External song directory is unavailable: " + exception.Message);
            }

            return roots;
        }
    }
#endif
}
