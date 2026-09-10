using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public sealed partial class AppUiShell
    {
        private static readonly Color ScoringModAccent = new Color(.30f, .86f, .59f);
        private static readonly Color PracticeModAccent = new Color(1f, .68f, .34f);

        private RectTransform BuildModDialog(string name, out RectTransform layer, out Text title,
            string heading, UnityAction close)
        {
            layer = Panel(name, canvas.transform, new Color(0, 0, 0, .78f), Vector2.zero, Vector2.one);
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            RectTransform body = Panel(name + " body", layer, new Color(.075f, .08f, .09f, .99f),
                new Vector2(.08f, .055f), new Vector2(.92f, .945f));
            title = Label(heading, body, 28, TextAnchor.MiddleLeft,
                new Vector2(.05f, .89f), new Vector2(.85f, .98f), Color.white);
            FitModText(title, 28);
            ButtonText("\u00d7", body, new Vector2(.89f, .90f), new Vector2(.95f, .98f), surface, close);
            ModSection(body, "\u8ba1\u5165\u6210\u7ee9 MOD", .77f, ScoringModAccent);
            ModSection(body, "\u4e0d\u8ba1\u5165\u6210\u7ee9 MOD", .60f, PracticeModAccent);
            ModSection(body, "\u6b4c\u66f2\u901f\u5ea6", .21f, PracticeModAccent);
            return body;
        }

        private void ModSection(RectTransform body, string name, float bottom, Color accent)
        {
            Text label = Label(name, body, 20, TextAnchor.MiddleLeft,
                new Vector2(.05f, bottom), new Vector2(.23f, bottom + .09f), accent);
            FitModText(label, 20);
            RectTransform divider = Panel("Section divider", body, new Color(1, 1, 1, .12f),
                new Vector2(.05f, bottom + .115f), new Vector2(.95f, bottom + .115f));
            divider.offsetMin = Vector2.zero;
            divider.offsetMax = new Vector2(0, 1);
            divider.GetComponent<Image>().raycastTarget = false;
        }

        private Button ModOption(RectTransform parent, int column, float bottom, bool scoring, UnityAction action)
        {
            bool speed = bottom < .3f;
            float step = speed ? .235f : .355f;
            float left = .25f + column * step;
            Button button = ButtonText("", parent, new Vector2(left, bottom),
                new Vector2(left + step - .01f, bottom + .09f), surface, action, true);
            button.name = scoring ? "Scoring MOD" : "Practice MOD";
            Text label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(16, 2);
            label.rectTransform.offsetMax = new Vector2(-42, -2);
            label.raycastTarget = false;
            FitModText(label, 22);
            RectTransform check = Panel("Selection", button.transform, Color.white, new Vector2(1, .5f), new Vector2(1, .5f));
            check.pivot = new Vector2(1, .5f);
            check.sizeDelta = new Vector2(20, 20);
            check.anchoredPosition = new Vector2(-14, 0);
            check.GetComponent<Image>().raycastTarget = false;
            RectTransform center = Panel("Center", check, surface, Vector2.zero, Vector2.one);
            center.offsetMin = new Vector2(3, 3);
            center.offsetMax = new Vector2(-3, -3);
            center.GetComponent<Image>().raycastTarget = false;
            return button;
        }

        private static void FitModText(Text label, int maximum)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = maximum;
        }

        private void UpdateModOption(Button button, string name, bool selected, bool disabled)
        {
            Color accent = button.name == "Scoring MOD" ? ScoringModAccent : PracticeModAccent;
            Color background = disabled ? new Color(.12f, .125f, .135f) : selected
                ? Color.Lerp(new Color(.12f, .13f, .14f), accent, .23f) : new Color(.15f, .16f, .175f);
            button.interactable = !disabled;
            button.GetComponent<Image>().color = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            Text label = button.GetComponentInChildren<Text>();
            label.text = UiLocalization.Translate(name);
            label.color = disabled ? new Color(.46f, .48f, .51f) : selected ? accent : Color.white;
            Transform check = button.transform.Find("Selection");
            if (check != null) {
                check.GetComponent<Image>().color = selected && !disabled ? accent : new Color(.36f, .38f, .41f);
                check.Find("Center").GetComponent<Image>().color = selected && !disabled ? accent : background;
            }
        }

        private void ResetSinglePlayerMods()
        {
            mods.AutoPlay = mods.AirAreaAuto = mods.DirectionalAirAuto = mods.AirLongNoMotion = false;
            mods.MirrorChart = mods.MineImmunity = mods.NonMissJusticeCritical = false;
            mods.MusicRate = 1f;
            RefreshMods();
        }
    }
}
