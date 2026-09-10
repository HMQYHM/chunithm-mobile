using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
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

            public PendingTarget(Image target, bool preserveAspect)
            {
                Target = target;
                PreserveAspect = preserveAspect;
            }
        }

        public static void Load(MonoBehaviour host, string path, Image target, bool preserveAspect = false)
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
                ApplyIfExpected(target, path, sprite, preserveAspect);
                return;
            }
            if (PendingLoads.TryGetValue(path, out List<PendingTarget> waiting))
            {
                waiting.Add(new PendingTarget(target, preserveAspect));
                return;
            }

            PendingLoads[path] = new List<PendingTarget> { new PendingTarget(target, preserveAspect) };
            host.StartCoroutine(LoadRoutine(path));
        }

        public static void Reset(Image target)
        {
            if (target == null) return;
            ExpectedPaths.Remove(target.GetInstanceID());
            TrimCache();
        }

        private static IEnumerator LoadRoutine(string path)
        {
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                Sprite ddsSprite = LoadDxt1Dds(path);
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
                ApplyIfExpected(request.Target, path, sprite, request.PreserveAspect);
        }

        private static Sprite LoadDxt1Dds(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length <= 128 || bytes[0] != (byte)'D' || bytes[1] != (byte)'D' || bytes[2] != (byte)'S' || bytes[3] != (byte)' ')
                    return null;
                int height = BitConverter.ToInt32(bytes, 12);
                int width = BitConverter.ToInt32(bytes, 16);
                string fourCc = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
                if (width <= 0 || height <= 0 || !fourCc.Equals("DXT1", StringComparison.OrdinalIgnoreCase))
                    return null;
                int payloadLength = bytes.Length - 128;
                var payload = new byte[payloadLength];
                Buffer.BlockCopy(bytes, 128, payload, 0, payloadLength);
                var texture = new Texture2D(width, height, TextureFormat.DXT1, false)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                texture.LoadRawTextureData(payload);
                texture.Apply(false, false);
                return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"DDS jacket load failed: {path} | {exception.Message}");
                return null;
            }
        }

        private static void Apply(Image target, Sprite sprite, bool preserveAspect)
        {
            if (target == null || sprite == null) return;
            target.sprite = sprite;
            target.color = Color.white;
            target.type = Image.Type.Simple;
            target.preserveAspect = preserveAspect;
            // Extracted jacket textures need a vertical correction for Unity UI,
            // but must retain their original left/right orientation.
            target.rectTransform.localRotation = Quaternion.identity;
            target.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        }

        private static void ApplyIfExpected(Image target, string path, Sprite sprite, bool preserveAspect)
        {
            if (target == null || sprite == null || string.IsNullOrWhiteSpace(path) ||
                !ExpectedPaths.TryGetValue(target.GetInstanceID(), out string expected) ||
                !string.Equals(expected, path, StringComparison.OrdinalIgnoreCase)) return;
            Apply(target, sprite, preserveAspect);
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
