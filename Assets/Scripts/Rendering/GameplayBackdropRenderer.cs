using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Rendering
{
    public sealed class GameplayBackdropRenderer : MonoBehaviour
    {
        private MeshRenderer blurredRenderer;
        private MeshRenderer coverRenderer;
        private Material blurredMaterial;
        private Material coverMaterial;
        private Texture2D loadedSource;
        private Texture2D loadedBlurred;
        private Texture2D loadedCover;
        private Coroutine loading;
        private int requestVersion;
        private float brightness = .70f;
        private float opacity = .42f;

        private void Awake()
        {
            Camera camera = GetComponent<Camera>();
            if (camera == null) return;
            const float distance = 70f;
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float width = height * camera.aspect;
            blurredMaterial = MeshFactory.Material((Texture)null, new Color(.7f, .7f, .7f, 1f));
            blurredMaterial.renderQueue = 1000;
            coverMaterial = MeshFactory.Material((Texture)null, new Color(.7f, .7f, .7f, 1f), true);
            coverMaterial.renderQueue = 1001;
            blurredRenderer = CreateQuad("Blurred jacket background", distance, width, height, blurredMaterial);
            float coverSize = height * .56f;
            coverRenderer = CreateQuad("Centered jacket", distance - .08f, coverSize, coverSize, coverMaterial);
            SetVisible(false);
        }

        public void Configure(float backgroundBrightness, float jacketOpacity)
        {
            brightness = Mathf.Clamp01(backgroundBrightness);
            opacity = Mathf.Clamp01(jacketOpacity);
            ApplyColors();
        }

        public void ShowJacket(string path)
        {
            requestVersion++;
            if (loading != null) StopCoroutine(loading);
            ReleaseTextures();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                SetVisible(false);
                return;
            }
            loading = StartCoroutine(Load(path, requestVersion));
        }

        public void ClearJacket()
        {
            requestVersion++;
            if (loading != null) StopCoroutine(loading);
            loading = null;
            SetVisible(false);
            ReleaseTextures();
        }

        private IEnumerator Load(string path, int version)
        {
            Texture2D source = null;
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                source = LocalDdsTexture.LoadDxt1(path);
            else {
                using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri, false);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                    source = DownloadHandlerTexture.GetContent(request);
            }
            if (version != requestVersion || source == null) {
                if (source != null) Destroy(source);
                yield break;
            }
            Texture2D blurred = CreateBlurred(source, 64);
            Texture2D roundedCover = CreateRoundedCover(source, 512, .085f);
            loadedSource = source;
            loadedBlurred = blurred;
            loadedCover = roundedCover;
            blurredMaterial.mainTexture = blurred;
            coverMaterial.mainTexture = roundedCover;
            ApplyColors();
            SetVisible(true);
            loading = null;
        }

        private void ReleaseTextures()
        {
            if (loadedBlurred != null) Destroy(loadedBlurred);
            if (loadedCover != null) Destroy(loadedCover);
            if (loadedSource != null) Destroy(loadedSource);
            loadedBlurred = null;
            loadedCover = null;
            loadedSource = null;
            if (blurredMaterial != null) blurredMaterial.mainTexture = null;
            if (coverMaterial != null) coverMaterial.mainTexture = null;
        }

        private void OnDestroy()
        {
            ReleaseTextures();
            if (blurredMaterial != null) Destroy(blurredMaterial);
            if (coverMaterial != null) Destroy(coverMaterial);
        }

        private void ApplyColors()
        {
            if (blurredMaterial != null)
                blurredMaterial.color = new Color(brightness, brightness, brightness, Mathf.Lerp(.34f, .92f, opacity));
            if (coverMaterial != null)
                coverMaterial.color = new Color(brightness, brightness, brightness, Mathf.Lerp(.50f, 1f, opacity));
        }

        private void SetVisible(bool visible)
        {
            if (blurredRenderer != null) blurredRenderer.enabled = visible;
            if (coverRenderer != null) coverRenderer.enabled = visible;
        }

        private MeshRenderer CreateQuad(string name, float z, float width, float height, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3(0, 0, z);
            var filter = child.AddComponent<MeshFilter>();
            Mesh quad = MeshFactory.Quad(
                new Vector3(-width * .5f, -height * .5f, 0),
                new Vector3(width * .5f, -height * .5f, 0),
                new Vector3(width * .5f, height * .5f, 0),
                new Vector3(-width * .5f, height * .5f, 0));
            // Extracted jackets use the opposite vertical origin from the
            // world-space quad. UI jackets correct this in LocalCoverLoader;
            // keep the same orientation for the blurred and centred gameplay
            // images without rotating or mirroring the artwork horizontally.
            quad.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), Vector2.zero };
            filter.sharedMesh = quad;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Texture2D CreateBlurred(Texture source, int size)
        {
            var temporary = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, temporary);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "Blurred gameplay jacket", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            result.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);

            Color[] pixels = result.GetPixels();
            Color[] pass = new Color[pixels.Length];
            for (int iteration = 0; iteration < 3; iteration++) {
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                    Color sum = Color.clear; int count = 0;
                    for (int oy = -2; oy <= 2; oy++) for (int ox = -2; ox <= 2; ox++) {
                        int px = Mathf.Clamp(x + ox, 0, size - 1);
                        int py = Mathf.Clamp(y + oy, 0, size - 1);
                        sum += pixels[py * size + px]; count++;
                    }
                    pass[y * size + x] = sum / count;
                }
                Color[] swap = pixels; pixels = pass; pass = swap;
            }
            result.SetPixels(pixels); result.Apply(false, true);
            return result;
        }

        private static Texture2D CreateRoundedCover(Texture source, int size, float radiusRatio)
        {
            var temporary = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, temporary);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "Rounded gameplay jacket", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);

            Color[] pixels = result.GetPixels();
            float half = size * .5f;
            float radius = Mathf.Clamp(size * radiusRatio, 2f, half);
            Vector2 innerHalf = new Vector2(half - radius, half - radius);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                Vector2 point = new Vector2(Mathf.Abs(x + .5f - half), Mathf.Abs(y + .5f - half));
                Vector2 outside = new Vector2(
                    Mathf.Max(point.x - innerHalf.x, 0f),
                    Mathf.Max(point.y - innerHalf.y, 0f));
                float signedDistance = outside.magnitude +
                    Mathf.Min(Mathf.Max(point.x - innerHalf.x, point.y - innerHalf.y), 0f) - radius;
                float coverage = Mathf.Clamp01(.5f - signedDistance);
                int index = y * size + x;
                Color color = pixels[index];
                color.a *= coverage;
                pixels[index] = color;
            }
            result.SetPixels(pixels);
            result.Apply(false, true);
            return result;
        }
    }

    internal static class LocalDdsTexture
    {
        public static Texture2D LoadDxt1(string path)
        {
            try {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length <= 128 || bytes[0] != 'D' || bytes[1] != 'D' || bytes[2] != 'S') return null;
                int height = BitConverter.ToInt32(bytes, 12), width = BitConverter.ToInt32(bytes, 16);
                if (width <= 0 || height <= 0 || System.Text.Encoding.ASCII.GetString(bytes, 84, 4) != "DXT1") return null;
                byte[] payload = new byte[bytes.Length - 128];
                Buffer.BlockCopy(bytes, 128, payload, 0, payload.Length);
                var texture = new Texture2D(width, height, TextureFormat.DXT1, false) {
                    name = Path.GetFileNameWithoutExtension(path), filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadRawTextureData(payload); texture.Apply(false, false);
                return texture;
            }
            catch (Exception exception) {
                Debug.LogWarning("Gameplay DDS jacket load failed: " + exception.Message);
                return null;
            }
        }
    }
}
