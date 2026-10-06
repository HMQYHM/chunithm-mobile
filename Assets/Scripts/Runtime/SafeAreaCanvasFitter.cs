using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Keeps screen-space UI inside the iOS/iPadOS safe area after a rotation,
    /// scene activation, or a window-size change. The Canvas background is
    /// allowed to become slightly inset; this is preferable to clipping text,
    /// buttons, or the import dialog below a notch or rounded display corner.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaCanvasFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafeArea;
        private int lastWidth;
        private int lastHeight;

        private void Awake()
        {
            rect = (RectTransform)transform;
            Apply(true);
        }

        private void Update()
        {
            Apply(false);
        }

        private void Apply(bool force)
        {
            Rect safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            if (!force && safe == lastSafeArea &&
                Screen.width == lastWidth && Screen.height == lastHeight) return;

            Vector2 size = new Vector2(Screen.width, Screen.height);
            Vector2 min = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            Vector2 max = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            lastSafeArea = safe;
            lastWidth = Screen.width;
            lastHeight = Screen.height;
        }
    }
}
