using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 背景图片仓库：管理导入图片的压缩、长宽比、持久化路径和随机选择索引。
    // 文件处理应使用流式/分批方式，避免大图在移动设备上同时占用多份完整像素内存。
    /// <summary>Persistent custom background images with one-at-a-time 720p processing.</summary>
    public static class BackgroundImageStore
    {
        public const int MaxImages = 50;
        private const int MaxWidth = 1280;
        private const int MaxHeight = 720;
        private const int RuntimeTextureCacheLimit = 18;

        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };
        private static readonly Dictionary<string, Texture2D> RuntimeTextureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> RuntimeTextureLru = new LinkedList<string>();

        public static string Root => Path.Combine(Application.persistentDataPath, "BackgroundSkins");
        public static string ImagesDirectory => Path.Combine(Root, "Images");
        public static string StagingDirectory => Path.Combine(Root, ".importing");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ImagesDirectory);
        }

        public static IReadOnlyList<string> GetImagePaths()
        {
            EnsureDirectories();
            return Directory.GetFiles(ImagesDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(MaxImages)
                .ToArray();
        }

        /// <summary>
        /// Loads only the image currently needed by a falling background item.
        /// The bounded shared cache keeps several active pages from decoding
        /// the same imported image repeatedly while avoiding a 50-image spike.
        /// </summary>
        public static bool TryGetRuntimeTexture(string path, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            string key = Path.GetFullPath(path);
            if (!RuntimeTextureCache.TryGetValue(key, out texture) || texture == null)
            {
                texture = null;
                return false;
            }
            TouchRuntimeTexture(key);
            return true;
        }

        public static Texture2D LoadRuntimeTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsSupportedPath(path)) return null;
            string key = Path.GetFullPath(path);
            if (TryGetRuntimeTexture(key, out Texture2D cached)) return cached;

            Texture2D texture = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(key);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
                    name = "Background trail image",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                // Falling images never need CPU-readable pixels after decode.
                if (!texture.LoadImage(bytes, true) || texture.width <= 0 || texture.height <= 0)
                {
                    DestroyRuntimeTexture(texture);
                    return null;
                }

                RuntimeTextureCache[key] = texture;
                RuntimeTextureLru.Remove(key);
                RuntimeTextureLru.AddLast(key);
                TrimRuntimeTextureCache();
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("背景拖尾图片加载失败：" + Path.GetFileName(path) + " | " + exception.Message);
                DestroyRuntimeTexture(texture);
                return null;
            }
        }

        public static void ClearRuntimeTextureCache()
        {
            foreach (Texture2D texture in RuntimeTextureCache.Values)
                DestroyRuntimeTexture(texture);
            RuntimeTextureCache.Clear();
            RuntimeTextureLru.Clear();
        }

        public static string BeginImport()
        {
            EnsureDirectories();
            if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, true);
            Directory.CreateDirectory(StagingDirectory);
            return StagingDirectory;
        }

        public static string ProcessToStaging(string sourcePath, string stagingDirectory, int index)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("找不到所选图片", sourcePath);
            if (!IsSupportedPath(sourcePath))
                throw new InvalidDataException("不支持的图片格式：" + Path.GetFileName(sourcePath));

            byte[] bytes = File.ReadAllBytes(sourcePath);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(bytes, false) || source.width <= 0 || source.height <= 0)
                    throw new InvalidDataException("图片无法读取：" + Path.GetFileName(sourcePath));

                // Images already inside the 720p bounds keep their original
                // encoding so a small, clear source is never recompressed.
                if (source.width <= MaxWidth && source.height <= MaxHeight)
                {
                    string originalExtension = Path.GetExtension(sourcePath).ToLowerInvariant();
                    string destination = Path.Combine(stagingDirectory,
                        "background_" + index.ToString("00") + originalExtension);
                    File.Copy(sourcePath, destination, true);
                    return destination;
                }

                float scale = Mathf.Min((float)MaxWidth / source.width, (float)MaxHeight / source.height);
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                Texture2D resized = Resize(source, width, height);
                try
                {
                    byte[] output = resized.EncodeToJPG(86);
                    string destination = Path.Combine(stagingDirectory,
                        "background_" + index.ToString("00") + ".jpg");
                    File.WriteAllBytes(destination, output);
                    return destination;
                }
                finally
                {
                    UnityEngine.Object.Destroy(resized);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(source);
            }
        }

        public static IReadOnlyList<string> CommitImport(string stagingDirectory)
        {
            if (string.IsNullOrWhiteSpace(stagingDirectory) ||
                !Directory.Exists(stagingDirectory))
                throw new InvalidOperationException("背景图片临时目录不存在");

            string[] staged = Directory.GetFiles(stagingDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (staged.Length == 0 || staged.Length > MaxImages)
                throw new InvalidOperationException("背景图片数量必须为 1 至 50 张");

            EnsureDirectories();
            ClearRuntimeTextureCache();
            foreach (string oldPath in Directory.GetFiles(ImagesDirectory, "*.*", SearchOption.TopDirectoryOnly))
                if (IsSupportedPath(oldPath)) File.Delete(oldPath);
            foreach (string source in staged)
            {
                string destination = Path.Combine(ImagesDirectory, Path.GetFileName(source));
                File.Move(source, destination);
            }
            Directory.Delete(stagingDirectory, true);
            return GetImagePaths();
        }

        public static void AbortImport()
        {
            if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, true);
        }

        public static void Clear()
        {
            EnsureDirectories();
            ClearRuntimeTextureCache();
            foreach (string path in Directory.GetFiles(ImagesDirectory, "*.*", SearchOption.TopDirectoryOnly))
                if (IsSupportedPath(path)) File.Delete(path);
            AbortImport();
        }

        private static void TouchRuntimeTexture(string key)
        {
            LinkedListNode<string> node = RuntimeTextureLru.Find(key);
            if (node == null) RuntimeTextureLru.AddLast(key);
            else {
                RuntimeTextureLru.Remove(node);
                RuntimeTextureLru.AddLast(key);
            }
        }

        private static void TrimRuntimeTextureCache()
        {
            while (RuntimeTextureCache.Count > RuntimeTextureCacheLimit && RuntimeTextureLru.First != null)
            {
                string key = RuntimeTextureLru.First.Value;
                RuntimeTextureLru.RemoveFirst();
                if (!RuntimeTextureCache.TryGetValue(key, out Texture2D texture)) continue;
                RuntimeTextureCache.Remove(key);
                DestroyRuntimeTexture(texture);
            }
        }

        private static void DestroyRuntimeTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        private static bool IsSupportedPath(string path)
        {
            string extension = Path.GetExtension(path);
            return Extensions.Any(value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static Texture2D Resize(Texture2D source, int width, int height)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var resized = new Texture2D(width, height, TextureFormat.RGB24, false) {
                    name = "Compressed custom background",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resized.Apply(false, false);
                return resized;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
