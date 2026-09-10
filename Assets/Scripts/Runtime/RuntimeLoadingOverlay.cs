using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>Temporary runtime loading surface; final UI will replace its styling.</summary>
    public sealed class RuntimeLoadingOverlay : MonoBehaviour
    {
        private GameObject surface;
        private Image fill;
        private Image background;
        private Image track;
        private Text label;
        private CanvasGroup canvasGroup;
        public bool Visible { get; private set; } = true;

        private void Awake()
        {
            surface = new GameObject("Loading Canvas");
            surface.transform.SetParent(transform, false);
            var canvas = surface.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10000;
            surface.AddComponent<GraphicRaycaster>();
            canvasGroup = surface.AddComponent<CanvasGroup>();
            var scaler = surface.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            background = CreateImage("Background", surface.transform, new Color(.005f, .008f, .015f));
            background.raycastTarget = true;
            Stretch(background.rectTransform, 0, 0, 1, 1);

            track = CreateImage("Progress Track", surface.transform, new Color(.10f, .16f, .22f));
            Stretch(track.rectTransform, .06f, .055f, .94f, .075f);
            fill = CreateImage("Progress Fill", track.transform, new Color(.10f, .88f, 1f));
            Stretch(fill.rectTransform, 0, 0, 0, 1);

            var textObject = new GameObject("Loading Status");
            textObject.transform.SetParent(surface.transform, false);
            label = textObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 34;
            label.alignment = TextAnchor.LowerLeft;
            label.color = Color.white;
            Stretch(label.rectTransform, .06f, .085f, .94f, .15f);
            SetHighContrastPalette(AppSettings.Load().HighContrastUi);
            Show("正在加载歌曲、音乐和谱面…", 0);
        }

        public void SetHighContrastPalette(bool enabled)
        {
            if (background != null) background.color = enabled ? UiThemeContrast.Navy : new Color(.005f, .008f, .015f);
            if (track != null) track.color = enabled ? Color.white : new Color(.10f, .16f, .22f);
            if (fill != null) fill.color = enabled ? UiThemeContrast.Yellow : new Color(.10f, .88f, 1f);
        }

        public void Show(string message, float value = 0)
        {
            if (label != null) label.text = message;
            SetFill(value);
            Visible = true;
            if (surface != null) {
                surface.SetActive(true);
                if (canvasGroup != null) canvasGroup.alpha = 1f;
            }
        }

        public IEnumerator ShowAnimated(string message, float value, float duration)
        {
            if (label != null) label.text = message;
            SetFill(value);
            Visible = true;
            if (surface == null) yield break;
            surface.SetActive(true);
            if (canvasGroup == null) yield break;
            canvasGroup.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < duration) {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            canvasGroup.alpha = 1f;
        }

        public void SetProgress(string message, float value)
        {
            if (label != null) label.text = message;
            SetFill(value);
        }

        public void Hide()
        {
            Visible = false;
            if (surface != null) {
                surface.SetActive(false);
                if (canvasGroup != null) canvasGroup.alpha = 1f;
            }
        }

        public IEnumerator HideAnimated(float duration)
        {
            if (surface == null) yield break;
            if (canvasGroup == null) {
                Hide();
                yield break;
            }
            float from = canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < duration) {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.SmoothStep(from, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            Hide();
        }

        private void SetFill(float value)
        {
            if (fill == null) return;
            Vector2 maximum = fill.rectTransform.anchorMax;
            maximum.x = Mathf.Clamp01(value);
            fill.rectTransform.anchorMax = maximum;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
