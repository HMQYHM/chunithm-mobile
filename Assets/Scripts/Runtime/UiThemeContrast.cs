using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // UI 主题管理：应用高对比/低对比配色，并在启动时恢复上次选择。
    // 主题只作用于菜单和暂停界面；游玩谱面颜色和 MOD 状态颜色由各自系统控制。
    // Only dirty UI labels are checked. No per-frame traversal of the UI tree.
    public sealed class UiThemeContrast : MonoBehaviour
    {
        public static readonly Color Navy = new Color(.055f, .075f, .14f);
        public static readonly Color PageLight = new Color(.94f, .97f, 1f);
        public static readonly Color PanelLight = new Color(.88f, .93f, .97f);
        public static readonly Color Yellow = new Color(1f, .91f, .08f);
        public static readonly Color Cyan = new Color(.04f, .74f, 1f);
        public static readonly Color Magenta = new Color(1f, .12f, .59f);
        public static readonly Color Success = new Color(.05f, .58f, .24f);
        public static readonly Color Danger = new Color(.82f, .08f, .13f);
        public static readonly Color MutedOnLight = new Color(.12f, .17f, .27f);
        private static readonly HashSet<UiThemeContrast> Pending = new HashSet<UiThemeContrast>();
        private static readonly List<UiThemeContrast> Work = new List<UiThemeContrast>();
        private readonly List<Image> backgrounds = new List<Image>();
        private Text label;
        private bool applying;
        private bool subscribed;

        public void Bind(Text value)
        {
            Unsubscribe();
            backgrounds.Clear();
            label = value;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent) {
                Image image = parent.GetComponent<Image>();
                if (image != null) {
                    backgrounds.Add(image);
                }
            }
            Canvas.willRenderCanvases -= Flush;
            Canvas.willRenderCanvases += Flush;
            if (isActiveAndEnabled) Subscribe();
            Apply();
        }

        private void OnEnable() { Subscribe(); MarkDirty(); }
        private void MarkDirty() { if (this != null && !applying && isActiveAndEnabled) Pending.Add(this); }
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (subscribed || label == null) return;
            subscribed = true;
            label.RegisterDirtyVerticesCallback(MarkDirty);
            foreach (Image image in backgrounds)
                if (image != null) image.RegisterDirtyVerticesCallback(MarkDirty);
        }

        private void Unsubscribe()
        {
            Pending.Remove(this);
            if (!subscribed) return;
            subscribed = false;
            if (label != null) label.UnregisterDirtyVerticesCallback(MarkDirty);
            foreach (Image image in backgrounds)
                if (image != null) image.UnregisterDirtyVerticesCallback(MarkDirty);
        }

        private static void Flush()
        {
            if (Pending.Count == 0) return;
            Work.AddRange(Pending);
            Pending.Clear();
            foreach (UiThemeContrast item in Work)
                if (item != null && item.isActiveAndEnabled) item.Apply();
            Work.Clear();
        }

        private void Apply()
        {
            if (label == null) return;
            Color background = Navy;
            for (int i = backgrounds.Count - 1; i >= 0; i--) {
                Image image = backgrounds[i];
                if (image != null && image.enabled) background = Color.Lerp(background, image.color, image.color.a);
            }
            Color foreground = label.color;
            if (Ratio(foreground, background) >= 4.5f) return;
            Color replacement = Ratio(Navy, background) > Ratio(Color.white, background) ? Navy : Color.white;
            replacement.a = foreground.a;
            applying = true;
            label.color = replacement;
            applying = false;
        }

        public static float Ratio(Color first, Color second)
        {
            float a = Luminance(first), b = Luminance(second);
            return (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
        }

        private static float Luminance(Color color)
        {
            Color linear = color.linear;
            return .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
        }

        /// <summary>
        /// Maps only application-surface colors. Note and playfield colors are
        /// deliberately kept outside this helper because they are gameplay
        /// semantics rather than interface chrome.
        /// </summary>
        public static Color ResolveSurface(Color source, string objectName)
        {
            if (source.a <= .01f) return source;
            if (source == Color.white) return Color.white;
            if (Near(source, PageLight) || Near(source, PanelLight)) return source;
            if (Near(source, Yellow) || Near(source, Cyan) || Near(source, Magenta) ||
                Near(source, Success) || Near(source, Danger))
                return source;

            string name = objectName ?? string.Empty;
            if (name.IndexOf("error", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("danger", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("kick", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return WithAlpha(Danger, source.a);

            float max = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
            float min = Mathf.Min(source.r, Mathf.Min(source.g, source.b));
            float saturation = max <= .001f ? 0f : (max - min) / max;

            // Existing blue/cyan action surfaces become the secondary cyan
            // accent; purple surfaces become the restrained hot-pink accent.
            if (max > .18f && source.b > source.r * 1.35f && source.g > source.r * 1.12f)
                return WithAlpha(Cyan, source.a);
            if (max > .18f && source.r > source.g * 1.45f && source.b > source.g * 1.20f)
                return WithAlpha(Magenta, source.a);
            if (max > .72f && min > .54f) return WithAlpha(Color.white, source.a);

            // Ordinary application surfaces use a light base. Gameplay and
            // semantic MOD colors are excluded by the caller.
            if (max < .38f || (max < .56f && saturation < .72f))
                return WithAlpha(PanelLight, source.a);

            return source;
        }

        private static Color WithAlpha(Color value, float alpha)
        {
            value.a = alpha;
            return value;
        }

        private static bool Near(Color first, Color second)
        {
            return Mathf.Abs(first.r - second.r) < .015f &&
                Mathf.Abs(first.g - second.g) < .015f &&
                Mathf.Abs(first.b - second.b) < .015f;
        }
    }
}
