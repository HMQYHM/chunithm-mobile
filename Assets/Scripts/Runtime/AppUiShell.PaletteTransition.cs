using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public sealed partial class AppUiShell
    {
        private Button homePaletteButton;
        private GameObject paletteTransitionObject;
        private CanvasGroup paletteTransitionGroup;
        private Image paletteTransitionWash;
        private Text paletteLeavingGlyph;
        private Text paletteLeavingDetails;
        private Text paletteEnteringGlyph;
        private Text paletteEnteringDetails;

        private void BuildHomePaletteToggle(RectTransform page)
        {
            Color background = HighContrastUi ? UiThemeContrast.Navy : new Color(.07f, .15f, .22f, .98f);
            Color iconColor = HighContrastUi ? UiThemeContrast.Yellow : new Color(.78f, .90f, 1f, 1f);
            RectTransform frame = Panel("Home palette switch", page, background,
                new Vector2(.075f, .17f), new Vector2(.075f, .17f), true);
            frame.pivot = Vector2.zero;
            frame.sizeDelta = new Vector2(60, 60);
            frame.anchoredPosition = Vector2.zero;
            homePaletteButton = frame.gameObject.AddComponent<Button>();
            homePaletteButton.targetGraphic = frame.GetComponent<Image>();
            ColorBlock colors = homePaletteButton.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, .18f);
            colors.pressedColor = Color.Lerp(background, Color.black, .22f);
            colors.fadeDuration = .08f;
            homePaletteButton.colors = colors;
            homePaletteButton.onClick.AddListener(() => ChangeUiPalette(!HighContrastUi));
            frame.gameObject.AddComponent<UiPressAnimation>();

            Text icon = Label(HighContrastUi ? "☀" : "☾", frame, 30, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, iconColor, true);
            icon.raycastTarget = false;
            icon.horizontalOverflow = HorizontalWrapMode.Overflow;
            icon.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void EnsurePaletteTransitionOverlay()
        {
            if (paletteTransitionObject != null && paletteTransitionGroup != null) return;

            paletteTransitionObject = new GameObject("UI palette transition", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster));
            paletteTransitionObject.transform.SetParent(transform, false);
            Canvas transitionCanvas = paletteTransitionObject.GetComponent<Canvas>();
            transitionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            transitionCanvas.sortingOrder = 1000;
            CanvasScaler scaler = paletteTransitionObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .45f;
            paletteTransitionGroup = paletteTransitionObject.GetComponent<CanvasGroup>();
            paletteTransitionGroup.alpha = 0;
            paletteTransitionGroup.blocksRaycasts = false;
            paletteTransitionGroup.interactable = false;

            RectTransform stage = (RectTransform)paletteTransitionObject.transform;
            stage.anchorMin = Vector2.zero; stage.anchorMax = Vector2.one;
            stage.offsetMin = Vector2.zero; stage.offsetMax = Vector2.zero;
            paletteTransitionWash = stage.gameObject.AddComponent<Image>();
            paletteTransitionWash.color = Color.clear;
            // Keep the transition layer modal while its alpha is animated so a
            // tap cannot leak through to the page being rebuilt underneath.
            paletteTransitionWash.raycastTarget = true;

            paletteLeavingGlyph = CreatePaletteGlyph("Leaving", stage, "☾", 156, new Color(.75f, .90f, 1f, 1f));
            paletteLeavingDetails = CreatePaletteGlyph("Leaving stars", stage, "✦   ·   ✧", 42, new Color(.55f, .82f, 1f, .92f));
            paletteEnteringGlyph = CreatePaletteGlyph("Entering", stage, "☀", 156, UiThemeContrast.Yellow);
            paletteEnteringDetails = CreatePaletteGlyph("Entering clouds", stage, "☁   ☁", 46, Color.white);
            paletteTransitionObject.SetActive(false);
        }

        private Text CreatePaletteGlyph(string name, RectTransform parent, string value, int size, Color color)
        {
            GameObject objectValue = new GameObject(name, typeof(RectTransform), typeof(Text));
            objectValue.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)objectValue.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(720, 190);
            Text text = objectValue.GetComponent<Text>();
            text.font = size >= 80 ? titleFont : font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Shadow shadow = objectValue.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .42f);
            shadow.effectDistance = new Vector2(0, -8);
            return text;
        }

        private void SetPaletteGlyph(Text glyph, Vector2 position, float scale, bool active)
        {
            if (glyph == null) return;
            glyph.rectTransform.anchoredPosition = position;
            glyph.rectTransform.localScale = Vector3.one * scale;
            glyph.gameObject.SetActive(active);
        }

        private IEnumerator AnimateUiPalette(bool enabled)
        {
            transitioning = true;
            EnsurePaletteTransitionOverlay();
            paletteTransitionObject.SetActive(true);
            paletteTransitionGroup.alpha = 1f;
            paletteTransitionGroup.blocksRaycasts = true;
            paletteTransitionGroup.interactable = false;

            bool leavingHighContrast = settings.HighContrastUi;
            // Persist the target immediately. If Android suspends or kills the
            // process during the transition, the next launch still uses the UI
            // palette the user selected.
            settings.HighContrastUi = enabled;
            settings.Save();
            Text outgoingGlyph = leavingHighContrast ? paletteEnteringGlyph : paletteLeavingGlyph;
            Text outgoingDetails = leavingHighContrast ? paletteEnteringDetails : paletteLeavingDetails;
            Text incomingGlyph = enabled ? paletteEnteringGlyph : paletteLeavingGlyph;
            Text incomingDetails = enabled ? paletteEnteringDetails : paletteLeavingDetails;
            outgoingGlyph.color = WithAlpha(outgoingGlyph.color, 1f);
            outgoingDetails.color = WithAlpha(outgoingDetails.color, 1f);
            incomingGlyph.color = WithAlpha(incomingGlyph.color, 1f);
            incomingDetails.color = WithAlpha(incomingDetails.color, 1f);
            SetPaletteGlyph(outgoingGlyph, Vector2.zero, 1f, true);
            SetPaletteGlyph(outgoingDetails, Vector2.zero, 1f, true);
            SetPaletteGlyph(incomingGlyph, Vector2.zero, .88f, true);
            SetPaletteGlyph(incomingDetails, Vector2.zero, .88f, true);
            if (outgoingGlyph == incomingGlyph) SetPaletteGlyph(outgoingGlyph, Vector2.zero, 1f, true);
            paletteTransitionWash.color = enabled
                ? new Color(UiThemeContrast.Navy.r, UiThemeContrast.Navy.g, UiThemeContrast.Navy.b, 0)
                : new Color(.008f, .015f, .030f, 0);

            float time = 0f;
            const float coverDuration = .30f;
            while (time < coverDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = SmoothStep(Mathf.Clamp01(time / coverDuration));
                paletteTransitionWash.color = WithAlpha(paletteTransitionWash.color, Mathf.Lerp(0f, .94f, t));
                SetPaletteGlyph(outgoingGlyph, Vector2.zero, Mathf.Lerp(1f, .92f, t), true);
                SetPaletteGlyph(outgoingDetails, Vector2.zero, Mathf.Lerp(1f, .92f, t), true);
                outgoingGlyph.color = WithAlpha(outgoingGlyph.color, 1f - t);
                outgoingDetails.color = WithAlpha(outgoingDetails.color, 1f - t);
                yield return null;
            }

            float scroll = settingsScrollRectValue;
            RebuildUiForLanguage();
            RestoreSettingsScroll(scroll);
            Canvas.ForceUpdateCanvases();
            yield return null;

            time = 0f;
            const float revealDuration = .42f;
            while (time < revealDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = SmoothStep(Mathf.Clamp01(time / revealDuration));
                paletteTransitionWash.color = WithAlpha(paletteTransitionWash.color, Mathf.Lerp(.94f, 0f, t));
                SetPaletteGlyph(incomingGlyph, Vector2.zero, Mathf.Lerp(.88f, 1f, t), true);
                SetPaletteGlyph(incomingDetails, Vector2.zero, Mathf.Lerp(.88f, 1f, t), true);
                incomingGlyph.color = WithAlpha(incomingGlyph.color, t);
                incomingDetails.color = WithAlpha(incomingDetails.color, incomingGlyph.color.a);
                yield return null;
            }

            SetPaletteGlyph(outgoingGlyph, Vector2.zero, 1f, false);
            SetPaletteGlyph(outgoingDetails, Vector2.zero, 1f, false);
            SetPaletteGlyph(incomingGlyph, Vector2.zero, 1f, false);
            SetPaletteGlyph(incomingDetails, Vector2.zero, 1f, false);
            paletteTransitionGroup.alpha = 0f;
            paletteTransitionGroup.blocksRaycasts = false;
            paletteTransitionObject.SetActive(false);
            transitioning = false;
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private float settingsScrollRectValue => settingsScroll == null ? -1f : settingsScroll.verticalNormalizedPosition;

        private void RestoreSettingsScroll(float value)
        {
            if (value >= 0f && settingsScroll != null) settingsScroll.verticalNormalizedPosition = value;
        }
    }
}
