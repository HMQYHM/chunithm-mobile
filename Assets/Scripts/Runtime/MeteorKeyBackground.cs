using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // 非游玩页面的流星/emoji 背景：维护种子、速度、旋转、拖尾和边界淡入淡出。
    // 进入 Gameplay 时应停用或隐藏，保证背景不会遮挡谱面并减少移动端 GPU 开销。
    public enum MeteorBackgroundProfile
    {
        Home,
        Quiet,
        Results
    }

    public enum MeteorTrailMode
    {
        Notes = 0,
        Emoji = 1,
        Images = 2
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MeteorKeyBackground : MaskableGraphic
    {
        private const int MaximumKeys = 18;
        private const int TrailSegments = 6;
        private const int HaloSegments = 14;

        private readonly MeteorSeed[] seeds = new MeteorSeed[MaximumKeys];
        private readonly RawImage[] assetImages = new RawImage[MaximumKeys];
        private static Texture2D[] emojiTextures;
        private MeteorBackgroundProfile profile;
        private int visibleKeys = 12;
        private float intensity = .6f;
        private bool seedsReady;
        private MeteorTrailMode trailMode = MeteorTrailMode.Notes;
        private string[] importedImagePaths = Array.Empty<string>();
        private int imageLoadBudget;
        private bool highContrastPalette;
        private Color[] ActivePalette => highContrastPalette ? ArcadePalette : Palette;
        private static readonly Color[] ArcadePalette = {
            new Color(1f, .91f, .08f), new Color(1f, .91f, .08f), new Color(1f, .91f, .08f),
            new Color(1f, .91f, .08f), new Color(1f, .91f, .08f), new Color(1f, .91f, .08f),
            Color.white, Color.white, Color.white,
            new Color(.04f, .74f, 1f), new Color(.04f, .74f, 1f), new Color(1f, .12f, .59f)
        };

        public void SetHighContrastPalette(bool enabled)
        {
            if (highContrastPalette == enabled) return;
            highContrastPalette = enabled;
            BuildSeeds();
            SetVerticesDirty();
        }

        private static readonly Color[] Palette =
        {
            new Color(.18f, .88f, 1f, 1f),
            new Color(.38f, 1f, .70f, 1f),
            new Color(1f, .76f, .20f, 1f),
            new Color(1f, .38f, .48f, 1f),
            new Color(.72f, .62f, 1f, 1f)
        };

        private static readonly Vector2[] ChamferPoints =
        {
            new Vector2(-1f, -1f),
            new Vector2(1f, -1f),
            new Vector2(1.42f, -.58f),
            new Vector2(1.42f, .58f),
            new Vector2(1f, 1f),
            new Vector2(-1f, 1f),
            new Vector2(-1.42f, .58f),
            new Vector2(-1.42f, -.58f)
        };

        public void Configure(MeteorBackgroundProfile value)
        {
            profile = value;
            switch (profile)
            {
                case MeteorBackgroundProfile.Home:
                    visibleKeys = 16;
                    intensity = .84f;
                    break;
                case MeteorBackgroundProfile.Results:
                    visibleKeys = 18;
                    intensity = .78f;
                    break;
                default:
                    visibleKeys = 11;
                    intensity = .48f;
                    break;
            }
            SetVerticesDirty();
        }

        public void SetBackgroundMode(int mode)
        {
            trailMode = (MeteorTrailMode)Mathf.Clamp(mode, 0, 2);
            if (trailMode == MeteorTrailMode.Emoji) EnsureEmojiTextures();
            importedImagePaths = trailMode == MeteorTrailMode.Images
                ? BackgroundImageStore.GetImagePaths().ToArray()
                : Array.Empty<string>();
            if (trailMode != MeteorTrailMode.Notes) EnsureAssetImages();
            for (int index = 0; index < assetImages.Length; index++)
                if (assetImages[index] != null) assetImages[index].gameObject.SetActive(false);
            SetVerticesDirty();
        }

        public static Texture2D RandomEmojiTexture()
        {
            EnsureEmojiTextures();
            return emojiTextures.Length == 0 ? null : emojiTextures[UnityEngine.Random.Range(0, emojiTextures.Length)];
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
            BuildSeeds();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!seedsReady) BuildSeeds();
            SetVerticesDirty();
        }

        private void Update()
        {
            if (trailMode != MeteorTrailMode.Notes) UpdateAssetImages();
            SetVerticesDirty();
        }

        private void BuildSeeds()
        {
            uint state = 0x51F2A93Du;
            for (int index = 0; index < seeds.Length; index++)
            {
                float size = Mathf.Lerp(30f, 88f, Mathf.Pow(Random01(ref state), 1.45f));
                float speedRoll = Random01(ref state);
                float duration = speedRoll < .28f
                    ? Mathf.Lerp(3.2f, 4.4f, Random01(ref state))
                    : speedRoll < .70f
                        ? Mathf.Lerp(5.2f, 7.4f, Random01(ref state))
                        : Mathf.Lerp(8.8f, 11.8f, Random01(ref state));
                float spinRoll = Random01(ref state);
                float spin = spinRoll < .34f ? 0f : Mathf.Lerp(9f, 72f, Random01(ref state));
                if (Random01(ref state) < .5f) spin = -spin;
                seeds[index] = new MeteorSeed
                {
                    Phase = Random01(ref state) * 9f,
                    Duration = duration,
                    X = Mathf.Lerp(.07f, .93f, Random01(ref state)),
                    Drift = Mathf.Lerp(-.18f, .18f, Random01(ref state)),
                    Width = size * Mathf.Lerp(1.18f, 1.82f, Random01(ref state)),
                    Height = size * Mathf.Lerp(.35f, .52f, Random01(ref state)),
                    Tilt = Mathf.Lerp(-24f, 24f, Random01(ref state)),
                    Spin = spin,
                    ColorIndex = Mathf.FloorToInt(Random01(ref state) * ActivePalette.Length) % ActivePalette.Length,
                    EmojiIndex = Mathf.FloorToInt(Random01(ref state) * 200f),
                    Brightness = Mathf.Lerp(.66f, 1f, Random01(ref state))
                };
            }
            seedsReady = true;
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect bounds = rectTransform.rect;
            if (bounds.width < 1f || bounds.height < 1f || intensity <= 0f) return;

            float now = Time.unscaledTime;
            int count = Mathf.Clamp(visibleKeys, 0, seeds.Length);

            // Edge light is laid down first so overlapping keys never make the
            // top or bottom border turn into a solid band.
            for (int index = 0; index < count; index++)
                DrawEdgeHalos(vertices, bounds, seeds[index], now);

            for (int index = 0; index < count; index++)
                DrawMeteor(vertices, bounds, seeds[index], now);
        }

        private void DrawEdgeHalos(VertexHelper vertices, Rect bounds, MeteorSeed seed, float now)
        {
            Sample(seed, bounds, now, out Vector2 position, out _, out float alpha);
            Color tint = ActivePalette[seed.ColorIndex];
            float haloWidth = seed.Width * 2.4f;
            float topDistance = Mathf.Abs(position.y - bounds.yMax);
            float bottomDistance = Mathf.Abs(position.y - bounds.yMin);
            float top = 1f - Mathf.Clamp01(topDistance / Mathf.Max(80f, seed.Width * 1.7f));
            float bottom = 1f - Mathf.Clamp01(bottomDistance / Mathf.Max(105f, seed.Width * 2.1f));
            float cappedIntensity = Mathf.Min(intensity, .66f);

            if (top > .001f)
            {
                float glow = top * top * alpha * cappedIntensity * .38f;
                AddRadialEllipse(vertices, new Vector2(position.x, bounds.yMax),
                    new Vector2(haloWidth, Mathf.Max(34f, seed.Height * 2.6f)), WithAlpha(tint, glow));
            }
            if (bottom > .001f)
            {
                float glow = bottom * bottom * alpha * cappedIntensity * .46f;
                AddRadialEllipse(vertices, new Vector2(position.x, bounds.yMin),
                    new Vector2(haloWidth * 1.12f, Mathf.Max(42f, seed.Height * 3.1f)), WithAlpha(tint, glow));
            }
        }

        private void DrawMeteor(VertexHelper vertices, Rect bounds, MeteorSeed seed, float now)
        {
            Sample(seed, bounds, now, out Vector2 position, out Vector2 velocity, out float alpha);
            if (alpha <= .001f) return;

            Color tint = ActivePalette[seed.ColorIndex];
            float strength = intensity * seed.Brightness * alpha;
            Vector2 tailDirection = velocity.sqrMagnitude > .001f ? -velocity.normalized : Vector2.up;
            float speedFactor = Mathf.InverseLerp(bounds.height / 11.8f, bounds.height / 3.2f, -velocity.y);
            float trailLength = seed.Width * Mathf.Lerp(2.2f, 4.3f, speedFactor);
            AddTrail(vertices, position, tailDirection, trailLength, seed.Height * .72f,
                WithAlpha(tint, strength * .34f));

            if (trailMode != MeteorTrailMode.Notes)
            {
                AddRadialEllipse(vertices, position,
                    new Vector2(seed.Width * .78f, seed.Width * .78f), WithAlpha(tint, strength * .18f));
                return;
            }

            float angle = seed.Tilt + seed.Spin * (now + seed.Phase);
            AddChamferedFan(vertices, position, seed.Width * 1.62f, seed.Height * 2.15f, angle,
                WithAlpha(tint, strength * .20f), new Color(tint.r, tint.g, tint.b, 0));
            AddChamferedFan(vertices, position, seed.Width, seed.Height, angle,
                WithAlpha(Color.Lerp(tint, Color.white, .42f), strength * .92f),
                WithAlpha(tint, strength * .72f));
            AddChamferedFan(vertices, position, seed.Width * .72f, seed.Height * .48f, angle,
                WithAlpha(Color.white, strength * .74f), WithAlpha(tint, strength * .46f));
        }

        private static void Sample(MeteorSeed seed, Rect bounds, float now,
            out Vector2 position, out Vector2 velocity, out float alpha)
        {
            float progress = Mathf.Repeat(now + seed.Phase, seed.Duration) / seed.Duration;
            float travelPadding = Mathf.Max(90f, seed.Width * 1.7f);
            float startY = bounds.yMax + travelPadding;
            float endY = bounds.yMin - travelPadding;
            float x = bounds.xMin + bounds.width * (seed.X + seed.Drift * (progress - .5f));
            position = new Vector2(x, Mathf.Lerp(startY, endY, progress));
            velocity = new Vector2(bounds.width * seed.Drift / seed.Duration,
                (endY - startY) / seed.Duration);
            alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .055f)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - progress) / .055f));
        }

        private static void AddTrail(VertexHelper vertices, Vector2 head, Vector2 direction,
            float length, float width, Color headColor)
        {
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            int first = vertices.currentVertCount;
            for (int index = 0; index <= TrailSegments; index++)
            {
                float t = index / (float)TrailSegments;
                float taperedWidth = width * Mathf.Pow(1f - t, .72f);
                Vector2 center = head + direction * length * t;
                Color color = headColor;
                color.a *= Mathf.Pow(1f - t, 1.65f);
                AddVertex(vertices, center - perpendicular * taperedWidth * .5f, color);
                AddVertex(vertices, center + perpendicular * taperedWidth * .5f, color);
                if (index == TrailSegments) continue;
                int offset = first + index * 2;
                vertices.AddTriangle(offset, offset + 1, offset + 2);
                vertices.AddTriangle(offset + 1, offset + 3, offset + 2);
            }
        }

        private static void AddRadialEllipse(VertexHelper vertices, Vector2 center, Vector2 radius, Color centerColor)
        {
            int first = vertices.currentVertCount;
            AddVertex(vertices, center, centerColor);
            Color edge = centerColor;
            edge.a = 0;
            for (int index = 0; index <= HaloSegments; index++)
            {
                float radians = index / (float)HaloSegments * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(radians) * radius.x, Mathf.Sin(radians) * radius.y);
                AddVertex(vertices, point, edge);
                if (index > 0) vertices.AddTriangle(first, first + index, first + index + 1);
            }
        }

        private static void AddChamferedFan(VertexHelper vertices, Vector2 center, float width,
            float height, float degrees, Color centerColor, Color edgeColor)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            int first = vertices.currentVertCount;
            AddVertex(vertices, center, centerColor);
            for (int index = 0; index <= ChamferPoints.Length; index++)
            {
                Vector2 template = ChamferPoints[index % ChamferPoints.Length];
                Vector2 point = new Vector2(template.x * width * .35f, template.y * height * .5f);
                Vector2 rotated = new Vector2(point.x * cosine - point.y * sine,
                    point.x * sine + point.y * cosine);
                AddVertex(vertices, center + rotated, edgeColor);
                if (index > 0) vertices.AddTriangle(first, first + index, first + index + 1);
            }
        }

        private static void AddVertex(VertexHelper vertices, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            vertices.AddVert(vertex);
        }

        private static Color WithAlpha(Color value, float alpha)
        {
            value.a = Mathf.Clamp01(alpha);
            return value;
        }

        private static float Random01(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return (state & 0x00FFFFFFu) / 16777215f;
        }

        private static void EnsureEmojiTextures()
        {
            if (emojiTextures != null) return;
            emojiTextures = Resources.LoadAll<Texture2D>("Emoji") ?? Array.Empty<Texture2D>();
            Array.Sort(emojiTextures, (left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));
        }

        private void EnsureAssetImages()
        {
            for (int index = 0; index < assetImages.Length; index++)
            {
                if (assetImages[index] != null) continue;
                var imageObject = new GameObject("Background trail image " + index, typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(RawImage));
                RectTransform rect = (RectTransform)imageObject.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                RawImage image = imageObject.GetComponent<RawImage>();
                image.raycastTarget = false;
                assetImages[index] = image;
            }
        }

        private void UpdateAssetImages()
        {
            EnsureAssetImages();
            Rect bounds = rectTransform.rect;
            float now = Time.unscaledTime;
            int count = Mathf.Clamp(visibleKeys, 0, seeds.Length);
            imageLoadBudget = trailMode == MeteorTrailMode.Images ? 2 : 0;
            for (int index = 0; index < assetImages.Length; index++)
            {
                RawImage image = assetImages[index];
                if (image == null) continue;
                Texture2D texture = index < count ? AssetTexture(seeds[index]) : null;
                bool visible = index < count && texture != null;
                image.gameObject.SetActive(visible);
                if (!visible) continue;
                MeteorSeed seed = seeds[index];
                Sample(seed, bounds, now, out Vector2 position, out _, out float alpha);
                RectTransform rect = image.rectTransform;
                rect.anchoredPosition = position;
                image.texture = texture;
                float maxSize = seed.Width * .90f;
                float aspect = texture.width / (float)Mathf.Max(1, texture.height);
                rect.sizeDelta = aspect >= 1f
                    ? new Vector2(maxSize, maxSize / aspect)
                    : new Vector2(maxSize * aspect, maxSize);
                rect.localRotation = Quaternion.Euler(0, 0, seed.Tilt + seed.Spin * (now + seed.Phase));
                image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(intensity * seed.Brightness * alpha));
            }
        }

        private Texture2D AssetTexture(MeteorSeed seed)
        {
            if (trailMode == MeteorTrailMode.Emoji)
            {
                EnsureEmojiTextures();
                return emojiTextures.Length == 0 ? null : emojiTextures[seed.EmojiIndex % emojiTextures.Length];
            }
            if (trailMode != MeteorTrailMode.Images || importedImagePaths.Length == 0) return null;
            string path = importedImagePaths[seed.EmojiIndex % importedImagePaths.Length];
            if (BackgroundImageStore.TryGetRuntimeTexture(path, out Texture2D cached)) return cached;
            if (imageLoadBudget <= 0) return null;
            imageLoadBudget--;
            return BackgroundImageStore.LoadRuntimeTexture(path);
        }

        private struct MeteorSeed
        {
            public float Phase;
            public float Duration;
            public float X;
            public float Drift;
            public float Width;
            public float Height;
            public float Tilt;
            public float Spin;
            public int ColorIndex;
            public int EmojiIndex;
            public float Brightness;
        }
    }
}
