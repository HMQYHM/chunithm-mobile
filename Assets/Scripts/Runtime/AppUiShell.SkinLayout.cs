using System;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public partial class AppUiShell
    {
        /// <summary>
        /// Applies only declarative overrides from the active skin. Buttons keep
        /// their game-owned callbacks; a skin can change their appearance and
        /// bounds but cannot replace their behavior.
        /// </summary>
        private void ApplyGameplaySkinLayout(GameplaySkinStyle style)
        {
            if (style == null) return;
            if (!pages.TryGetValue(AppPage.Gameplay, out RectTransform canvas)) return;
            // Parent geometry must settle before child coordinates are converted.
            // Export order is a layer list, not a transform dependency order.
            foreach (GameplaySkinElementDefinition element in style.LayoutElements)
            {
                if (element == null) continue;
                RectTransform target = LayoutTarget(element.Id);
                if (target == null) continue;
                if (element.Id == "songCover" || element.Id == "judgementImage") continue;
                SetCanvasSkinRect(target, canvas, element);
            }
            foreach (GameplaySkinElementDefinition element in style.LayoutElements)
            {
                if (element == null) continue;
                RectTransform target = LayoutTarget(element.Id);
                if (target == null) continue;
                if (element.Id == "songCover")
                    SetCanvasSkinRect(gameplaySongInfoCoverMask, canvas, element);
                if (element.Id == "judgementImage") {
                    // FitInParent otherwise overwrites the authored rectangle.
                    var fitter = target.GetComponent<AspectRatioFitter>();
                    if (fitter != null) fitter.enabled = false;
                    SetCanvasSkinRect(target, canvas, element);
                }
                ApplySkinVisual(target.gameObject, element, IsDynamicLayoutElement(element.Id));
            }
        }

        private RectTransform LayoutTarget(string id)
        {
            switch (id)
            {
                case "songInfo": return gameplaySongInfoBlock;
                case "songCover": return gameplaySongInfoCover?.rectTransform;
                case "scoreHud": return gameplayHud;
                case "comboHud": return gameplayCombo?.rectTransform;
                case "judgement": return gameplayJudgementBlock;
                case "judgementImage": return gameplayJudgementImage?.rectTransform;
                case "pauseButton": return gameplayPauseButton?.transform as RectTransform;
                case "pausePanel": return gameplayPausePanel;
                case "progressBar": return gameplayProgressTrack?.rectTransform;
                case "fps": return gameplayFpsLabel?.rectTransform;
                case "countdown": return gameplayCountdown?.rectTransform;
                case "touchpad": return gameplayTouchpad;
                case "airZone": return gameplayAirZone;
                case "airBoundaryBottom": return gameplayAirBoundaryBottom?.rectTransform;
                case "airBoundaryTop": return gameplayAirBoundaryTop?.rectTransform;
                case "airGuide": return gameplayAirGuide?.rectTransform;
                case "trackMask": return gameplayTrackMask?.transform as RectTransform;
                case "touchFeedback": return gameplayTouchContacts?.transform as RectTransform;
                default: return null;
            }
        }

        private static bool IsDynamicLayoutElement(string id)
        {
            return id == "pausePanel" || id == "judgement" || id == "judgementImage" ||
                id == "countdown" || id == "airGuide" || id == "touchFeedback";
        }

        /// <summary>Match sensor bounds to authored region rectangles, without changing scoring rules.</summary>
        private void ApplySkinInputAreas(GameplaySkinStyle style)
        {
            // The room's fullscreen MOD remains authoritative. A local skin
            // cannot restore AIR or shrink that MOD's full-screen ground area.
            if (IsFullscreenTouchpadActive() || inputRig == null || style == null) return;
            GameplaySkinElementDefinition ground = style.LayoutElement("touchpad");
            Rect viewport = SkinCanvasViewport();
            if (TrySkinArea(ground, out Rect groundArea)) inputRig.Ground?.SetPlayArea(MapSkinArea(groundArea, viewport));
            GameplaySkinElementDefinition air = style.LayoutElement("airZone");
            if (TrySkinArea(air, out Rect airArea)) inputRig.Air?.SetAirArea(MapSkinArea(airArea, viewport));
        }

        // Sensor coordinates are screen-relative; Studio coordinates belong to
        // the safe gameplay page. Use its actual corners, including iOS insets.
        private Rect SkinCanvasViewport()
        {
            if (!pages.TryGetValue(AppPage.Gameplay, out RectTransform page) || Screen.width <= 0 || Screen.height <= 0)
                return new Rect(0, 0, 1, 1);
            var corners = new Vector3[4];
            page.GetWorldCorners(corners);
            Canvas canvas = page.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x / Screen.width, min.y / Screen.height, max.x / Screen.width, max.y / Screen.height);
        }

        private static Rect MapSkinArea(Rect area, Rect viewport) => new Rect(
            viewport.x + area.x * viewport.width, viewport.y + area.y * viewport.height,
            area.width * viewport.width, area.height * viewport.height);

        private static bool TrySkinArea(GameplaySkinElementDefinition element, out Rect area)
        {
            area = default;
            if (element == null || element.X < 0 || element.Y < 0 || element.Width <= 0 || element.Height <= 0) return false;
            area = new Rect(element.X, element.Y, Mathf.Min(element.Width, 1f - element.X),
                Mathf.Min(element.Height, 1f - element.Y));
            // Visibility is visual only: hiding the input surface must not
            // accidentally disable touch sampling for a minimal HUD skin.
            return area.width >= .01f && area.height >= .01f;
        }

        private static void SetCanvasSkinRect(RectTransform target, RectTransform canvas, GameplaySkinElementDefinition element)
        {
            if (target == null || canvas == null || !(target.parent is RectTransform parent)) return;
            if (element.X < 0 || element.Y < 0 || element.Width <= 0 || element.Height <= 0) return;
            if (parent.rect.width <= 0 || parent.rect.height <= 0) return;
            Rect bounds = canvas.rect;
            Vector3 min = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(
                bounds.xMin + bounds.width * element.X, bounds.yMin + bounds.height * element.Y)));
            Vector3 max = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(
                bounds.xMin + bounds.width * Mathf.Min(1, element.X + element.Width),
                bounds.yMin + bounds.height * Mathf.Min(1, element.Y + element.Height))));
            target.anchorMin = new Vector2((min.x - parent.rect.xMin) / parent.rect.width, (min.y - parent.rect.yMin) / parent.rect.height);
            target.anchorMax = new Vector2((max.x - parent.rect.xMin) / parent.rect.width, (max.y - parent.rect.yMin) / parent.rect.height);
            target.offsetMin = target.offsetMax = Vector2.zero;
            target.pivot = new Vector2(.5f, .5f);
            target.localRotation = Quaternion.identity;
        }

        private void ApplySkinVisual(GameObject target, GameplaySkinElementDefinition element, bool dynamic)
        {
            if (target == null) return;
            if (dynamic) {
                // Dynamic panels are activated by gameplay state. A hidden
                // element stays disabled; a visible one keeps its runtime
                // state (for example, the pause panel starts closed).
                if (!element.Visible) target.SetActive(false);
            } else target.SetActive(element.Visible);
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.AddComponent<CanvasGroup>();
            group.alpha = element.Visible ? Mathf.Clamp01(element.Opacity) : 0f;
            // Keep the original interaction contract for buttons and panels
            // containing buttons. A visual skin may restyle them, but must not
            // make the pause control or its confirmation buttons untappable.
            bool interactive = target.GetComponent<Button>() != null ||
                target.GetComponentInChildren<Button>(true) != null;
            group.blocksRaycasts = interactive;
            group.interactable = interactive;

            if (!string.IsNullOrEmpty(element.Color) &&
                ColorUtility.TryParseHtmlString(element.Color, out Color color))
            {
                Graphic graphic = target.GetComponent<Graphic>();
                if (graphic != null) graphic.color = color;
            }
            ApplySkinImage(target, element.Image);
            if (!string.IsNullOrEmpty(element.Label))
            {
                Text label = target.GetComponentInChildren<Text>(true);
                if (label != null) label.text = element.Label;
            }
        }

        private void ApplySkinImage(GameObject target, string path)
        {
            if (target == null || string.IsNullOrEmpty(path)) return;
            RawImage raw = target.GetComponent<RawImage>();
            if (raw != null) {
                raw.texture = GameplaySkinManager.LoadSkinImage(GameplaySkinManager.Active, path);
                raw.raycastTarget = false;
                return;
            }
            Image image = target.GetComponent<Image>();
            if (image != null) {
                image.overrideSprite = GameplaySkinManager.LoadSkinSprite(GameplaySkinManager.Active, path);
                // Button graphics need their raycast target. Decorative images
                // remain non-interactive so they never steal gameplay touches.
                if (target.GetComponent<Button>() == null) image.raycastTarget = false;
            }
        }
    }
}
