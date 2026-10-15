using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // 封面异步加载器：从本地歌曲目录读取图片，按 UI 需求分批应用，避免歌曲列表首帧卡顿。
    // 目标对象销毁或歌曲选择改变后，待处理结果必须丢弃而不能写回旧卡片。
    public static class LocalCoverLoader
    {
        private const int MaxCachedCovers = 48;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LinkedListNode<string>> CacheNodes =
            new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> CacheRecency = new LinkedList<string>();
        private static readonly Dictionary<int, string> ExpectedPaths = new Dictionary<int, string>();
        private static readonly Dictionary<string, List<PendingTarget>> PendingLoads =
            new Dictionary<string, List<PendingTarget>>(StringComparer.OrdinalIgnoreCase);

        private readonly struct PendingTarget
        {
            public readonly Image Target;
            public readonly bool PreserveAspect;
            public readonly bool FlipVertical;

            public PendingTarget(Image target, bool preserveAspect, bool flipVertical)
            {
                Target = target;
                PreserveAspect = preserveAspect;
                FlipVertical = flipVertical;
            }
        }

        public static void Load(MonoBehaviour host, string path, Image target, bool preserveAspect = false, bool flipVertical = false)
        {
            if (host == null || target == null) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Reset(target);
                return;
            }
            ExpectedPaths[target.GetInstanceID()] = path;
            if (Cache.TryGetValue(path, out Sprite sprite))
            {
                Touch(path);
                ApplyIfExpected(target, path, sprite, preserveAspect, flipVertical);
                return;
            }
            if (PendingLoads.TryGetValue(path, out List<PendingTarget> waiting))
            {
                waiting.Add(new PendingTarget(target, preserveAspect, flipVertical));
                return;
            }

            PendingLoads[path] = new List<PendingTarget> { new PendingTarget(target, preserveAspect, flipVertical) };
            host.StartCoroutine(LoadRoutine(path));
        }

        public static void Reset(Image target)
        {
            if (target == null) return;
            ExpectedPaths.Remove(target.GetInstanceID());
            // A target may have displayed a previous cover. Always restore the
            // UI transform when clearing it so an old orientation cannot leak
            // into the next song or the visual layout preview.
            target.rectTransform.localRotation = Quaternion.identity;
            target.rectTransform.localScale = Vector3.one;
            TrimCache();
        }

        private static IEnumerator LoadRoutine(string path)
        {
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                Task<DdsTextureData> decode = Task.Run(() => DdsTextureDecoder.DecodeFile(path));
                while (!decode.IsCompleted) yield return null;
                if (decode.IsFaulted || decode.IsCanceled)
                {
                    Debug.LogWarning($"DDS jacket load failed: {path} | {decode.Exception?.GetBaseException().Message}");
                    CompleteLoad(path, null);
                    yield break;
                }
                Texture2D texture = DdsTextureDecoder.CreateTexture(decode.Result,
                    Path.GetFileNameWithoutExtension(path));
                Sprite ddsSprite = texture == null ? null : Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                CompleteLoad(path, ddsSprite);
                yield break;
            }
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri, false))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    CompleteLoad(path, null);
                    yield break;
                }
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                {
                    CompleteLoad(path, null);
                    yield break;
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                CompleteLoad(path, sprite);
            }
        }

        private static void CompleteLoad(string path, Sprite loaded)
        {
            if (!PendingLoads.TryGetValue(path, out List<PendingTarget> waiting))
            {
                if (loaded != null) Dispose(loaded);
                return;
            }

            PendingLoads.Remove(path);
            if (loaded == null) return;
            Sprite sprite = Store(path, loaded);
            foreach (PendingTarget request in waiting)
                ApplyIfExpected(request.Target, path, sprite, request.PreserveAspect, request.FlipVertical);
        }

        private static void Apply(Image target, Sprite sprite, bool preserveAspect, bool flipVertical)
        {
            if (target == null || sprite == null) return;
            target.sprite = sprite;
            target.color = Color.white;
            target.type = Image.Type.Simple;
            target.preserveAspect = preserveAspect;
            // DDS upload and UnityWebRequestTexture both produce Unity-origin
            // textures. Keep the UI upright by default. A caller may
            // explicitly opt into a vertical flip only for a source format
            // that is known to require it; ordinary jackets must not inherit
            // a global negative Y scale.
            target.rectTransform.localRotation = Quaternion.identity;
            target.rectTransform.localScale = flipVertical ? new Vector3(1f, -1f, 1f) : Vector3.one;
        }

        private static void ApplyIfExpected(Image target, string path, Sprite sprite, bool preserveAspect, bool flipVertical)
        {
            if (target == null || sprite == null || string.IsNullOrWhiteSpace(path) ||
                !ExpectedPaths.TryGetValue(target.GetInstanceID(), out string expected) ||
                !string.Equals(expected, path, StringComparison.OrdinalIgnoreCase)) return;
            Apply(target, sprite, preserveAspect, flipVertical);
        }

        private static Sprite Store(string path, Sprite sprite)
        {
            if (string.IsNullOrWhiteSpace(path) || sprite == null) return null;
            if (Cache.TryGetValue(path, out Sprite previous))
            {
                // A second request may finish after the first cached the same
                // path. Keep the already displayed texture and drop the copy.
                if (previous != sprite) Dispose(sprite);
                Touch(path);
                return previous;
            }
            Cache[path] = sprite;
            Touch(path);
            TrimCache();
            return sprite;
        }

        private static void Touch(string path)
        {
            if (CacheNodes.TryGetValue(path, out LinkedListNode<string> node))
                CacheRecency.Remove(node);
            node = CacheRecency.AddFirst(path);
            CacheNodes[path] = node;
        }

        private static void TrimCache()
        {
            int remainingCandidates = CacheRecency.Count;
            while (Cache.Count > MaxCachedCovers && remainingCandidates-- > 0 && CacheRecency.Last != null)
            {
                string path = CacheRecency.Last.Value;
                if (IsDisplayed(path))
                {
                    Touch(path);
                    continue;
                }
                CacheRecency.RemoveLast();
                CacheNodes.Remove(path);
                if (!Cache.TryGetValue(path, out Sprite sprite)) continue;
                Cache.Remove(path);
                Dispose(sprite);
            }
        }

        private static bool IsDisplayed(string path)
        {
            return ExpectedPaths.Values.Any(expected => string.Equals(expected, path, StringComparison.OrdinalIgnoreCase));
        }

        private static void Dispose(Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }
}
