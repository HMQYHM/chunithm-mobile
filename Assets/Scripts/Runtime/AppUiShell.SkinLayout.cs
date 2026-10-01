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
            foreach (GameplaySkinElementDefinition element in style.LayoutElements)
            {
                if (element == null) continue;
                RectTransform target = LayoutTarget(element.Id);
                if (target == null) continue;
                ApplySkinRect(target, element);
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
            if (TrySkinArea(ground, out Rect groundArea)) inputRig.Ground?.SetPlayArea(groundArea);
            GameplaySkinElementDefinition air = style.LayoutElement("airZone");
            if (TrySkinArea(air, out Rect airArea)) inputRig.Air?.SetAirArea(airArea);
        }

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

        private void ApplySkinRect(RectTransform target, GameplaySkinElementDefinition element)
        {
            if (element.X < 0 || element.Y < 0 || element.Width <= 0 || element.Height <= 0) return;
            target.anchorMin = new Vector2(Mathf.Clamp01(element.X), Mathf.Clamp01(element.Y));
            target.anchorMax = new Vector2(Mathf.Clamp01(element.X + element.Width),
                Mathf.Clamp01(element.Y + element.Height));
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
