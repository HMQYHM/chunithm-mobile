using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public sealed partial class AppUiShell
    {
        private void SkinSection(string title, Color accent)
        {
            settingsCursor += 16;
            RectTransform row = SettingsRect(title, 54, Color.clear);
            Text label = Label(title, row, 22, TextAnchor.MiddleLeft,
                new Vector2(.02f, 0), new Vector2(.95f, 1), accent);
            FitModText(label, 22);
        }

        private RectTransform SkinRow(string title)
        {
            RectTransform row = SettingsRect(title, 86, surfaceRaised);
            row.GetComponent<Image>().sprite = null;
            Text label = Label(title, row, 23, TextAnchor.MiddleLeft,
                new Vector2(.03f, .12f), new Vector2(.43f, .88f), Color.white);
            FitModText(label, 23);
            return row;
        }

        private void SkinAction(string title, string value, UnityEngine.Events.UnityAction action)
        {
            RectTransform row = SkinRow(title);
            Text detail = Label(value, row, 20, TextAnchor.MiddleRight,
                new Vector2(.46f, .12f), new Vector2(.96f, .88f), action == null ? muted : cyan);
            FitModText(detail, 20);
            if (action != null) {
                Button button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row.GetComponent<Image>();
                button.onClick.AddListener(action);
                row.gameObject.AddComponent<UiPressAnimation>();
            }
        }

        private void SkinToggle(string title, string option, bool selected, Action<bool> changed)
        {
            RectTransform row = SkinRow(title);
            Text value = Label(option, row, 19, TextAnchor.MiddleRight,
                new Vector2(.45f, .12f), new Vector2(.86f, .88f), muted);
            FitModText(value, 19);
            if (string.IsNullOrEmpty(option)) {
                Text titleLabel = row.GetComponentInChildren<Text>();
                titleLabel.rectTransform.anchorMax = new Vector2(.85f, .88f);
            }
            RectTransform box = Panel("Checkbox", row, surface, new Vector2(.93f, .5f), new Vector2(.93f, .5f));
            box.sizeDelta = new Vector2(30, 30);
            RectTransform mark = Panel("Check", box, cyan, Vector2.zero, Vector2.one);
            mark.offsetMin = new Vector2(6, 6); mark.offsetMax = new Vector2(-6, -6);
            Toggle toggle = row.gameObject.AddComponent<Toggle>();
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = mark.GetComponent<Image>();
            toggle.SetIsOnWithoutNotify(selected);
            toggle.onValueChanged.AddListener(value => {
                if (selected == value) return;
                selected = value;
                changed(value);
            });
        }

        private Dropdown SkinDropdown(string title, string[] options, int selected, Action<int> changed, bool translate = true)
        {
            RectTransform row = SkinRow(title);
            RectTransform control = Panel("Choice", row, surface, new Vector2(.45f, .16f), new Vector2(.97f, .84f));
            Dropdown dropdown = control.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = control.GetComponent<Image>();
            Text caption = Label("", control, 20, TextAnchor.MiddleLeft,
                new Vector2(.06f, .06f), new Vector2(.85f, .94f), Color.white);
            FitModText(caption, 20);
            Label("\u25be", control, 22, TextAnchor.MiddleCenter, new Vector2(.87f, .08f), new Vector2(.98f, .92f), cyan);
            dropdown.captionText = caption;
            RectTransform template = Panel("Template", control, surfaceRaised, new Vector2(0, 0), new Vector2(1, 0));
            template.pivot = new Vector2(.5f, 1);
            template.sizeDelta = new Vector2(0, Mathf.Min(300, Math.Max(1, options.Length) * 64 + 8));
            template.anchoredPosition = new Vector2(0, -4);
            ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform viewport = RectObject("Viewport", template, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = RectObject("Content", viewport, new Vector2(0, 1), Vector2.one);
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, 64);
            scroll.viewport = viewport; scroll.content = content;
            RectTransform item = Panel("Item", content, surfaceRaised, new Vector2(0, .5f), new Vector2(1, .5f));
            item.sizeDelta = new Vector2(0, 64);
            Toggle itemToggle = item.gameObject.AddComponent<Toggle>();
            itemToggle.toggleTransition = Toggle.ToggleTransition.None;
            itemToggle.targetGraphic = item.GetComponent<Image>();
            RectTransform selectedMark = Panel("Selected", item, cyan, new Vector2(0, .12f), new Vector2(.012f, .88f));
            selectedMark.offsetMin = selectedMark.offsetMax = Vector2.zero;
            itemToggle.graphic = selectedMark.GetComponent<Image>();
            Text itemText = Label("", item, 20, TextAnchor.MiddleLeft,
                new Vector2(.06f, .04f), new Vector2(.95f, .96f), Color.white);
            FitModText(itemText, 20);
            dropdown.template = template;
            dropdown.itemText = itemText;
            dropdown.AddOptions(options.Select(value => translate ? UiLocalization.Translate(value) : value).ToList());
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, Math.Max(0, options.Length - 1)));
            dropdown.RefreshShownValue();
            template.gameObject.SetActive(false);
            dropdown.onValueChanged.AddListener(index => changed(index));
            return dropdown;
        }

        private void ChangeUiPalette(bool enabled)
        {
            if (transitioning || importRunning || gameplaySkinImportPending || settings.HighContrastUi == enabled) return;
            StartCoroutine(AnimateUiPalette(enabled));
        }

        private void SettingsAudioRow(string title, bool enabled, int volume,
            Action<bool> enableChanged, Action<int> volumeChanged, UnityEngine.Events.UnityAction previewAction)
        {
            Slider slider = SettingsSlider(title, "%", 0, 100, volume,
                value => volumeChanged(Mathf.RoundToInt(value)));
            RectTransform row = (RectTransform)slider.transform.parent;
            Button play = ButtonText("播放", row, new Vector2(.04f, .08f), new Vector2(.22f, .42f), surface, previewAction);
            slider.interactable = enabled;
            play.interactable = enabled;
            RectTransform box = Panel("Audio enabled", row, surface, new Vector2(.59f, .7f), new Vector2(.59f, .7f));
            box.sizeDelta = new Vector2(36, 36);
            Toggle toggle = box.gameObject.AddComponent<Toggle>();
            RectTransform mark = Panel("Check", box, cyan, Vector2.zero, Vector2.one);
            mark.offsetMin = new Vector2(7, 7); mark.offsetMax = new Vector2(-7, -7);
            mark.GetComponent<Image>().raycastTarget = false;
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = mark.GetComponent<Image>();
            toggle.SetIsOnWithoutNotify(enabled);
            toggle.onValueChanged.AddListener(value => {
                enableChanged(value);
                slider.interactable = value;
                play.interactable = value;
                settings.Save();
                ApplyRuntimeSettings();
            });
        }

    }
}
