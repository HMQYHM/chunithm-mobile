using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // Shared two-pane MOD picker used by solo play and multiplayer room settings.
    // The left pane selects an item; the right pane is the only place that toggles it.
    public sealed partial class AppUiShell
    {
        private static readonly Color ScoringModAccent = new Color(.30f, .86f, .59f);
        private static readonly Color PracticeModAccent = new Color(1f, .68f, .34f);
        private const float ModScoringSectionTop = 0f;
        private const float ModPracticeSectionTop = 214f;
        private const float ModSpeedSectionTop = 704f;
        private const float ModListContentHeight = 940f;

        private sealed class ModOptionView
        {
            public Button Button;
            public UnityAction Toggle;
            public bool Scoring;
            public bool Selected;
            public bool Disabled;
            public bool PreviousEnabled;
            public string Name;
            public string Section;
        }

        private sealed class ModDialogView
        {
            public readonly Dictionary<Button, ModOptionView> Options = new Dictionary<Button, ModOptionView>();
            public readonly Dictionary<string, float> NextRow = new Dictionary<string, float>();
            public readonly Dictionary<string, float> SectionTop = new Dictionary<string, float>();
            public readonly Dictionary<string, RectTransform> SectionLabels = new Dictionary<string, RectTransform>();
            public readonly Dictionary<string, RectTransform> SectionLines = new Dictionary<string, RectTransform>();
            public Text DetailGroup;
            public Text DetailTitle;
            public Text DetailDescription;
            public Text DetailStatus;
            public Text DetailEffects;
            public Button DetailToggle;
            public ModOptionView Current;
        }

        private readonly Dictionary<RectTransform, ModDialogView> modDialogs = new Dictionary<RectTransform, ModDialogView>();

        private RectTransform BuildModDialog(string name, out RectTransform layer, out Text title,
            string heading, UnityAction close)
        {
            // MOD dialogs belong to the application shell, not the gameplay
            // canvas.  Keeping them inside the safe-area root prevents the
            // close button, section headers, and bottom action row from being
            // clipped by an iPhone notch, rounded corner, or iPad inset.
            layer = Panel(name, UiRootTransform, new Color(0, 0, 0, .78f), Vector2.zero, Vector2.one);
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            RectTransform body = Panel(name + " body", layer, surfaceRaised,
                new Vector2(.06f, .055f), new Vector2(.94f, .945f));
            title = Label(heading, body, 28, TextAnchor.MiddleLeft,
                new Vector2(.045f, .89f), new Vector2(.84f, .98f), ModPrimaryTextColor);
            FitModText(title, 28);
            ButtonText("×", body, new Vector2(.90f, .90f), new Vector2(.955f, .98f), surface, close);

            // Single-column rows keep state indicators readable on short landscape screens.
            RectTransform listPanel = Panel("MOD list panel", body, surface,
                new Vector2(.035f, .15f), new Vector2(.43f, .865f));
            RectTransform viewport = RectObject("MOD list viewport", listPanel, new Vector2(.035f, .025f), new Vector2(.965f, .975f));
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = RectObject("MOD list content", viewport, new Vector2(0, 1), Vector2.one);
            content.pivot = new Vector2(.5f, 1f);
            // The solo picker currently has 2 scoring, 7 practice and 3 speed
            // rows. Keep all section coordinates in one scrollable canvas so
            // adding a row cannot place it underneath the following heading.
            content.sizeDelta = new Vector2(0, ModListContentHeight);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42;

            RectTransform details = Panel("MOD details", body, surface,
                new Vector2(.455f, .15f), new Vector2(.965f, .865f));
            var view = new ModDialogView();
            view.NextRow["Scoring"] = ModScoringSectionTop + 18f;
            view.NextRow["Practice"] = ModPracticeSectionTop + 18f;
            view.NextRow["Speed"] = ModSpeedSectionTop + 18f;
            view.SectionTop["Scoring"] = ModScoringSectionTop;
            view.SectionTop["Practice"] = ModPracticeSectionTop;
            view.SectionTop["Speed"] = ModSpeedSectionTop;
            modDialogs[body] = view;
            CreateModSection(content, "Scoring", "计入成绩 MOD", ModScoringSectionTop, ScoringModAccent);
            CreateModSection(content, "Practice", "不计入成绩 MOD", ModPracticeSectionTop, PracticeModAccent);
            CreateModSection(content, "Speed", "歌曲速度", ModSpeedSectionTop, PracticeModAccent);
            BuildModDetails(view, details);
            return body;
        }

        private void CreateModSection(RectTransform content, string key, string name, float top, Color accent)
        {
            Color color = HighContrastUi ? (key == "Scoring" ? UiThemeContrast.Success : UiThemeContrast.Magenta) : accent;
            Text label = Label(name, content, 18, TextAnchor.MiddleLeft,
                new Vector2(.035f, 1f), new Vector2(.965f, 1f), color, true);
            label.rectTransform.offsetMin = new Vector2(0, -top - 31);
            label.rectTransform.offsetMax = new Vector2(0, -top);
            FitModText(label, 18);
            RectTransform line = Panel("MOD section line", content, color,
                new Vector2(.035f, 1f), new Vector2(.965f, 1f), true);
            line.offsetMin = new Vector2(0, -top - 35);
            line.offsetMax = new Vector2(0, -top - 33);
            line.GetComponent<Image>().raycastTarget = false;
            RectTransform body = content.parent.parent.parent as RectTransform;
            if (body != null && modDialogs.TryGetValue(body, out ModDialogView view))
            {
                view.SectionLabels[key] = label.rectTransform;
                view.SectionLines[key] = line;
            }
        }

        /// <summary>
        /// Reflows headings and rows from their actual counts. The list uses a
        /// top-anchored coordinate system, so moving a section updates both its
        /// heading and every option in that section by the same amount.
        /// </summary>
        private void RelayoutModSections(RectTransform body)
        {
            if (body == null || !modDialogs.TryGetValue(body, out ModDialogView view)) return;

            float nextTop = 0f;
            string[] sections = { "Scoring", "Practice", "Speed" };
            foreach (string section in sections)
            {
                float oldTop = view.SectionTop[section];
                float newTop = section == "Scoring" ? 0f : nextTop;
                float delta = newTop - oldTop;
                if (Mathf.Abs(delta) > .01f)
                {
                    if (view.SectionLabels.TryGetValue(section, out RectTransform label)) MoveModRect(label, delta);
                    if (view.SectionLines.TryGetValue(section, out RectTransform line)) MoveModRect(line, delta);
                    foreach (ModOptionView option in view.Options.Values)
                        if (option.Section == section) MoveModRect((RectTransform)option.Button.transform, delta);
                }

                view.SectionTop[section] = newTop;
                int count = 0;
                foreach (ModOptionView option in view.Options.Values)
                    if (option.Section == section) count++;
                view.NextRow[section] = newTop + 18f + count * 61f;
                nextTop = newTop + 18f + count * 61f + 24f;
            }

            RectTransform content = (RectTransform)body.Find("MOD list panel/MOD list viewport/MOD list content");
            if (content != null) content.sizeDelta = new Vector2(0, Mathf.Max(ModListContentHeight, nextTop + 110f));
        }

        private static void MoveModRect(RectTransform rect, float delta)
        {
            if (rect == null) return;
            rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMin.y - delta);
            rect.offsetMax = new Vector2(rect.offsetMax.x, rect.offsetMax.y - delta);
        }

        private void BuildModDetails(ModDialogView view, RectTransform details)
        {
            view.DetailGroup = Label("选择一个 MOD", details, 16, TextAnchor.MiddleLeft,
                new Vector2(.07f, .84f), new Vector2(.93f, .94f), ModMutedTextColor, true);
            view.DetailTitle = Label("", details, 26, TextAnchor.UpperLeft,
                new Vector2(.07f, .69f), new Vector2(.93f, .84f), ModPrimaryTextColor, true);
            FitModText(view.DetailTitle, 26);
            view.DetailDescription = Label("从左侧列表选择 MOD，右侧会说明它的游玩效果与成绩状态。", details, 17,
                TextAnchor.UpperLeft, new Vector2(.07f, .46f), new Vector2(.93f, .68f), ModMutedTextColor, true);
            view.DetailEffects = Label("", details, 16, TextAnchor.UpperLeft,
                new Vector2(.07f, .21f), new Vector2(.93f, .43f), ModMutedTextColor, true);
            view.DetailStatus = Label("", details, 16, TextAnchor.MiddleLeft,
                new Vector2(.07f, .12f), new Vector2(.93f, .20f), ModMutedTextColor, true);
            view.DetailToggle = ButtonText("启用", details, new Vector2(.07f, .025f), new Vector2(.93f, .105f), cyan,
                () => ToggleCurrentMod(view));
        }

        private Button ModOption(RectTransform body, int column, int row, bool scoring, UnityAction action, bool speed = false)
        {
            if (!modDialogs.TryGetValue(body, out ModDialogView view)) return null;
            string section = speed ? "Speed" : scoring ? "Scoring" : "Practice";
            float top = view.NextRow[section];
            view.NextRow[section] = top + 61;
            RectTransform content = (RectTransform)body.Find("MOD list panel/MOD list viewport/MOD list content");
            Button button = ButtonText("", content, new Vector2(.035f, 1f), new Vector2(.965f, 1f), surfaceRaised, null, true);
            RectTransform rect = (RectTransform)button.transform;
            rect.offsetMin = new Vector2(0, -top - 92);
            rect.offsetMax = new Vector2(0, -top - 38);
            Text label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(16, 2);
            label.rectTransform.offsetMax = new Vector2(-50, -2);
            label.raycastTarget = false;
            FitModText(label, 20);
            RectTransform dot = Panel("Status dot", button.transform, new Color(.42f, .45f, .48f), Vector2.one * .5f, Vector2.one * .5f);
            dot.pivot = new Vector2(1, .5f);
            dot.sizeDelta = new Vector2(15, 15);
            dot.anchoredPosition = new Vector2(-17, 0);
            dot.GetComponent<Image>().raycastTarget = false;
            ModOptionView option = new ModOptionView {
                Button = button, Toggle = action, Scoring = scoring, Section = section
            };
            view.Options[button] = option;
            button.onClick.AddListener(() => SelectModOption(view, option));
            if (view.Current == null) view.Current = option;
            return button;
        }

        private void SelectModOption(ModDialogView view, ModOptionView option)
        {
            if (option == null) return;
            view.Current = option;
            RefreshModDetails(view);
            RefreshModOptionSelection(view);
        }

        private void ToggleCurrentMod(ModDialogView view)
        {
            ModOptionView option = view.Current;
            if (option == null || option.Disabled || option.Toggle == null) return;
            option.Toggle.Invoke();
        }

        private static void FitModText(Text label, int maximum)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 13;
            label.resizeTextMaxSize = maximum;
        }

        private void UpdateModOption(Button button, string name, bool selected, bool disabled)
        {
            if (button == null) return;
            ModOptionView option = null;
            ModDialogView dialog = null;
            foreach (KeyValuePair<RectTransform, ModDialogView> pair in modDialogs)
                if (pair.Value.Options.TryGetValue(button, out option)) { dialog = pair.Value; break; }
            if (option == null) return;
            option.Name = name;
            option.Selected = selected;
            option.Disabled = disabled;
            Color accent = ModAccent(option.Scoring);
            Color idle = HighContrastUi ? new Color(.92f, .94f, .96f) : new Color(.15f, .16f, .175f);
            Color background = disabled ? (HighContrastUi ? new Color(.84f, .86f, .88f) : new Color(.10f, .12f, .14f))
                : dialog.Current == option ? Color.Lerp(idle, accent, .18f) : idle;
            button.interactable = !disabled;
            button.GetComponent<Image>().color = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(.85f, .85f, .85f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            Text label = button.GetComponentInChildren<Text>();
            label.text = UiLocalization.Translate(name);
            label.color = disabled ? ModDisabledTextColor : ModPrimaryTextColor;
            Transform dot = button.transform.Find("Status dot");
            if (dot != null) {
                // State is deliberately independent from score eligibility:
                // off is always grey and on is always green in both palettes.
                dot.GetComponent<Image>().color = selected && !disabled ? ModEnabledDotColor : ModDisabledDotColor;
                if (option.PreviousEnabled != selected && button.gameObject.activeInHierarchy)
                    StartCoroutine(AnimateModStatusDot(dot));
            }
            option.PreviousEnabled = selected;
            if (dialog.Current == option) RefreshModDetails(dialog);
            RefreshModOptionSelection(dialog);
        }

        private Color ModAccent(bool scoring)
        {
            return HighContrastUi ? (scoring ? UiThemeContrast.Success : UiThemeContrast.Magenta)
                : scoring ? ScoringModAccent : PracticeModAccent;
        }

        private Color ModPrimaryTextColor => HighContrastUi ? UiThemeContrast.Navy : Color.white;
        private Color ModMutedTextColor => HighContrastUi ? UiThemeContrast.MutedOnLight : muted;
        private Color ModDisabledTextColor => HighContrastUi ? new Color(.34f, .38f, .45f) : new Color(.46f, .48f, .51f);
        private Color ModEnabledDotColor => HighContrastUi ? new Color(.05f, .62f, .31f) : ScoringModAccent;
        private Color ModDisabledDotColor => HighContrastUi ? new Color(.39f, .43f, .49f) : new Color(.40f, .43f, .46f);

        private void RefreshModOptionSelection(ModDialogView dialog)
        {
            if (dialog == null) return;
            foreach (ModOptionView option in dialog.Options.Values) {
                if (option.Button == null) continue;
                Color idle = HighContrastUi ? new Color(.92f, .94f, .96f) : new Color(.15f, .16f, .175f);
                Color background = option.Disabled ? (HighContrastUi ? new Color(.84f, .86f, .88f) : new Color(.10f, .12f, .14f))
                    : dialog.Current == option ? Color.Lerp(idle, ModAccent(option.Scoring), .18f) : idle;
                option.Button.GetComponent<Image>().color = background;
            }
        }

        private IEnumerator AnimateModStatusDot(Transform dot)
        {
            Vector3 baseScale = Vector3.one;
            for (float t = 0; t < .14f; t += Time.unscaledDeltaTime) {
                dot.localScale = baseScale * Mathf.Lerp(.58f, 1.18f, t / .14f);
                yield return null;
            }
            dot.localScale = baseScale;
        }

        private void RefreshModDetails(ModDialogView view)
        {
            ModOptionView option = view.Current;
            if (option == null || string.IsNullOrEmpty(option.Name)) return;
            bool enabled = option.Selected && !option.Disabled;
            Color accent = ModAccent(option.Scoring);
            view.DetailGroup.text = UiLocalization.Translate(option.Scoring ? "计入成绩 MOD" : "不计入成绩 MOD");
            view.DetailGroup.color = accent;
            view.DetailTitle.text = UiLocalization.Translate(option.Name);
            view.DetailTitle.color = ModPrimaryTextColor;
            view.DetailDescription.text = UiLocalization.Translate(ModDescription(option.Name));
            view.DetailDescription.color = ModMutedTextColor;
            view.DetailEffects.text = UiLocalization.Translate(ModEffects(option.Name));
            view.DetailEffects.color = ModMutedTextColor;
            view.DetailStatus.text = UiLocalization.Translate(option.Disabled ? "当前模式不可用" : option.Scoring ? "开启后成绩仍会保存" : "开启后本局不记录成绩");
            view.DetailStatus.color = option.Disabled ? ModDisabledTextColor : enabled ? accent : ModMutedTextColor;
            Text buttonLabel = view.DetailToggle.GetComponentInChildren<Text>();
            buttonLabel.text = UiLocalization.Translate(option.Disabled ? "当前模式不可用" : enabled ? "关闭" : "启用");
            view.DetailToggle.interactable = !option.Disabled;
            view.DetailToggle.GetComponent<Image>().color = option.Disabled ? new Color(.35f, .37f, .40f) : enabled ? accent : cyan;
        }

        private static string ModDescription(string name)
        {
            switch (name) {
                case "全屏触控板": return "保留当前横向宽度，将地面触控范围扩展至全屏，并自动协助 AIR 判定。";
                case "AIR 区域 AUTO": return "AIR 区域内的音符由系统自动判定。";
                case "上下 AIR AUTO": return "自动处理上下 AIR 的方向判定。";
                case "全谱面 AUTO": return "自动判定整张谱面，仅用于观看与练习。";
                case "air长条简单模式": return "AIR 长条保持在 AIR 区即可完成，无需上下移动。";
                case "简单判定": return "除了 MISS 外的判定统一视为 JUSTICE CRITICAL。";
                case "谱面镜像": return "左右镜像谱面和对应的输入位置。";
                case "Flick 可点击判定": return "Flick 可通过点击直接判定。";
                case "免疫地雷音符": return "触碰地雷不会造成 MISS。";
                default: return "调整歌曲播放速度，适合个人练习使用。";
            }
        }

        private static string ModEffects(string name)
        {
            switch (name) {
                case "全屏触控板": return "- 全屏地面输入\n- 强制开启 AIR 区域 AUTO\n- 强制开启上下 AIR AUTO\n- 休闲联机可用；普通联机不可用";
                case "谱面镜像": return "- 可保存成绩\n- 联机普通模式可用";
                case "air长条简单模式": return "- 可保存成绩\n- 不影响其他 AIR 音符";
                default: return name.EndsWith("x", StringComparison.Ordinal) ? "- 改变音乐与谱面播放速度\n- 可再次点击恢复 1.0x" : "- 可再次点击关闭\n- 当前局立即生效";
            }
        }

        private void ResetSinglePlayerMods()
        {
            mods.AutoPlay = mods.AirAreaAuto = mods.DirectionalAirAuto = mods.AirLongNoMotion = false;
            mods.MirrorChart = mods.MineImmunity = mods.NonMissJusticeCritical = false;
            mods.FlickAllTap = mods.FullscreenTouchpad = false;
            mods.FullscreenTouchpadRestoreAirAreaAuto = mods.FullscreenTouchpadRestoreDirectionalAirAuto = false;
            mods.MusicRate = 1f;
            RefreshMods();
        }
    }
}
