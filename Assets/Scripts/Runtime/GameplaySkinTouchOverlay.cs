using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// 皮肤触控表现层。皮肤包只能通过 JSON 指定图片和布局，
    /// 触控数据来自只读事件，不会参与判定，也不会执行外部脚本。
    /// </summary>
    public sealed class GameplaySkinTouchOverlay : MonoBehaviour
    {
        private RectTransform root;
        private RawImage legacyImage;
        private RawImage tabletImage;
        private RawImage leftPawImage;
        private RawImage rightPawImage;
        private GameplaySkinInfo activeSkin;
        private GameplaySkinStyle style;
        private GameplaySkinEvents.TouchTelemetrySnapshot telemetry;
        private Vector2 leftPawPosition = new Vector2(.25f, .5f);
        private Vector2 rightPawPosition = new Vector2(.75f, .5f);
        private bool hasTelemetry;

        /// <summary>创建全屏非交互层，具体平板和猫爪位置由皮肤配置决定。</summary>
        public void Initialize(RectTransform parent)
        {
            if (parent == null) return;
            root = GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            legacyImage = CreateImage("Skin touch mascot image", root);
            tabletImage = CreateImage("Skin touch tablet", root);
            leftPawImage = CreateImage("Skin touch left paw", root);
            rightPawImage = CreateImage("Skin touch right paw", root);

            GameplaySkinEvents.TouchZonesUpdated += OnTouchZonesUpdated;
            GameplaySkinEvents.TouchTelemetryUpdated += OnTouchTelemetryUpdated;
            RefreshSkin();
        }

        private static RawImage CreateImage(string name, RectTransform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            child.transform.SetParent(parent, false);
            RawImage image = child.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;
            return image;
        }

        private void OnDestroy()
        {
            GameplaySkinEvents.TouchZonesUpdated -= OnTouchZonesUpdated;
            GameplaySkinEvents.TouchTelemetryUpdated -= OnTouchTelemetryUpdated;
        }

        /// <summary>皮肤切换后重新读取清单和图片资源。</summary>
        public void RefreshSkin()
        {
            activeSkin = GameplaySkinManager.Active;
            style = GameplaySkinManager.Style;
            hasTelemetry = false;
            ApplyConfiguredLayout();
            SetImage(legacyImage, style.TouchIdlePath);
            SetImage(tabletImage, style.TouchTabletPath);
            SetImage(leftPawImage, style.TouchLeftPawPath);
            SetImage(rightPawImage, style.TouchRightPawPath);
            ApplyConfiguredLayout();
            UpdateTrackedPaws(true);
        }

        private void ApplyConfiguredLayout()
        {
            if (root == null || style == null || activeSkin == null) return;
            bool external = !activeSkin.BuiltIn;
            root.gameObject.SetActive(external && (style.TouchOverlayEnabled || style.TouchTabletEnabled));
            SetNormalizedRect(legacyImage != null ? legacyImage.rectTransform : null,
                style.TouchOverlayX, style.TouchOverlayY, style.TouchOverlayWidth, style.TouchOverlayHeight);
            SetNormalizedRect(tabletImage != null ? tabletImage.rectTransform : null,
                style.TouchTabletX, style.TouchTabletY, style.TouchTabletWidth, style.TouchTabletHeight);
            if (legacyImage != null) legacyImage.gameObject.SetActive(external && style.TouchOverlayEnabled);
            if (tabletImage != null) tabletImage.gameObject.SetActive(external && style.TouchTabletEnabled &&
                tabletImage.texture != null);
            if (leftPawImage != null) leftPawImage.gameObject.SetActive(external && style.TouchTabletEnabled &&
                leftPawImage.texture != null);
            if (rightPawImage != null) rightPawImage.gameObject.SetActive(external && style.TouchTabletEnabled &&
                rightPawImage.texture != null);
        }

        private static void SetNormalizedRect(RectTransform target, float x, float y, float width, float height)
        {
            if (target == null) return;
            target.anchorMin = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
            target.anchorMax = new Vector2(Mathf.Clamp01(x + width), Mathf.Clamp01(y + height));
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }

        private void OnTouchZonesUpdated(GameplaySkinEvents.TouchZoneSnapshot zones)
        {
            if (style == null || activeSkin == null || activeSkin.BuiltIn || style.TouchTabletEnabled) return;
            // Keep the older four-zone skin format working without changing its behavior.
            if (zones.RightBottom) SetImage(legacyImage, style.TouchRightBottomPath);
            else if (zones.RightTop) SetImage(legacyImage, style.TouchRightTopPath);
            else if (zones.LeftBottom) SetImage(legacyImage, style.TouchLeftBottomPath);
            else if (zones.LeftTop) SetImage(legacyImage, style.TouchLeftTopPath);
            else SetImage(legacyImage, style.TouchIdlePath);
        }

        private void OnTouchTelemetryUpdated(GameplaySkinEvents.TouchTelemetrySnapshot value)
        {
            telemetry = value;
            hasTelemetry = true;
            UpdateTrackedPaws(false);
        }

        private void UpdateTrackedPaws(bool immediate)
        {
            if (style == null || !style.TouchTabletEnabled) return;
            leftPawPosition = MovePaw(leftPawPosition, telemetry.Left, true, immediate);
            rightPawPosition = MovePaw(rightPawPosition, telemetry.Right, false, immediate);
            SetPawRect(leftPawImage, leftPawPosition, style.TouchLeftPawWidth, style.TouchLeftPawHeight);
            SetPawRect(rightPawImage, rightPawPosition, style.TouchRightPawWidth, style.TouchRightPawHeight);
        }

        private Vector2 MovePaw(Vector2 current, GameplaySkinEvents.TouchTrack track,
            bool left, bool immediate)
        {
            Vector2 target = left ? new Vector2(.25f, .52f) : new Vector2(.75f, .52f);
            if (track.Active && telemetry.InputActive) target = new Vector2(track.CenterX, track.CenterY);
            float smoothing = left ? style.TouchLeftPawSmoothing : style.TouchRightPawSmoothing;
            float offsetX = left ? style.TouchLeftPawOffsetX : style.TouchRightPawOffsetX;
            float offsetY = left ? style.TouchLeftPawOffsetY : style.TouchRightPawOffsetY;
            target += new Vector2(offsetX, offsetY);
            Vector2 result = immediate || !hasTelemetry ? target :
                Vector2.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(.01f, smoothing) * 60f * Time.unscaledDeltaTime));
            return new Vector2(Mathf.Clamp01(result.x), Mathf.Clamp01(result.y));
        }

        private void SetPawRect(RawImage image, Vector2 normalizedPosition, float width, float height)
        {
            if (image == null) return;
            float x = style.TouchTabletX + normalizedPosition.x * style.TouchTabletWidth - style.TouchTabletWidth * width * .5f;
            float y = style.TouchTabletY + normalizedPosition.y * style.TouchTabletHeight - style.TouchTabletHeight * height * .5f;
            SetNormalizedRect(image.rectTransform, x, y, style.TouchTabletWidth * width,
                style.TouchTabletHeight * height);
        }

        private void SetImage(RawImage image, string path)
        {
            if (image == null || activeSkin == null) return;
            image.texture = GameplaySkinManager.LoadSkinImage(activeSkin, path);
        }
    }
}
