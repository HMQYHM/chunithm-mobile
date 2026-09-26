using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// 皮肤四区触控表现层。它只读取安全皮肤事件和图片资源，不参与输入判定，
    /// 也不会加载皮肤包中的脚本、Prefab、Shader 或程序集。
    /// </summary>
    public sealed class GameplaySkinTouchOverlay : MonoBehaviour
    {
        private RawImage image;
        private GameplaySkinInfo activeSkin;
        private GameplaySkinStyle style;

        /// <summary>创建位于游玩画面左侧中部的非交互图片层。</summary>
        public void Initialize(RectTransform parent)
        {
            if (parent == null) return;
            RectTransform root = GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            GameObject child = new GameObject("Skin touch mascot image", typeof(RectTransform), typeof(RawImage));
            child.transform.SetParent(root, false);
            image = child.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = Vector2.zero;
            image.rectTransform.offsetMax = Vector2.zero;
            RefreshSkin();
            GameplaySkinEvents.TouchZonesUpdated += OnTouchZonesUpdated;
            ApplyConfiguredLayout();
        }

        private void OnDestroy() => GameplaySkinEvents.TouchZonesUpdated -= OnTouchZonesUpdated;

        /// <summary>皮肤切换后重新读取资源；内置原版皮肤不显示外部装饰。</summary>
        public void RefreshSkin()
        {
            activeSkin = GameplaySkinManager.Active;
            style = GameplaySkinManager.Style;
            ApplyConfiguredLayout();
            SetImage(style.TouchIdlePath);
        }

        private void ApplyConfiguredLayout()
        {
            if (image == null || style == null) return;
            RectTransform root = (RectTransform)transform;
            root.anchorMin = new Vector2(style.TouchOverlayX, style.TouchOverlayY);
            root.anchorMax = new Vector2(style.TouchOverlayX + style.TouchOverlayWidth,
                style.TouchOverlayY + style.TouchOverlayHeight);
            root.gameObject.SetActive(!activeSkin.BuiltIn && style.TouchOverlayEnabled);
        }

        private void OnTouchZonesUpdated(GameplaySkinEvents.TouchZoneSnapshot zones)
        {
            if (activeSkin == null || activeSkin.BuiltIn || style == null || !style.TouchOverlayEnabled) return;
            // 多区同时触摸时使用固定优先级，保证图片不会在同一帧反复切换。
            if (zones.RightBottom) SetImage(style.TouchRightBottomPath);
            else if (zones.RightTop) SetImage(style.TouchRightTopPath);
            else if (zones.LeftBottom) SetImage(style.TouchLeftBottomPath);
            else if (zones.LeftTop) SetImage(style.TouchLeftTopPath);
            else SetImage(style.TouchIdlePath);
        }

        private void SetImage(string path)
        {
            if (image == null) return;
            Texture2D texture = GameplaySkinManager.LoadSkinImage(activeSkin, path);
            image.texture = texture;
            image.gameObject.SetActive(texture != null);
        }
    }
}
