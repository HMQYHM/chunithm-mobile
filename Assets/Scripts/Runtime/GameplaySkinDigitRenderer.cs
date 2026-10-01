using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Renders an authored 0-9 image set without replacing the game's score
    /// state or text contract. The source Text remains the fallback whenever a
    /// skin does not provide every digit needed by the current value.
    /// </summary>
    public sealed class GameplaySkinDigitRenderer : MonoBehaviour
    {
        private Text source;
        private string part;
        private GameObject layer;
        private string lastText;
        private int lastRevision = -1;
        private Color sourceColor;
        private int lastFontSize;

        public void Initialize(Text text, string stylePart)
        {
            source = text;
            part = stylePart;
            sourceColor = EffectiveColor();
            Refresh(true);
        }

        /// <summary>Refreshes the renderer after a skin or color setting changes.</summary>
        public void RefreshSkin()
        {
            sourceColor = EffectiveColor();
            lastRevision = -1;
            Refresh(true);
        }

        private void LateUpdate()
        {
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (source == null) return;
            Color color = EffectiveColor();
            if (!force && lastText == source.text && lastRevision == GameplaySkinManager.StyleRevision &&
                lastFontSize == source.fontSize && sourceColor == color) return;
            sourceColor = color;
            lastFontSize = source.fontSize;
            lastText = source.text;
            lastRevision = GameplaySkinManager.StyleRevision;
            string[] lines = (source.text ?? string.Empty).Split('\n');
            var rows = new List<List<Sprite>>();
            var labels = new List<string>();
            foreach (string line in lines)
            {
                var row = new List<Sprite>();
                if (line.Length == 0) { rows.Add(row); labels.Add(string.Empty); continue; }
                bool numeric = true;
                foreach (char character in line)
                {
                    if (character < '0' || character > '9') { numeric = false; break; }
                }
                if (!numeric)
                {
                    // Labels such as COMBO or 分数 stay as normal text below
                    // the image row. Mixed numeric labels remain a fallback.
                    if (line.Any(char.IsDigit)) { HideCustom(); return; }
                    rows.Add(row); labels.Add(line); continue;
                }
                foreach (char character in line)
                {
                    Sprite sprite = GameplaySkinManager.LoadDigitSprite(part, character);
                    if (sprite == null) { HideCustom(); return; }
                    row.Add(sprite);
                }
                rows.Add(row); labels.Add(string.Empty);
            }
            if (rows.Count == 0 || rows.TrueForAll(row => row.Count == 0)) { HideCustom(); return; }
            Build(rows, labels);
        }

        private void Build(List<List<Sprite>> rows, List<string> labels)
        {
            EnsureLayer();
            // Deferred destruction must not leave old digits in the layout for
            // the remainder of the frame; Editor self-tests cannot use Destroy.
            while (layer.transform.childCount > 0) {
                GameObject child = layer.transform.GetChild(0).gameObject;
                child.SetActive(false);
                child.transform.SetParent(null, false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            var vertical = layer.GetComponent<VerticalLayoutGroup>();
            vertical.childAlignment = TextAnchor.MiddleCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.spacing = -Mathf.Max(0, source.fontSize * .12f);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<Sprite> sprites = rows[rowIndex];
                if (sprites.Count == 0 && !string.IsNullOrEmpty(labels[rowIndex]))
                {
                    GameObject labelObject = new GameObject("Digit label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
                    labelObject.transform.SetParent(layer.transform, false);
                    Text label = labelObject.GetComponent<Text>();
                    label.font = source.font;
                    label.fontSize = source.fontSize;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = sourceColor;
                    label.raycastTarget = false;
                    label.text = labels[rowIndex];
                    continue;
                }
                if (sprites.Count == 0) continue;
                GameObject rowObject = new GameObject("Digit row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowObject.transform.SetParent(layer.transform, false);
                var row = rowObject.GetComponent<HorizontalLayoutGroup>();
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = false;
                row.childControlHeight = false;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                row.spacing = Mathf.Max(0, source.fontSize * .025f);
                foreach (Sprite sprite in sprites)
                {
                    GameObject imageObject = new GameObject("Digit", typeof(RectTransform), typeof(Image));
                    imageObject.transform.SetParent(rowObject.transform, false);
                    var image = imageObject.GetComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.color = sourceColor;
                    image.raycastTarget = false;
                    float aspect = sprite.rect.height > 0 ? sprite.rect.width / sprite.rect.height : 1f;
                    image.rectTransform.sizeDelta = new Vector2(source.fontSize * aspect, source.fontSize);
                }
            }
            source.color = new Color(sourceColor.r, sourceColor.g, sourceColor.b, 0f);
            layer.SetActive(true);
        }

        private void EnsureLayer()
        {
            if (layer != null) return;
            layer = new GameObject("Custom digit skin", typeof(RectTransform), typeof(VerticalLayoutGroup));
            layer.transform.SetParent(source.transform, false);
            var rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            layer.transform.SetAsLastSibling();
        }

        private void HideCustom()
        {
            if (layer != null) layer.SetActive(false);
            source.color = sourceColor;
        }

        // The element inspector overrides the part-wide tint, just as it does
        // for ordinary Text. Generated sprites never become input targets.
        private Color EffectiveColor()
        {
            GameplaySkinStyle style = GameplaySkinManager.Style;
            var element = style.LayoutElement(part == "combo" ? "comboHud" : "scoreHud");
            return element != null && ColorUtility.TryParseHtmlString(element.TextColor, out Color color)
                ? color : part == "combo" ? style.ComboColor : style.ScoreColor;
        }
    }
}
