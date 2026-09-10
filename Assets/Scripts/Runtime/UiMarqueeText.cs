using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>Scrolls overflowing legacy UI text after a short reading pause.</summary>
    public sealed class UiMarqueeText : MonoBehaviour
    {
        public Text Text;
        public RectTransform Viewport;
        public float PixelsPerSecond = 34f;
        public float PauseSeconds = 1.1f;
        private float elapsed;
        private float nextMeasureAt;
        private float cachedViewportWidth = -1f;
        private float cachedOverflow;
        private int cachedVisibleCharacters;
        private string cachedText;

        private void OnEnable() { elapsed = 0; ResetPosition(); }

        private void Update()
        {
            if (Text == null || Viewport == null) return;
            float viewportWidth = Mathf.Max(0, Viewport.rect.width);
            if (cachedText != Text.text || Mathf.Abs(cachedViewportWidth - viewportWidth) > 1f ||
                Time.unscaledTime >= nextMeasureAt)
            {
                float preferredWidth = Mathf.Max(0, Text.preferredWidth);
                cachedOverflow = preferredWidth - viewportWidth;
                cachedVisibleCharacters = Mathf.FloorToInt(viewportWidth / Mathf.Max(1f, Text.fontSize * 1.05f));
                cachedViewportWidth = viewportWidth;
                cachedText = Text.text;
                nextMeasureAt = Time.unscaledTime + .18f;
            }
            float overflow = cachedOverflow;
            int definitelyVisibleCharacters = cachedVisibleCharacters;
            // Ignore tiny layout/font rounding differences. Without this guard even a
            // label that visually fits can drift left and have its first glyph clipped.
            // The character-capacity check also protects CJK strings when a dynamic
            // font reports an inflated preferred width during its first atlas rebuild.
            if (viewportWidth <= 1 || Text.text.Length <= definitelyVisibleCharacters ||
                overflow <= Mathf.Max(2f, Text.fontSize * .75f))
            {
                elapsed = 0;
                ResetPosition();
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            float travel = overflow / Mathf.Max(1, PixelsPerSecond);
            float cycle = PauseSeconds + travel + PauseSeconds + travel;
            float phase = elapsed % cycle;
            float x;
            if (phase < PauseSeconds) x = 0;
            else if (phase < PauseSeconds + travel) x = -(phase - PauseSeconds) * PixelsPerSecond;
            else if (phase < PauseSeconds * 2 + travel) x = -overflow;
            else x = -overflow + (phase - PauseSeconds * 2 - travel) * PixelsPerSecond;
            Text.rectTransform.anchoredPosition = new Vector2(x, 0);
        }

        private void ResetPosition()
        {
            if (Text != null) Text.rectTransform.anchoredPosition = Vector2.zero;
        }

        public void Restart()
        {
            elapsed = 0f;
            cachedText = null;
            cachedViewportWidth = -1f;
            nextMeasureAt = 0f;
            ResetPosition();
        }
    }
}
