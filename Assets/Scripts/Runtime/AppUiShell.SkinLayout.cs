using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    public partial class AppUiShell
    {
        // Keep text-bearing gameplay elements above Unity's sub-pixel culling
        // threshold. A 5% parent scale makes Text meshes lose strokes or vanish
        // even though the layout rectangle is still present.
        private const float MinimumReadableLayoutScale = .25f;
        // Keep this list in sync with GameplaySkinManager's allow-list. The
        // editor uses it as the complete set of selectable runtime elements,
        // even when a skin omits gameplay-ui.json or only declares a subset.
        private static readonly string[] SkinEditorElementIds = {
            "songInfo", "songCover", "scoreHud", "comboHud", "judgement", "judgementImage",
            "pauseButton", "pausePanel", "progressBar", "fps", "countdown", "touchpad",
            "airZone", "airBoundaryBottom", "airBoundaryTop", "airGuide", "trackMask", "touchFeedback"
        };

        private static readonly Dictionary<string, string> SkinEditorElementLabels =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "songInfo", "歌曲信息" }, { "songCover", "歌曲封面" }, { "scoreHud", "分数详情" },
                { "comboHud", "COMBO" }, { "judgement", "判定文字" }, { "judgementImage", "判定图片" },
                { "pauseButton", "暂停按钮" }, { "pausePanel", "暂停面板" }, { "progressBar", "进度条" },
                { "fps", "FPS" }, { "countdown", "倒计时" }, { "touchpad", "触控板" },
                { "airZone", "AIR 区域" }, { "airBoundaryBottom", "AIR 下边界" },
                { "airBoundaryTop", "AIR 上边界" }, { "airGuide", "AIR 引导线" },
                { "trackMask", "轨道遮罩" }, { "touchFeedback", "触控反馈" }
            };

        private string SelectedLayoutElementId => string.IsNullOrEmpty(selectedLayoutElementId)
            ? SkinEditorElementIds[0] : selectedLayoutElementId;

        private static bool IsLegacyLayoutHandle(string id)
        {
            return id == "pauseButton" || id == "scoreHud" || id == "comboHud" ||
                id == "judgement" || id == "songInfo";
        }

        private void BuildLayoutElementSelector(Transform parent)
        {
            layoutEditorPreviousElementButton = ButtonText("‹", parent,
                new Vector2(.07f, .205f), new Vector2(.16f, .275f), surfaceRaised,
                () => SelectLayoutElement(-1));
            layoutEditorSelectedElementLabel = Label("编辑元素：歌曲信息", parent, 16,
                TextAnchor.MiddleCenter, new Vector2(.18f, .205f), new Vector2(.82f, .275f),
                Color.white);
            layoutEditorNextElementButton = ButtonText("›", parent,
                new Vector2(.84f, .205f), new Vector2(.93f, .275f), surfaceRaised,
                () => SelectLayoutElement(1));
        }

        private void SelectLayoutElement(int delta)
        {
            int index = Array.IndexOf(SkinEditorElementIds, SelectedLayoutElementId);
            if (index < 0) index = 0;
            index = (index + delta + SkinEditorElementIds.Length) % SkinEditorElementIds.Length;
            selectedLayoutElementId = SkinEditorElementIds[index];
            RefreshLayoutEditor();
        }

        private void RefreshSelectedLayoutElement()
        {
            if (LayoutTarget(SelectedLayoutElementId) == null)
            {
                selectedLayoutElementId = SkinEditorElementIds.FirstOrDefault(id => LayoutTarget(id) != null) ?? SkinEditorElementIds[0];
            }
            string id = SelectedLayoutElementId;
            // Explicitly selecting the cover is the opt-in point for an
            // independent rectangle. Automatic fallback seeding must keep it
            // attached to the song information parent.
            if (id == "songCover")
            {
                GameplayLayoutOverride cover = EnsureDraftLayoutOverride(id);
                if (cover != null) cover.Independent = true;
            }
            if (layoutEditorSelectedElementLabel != null)
            {
                string displayLabel = SkinEditorElementLabels.TryGetValue(id, out string selectedLabel) ? selectedLabel : id;
                if (id == "songCover" &&
                    LayoutOverride(draftSettings ?? settings, id)?.Independent != true)
                    displayLabel += "（随歌曲信息）";
                layoutEditorSelectedElementLabel.text = "编辑元素：" +
                    displayLabel;
            }
            bool generic = !IsLegacyLayoutHandle(id) && IsSkinLayoutEditable(id);
            if (editorSelectedElementHandle != null)
            {
                editorSelectedElementHandle.gameObject.SetActive(generic);
                if (generic && EnsureDraftLayoutOverride(id) is GameplayLayoutOverride item)
                {
                    editorSelectedElementHandle.anchorMin = new Vector2(item.X, item.Y);
                    editorSelectedElementHandle.anchorMax = new Vector2(item.X + item.Width, item.Y + item.Height);
                    // This handle is sized by its normalized anchors. Clear
                    // the legacy pixel size so it cannot cover neighbouring
                    // elements when COMBO or another item is reduced.
                    editorSelectedElementHandle.sizeDelta = Vector2.zero;
                    editorSelectedElementHandle.offsetMin = editorSelectedElementHandle.offsetMax = Vector2.zero;
                    UpdateEditorHandleLabel(editorSelectedElementHandle,
                        (SkinEditorElementLabels.TryGetValue(id, out string label) ? label : id) +
                        $"  {item.X:P0} / {item.Y:P0}");
                }
            }
        }

        /// <summary>
        /// Applies only declarative overrides from the active skin. Buttons keep
        /// their game-owned callbacks; a skin can change their appearance and
        /// bounds but cannot replace their behavior.
        /// </summary>
        private void ApplyGameplaySkinLayout(GameplaySkinStyle style)
        {
            if (style == null) return;
            if (!pages.TryGetValue(AppPage.Gameplay, out RectTransform canvas)) return;
            ClearGameplaySkinImageOverrides();
            foreach (string id in SkinEditorElementIds)
            {
                RectTransform target = LayoutTarget(id);
                if (target != null) target.localScale = Vector3.one;
            }
            // Parent geometry must settle before child coordinates are converted.
            // Export order is a layer list, not a transform dependency order.
            foreach (GameplaySkinElementDefinition element in style.LayoutElements)
            {
                if (element == null) continue;
                RectTransform target = LayoutTarget(element.Id);
                if (target == null) continue;
                if (element.Id == "songCover" || element.Id == "trackMask" || element.Id == "judgementImage") continue;
                SetCanvasSkinRect(target, canvas, element);
            }
            foreach (GameplaySkinElementDefinition element in style.LayoutElements)
            {
                if (element == null) continue;
                RectTransform target = LayoutTarget(element.Id);
                if (target == null) continue;
                // The cover is a child of songInfo. Apply its visual skin
                // here, but keep its rectangle under the parent unless the
                // user explicitly enabled an independent editor override.
                if (element.Id == "songCover" || element.Id == "trackMask")
                {
                    ApplySkinVisual(target.gameObject, element, IsDynamicLayoutElement(element.Id));
                    continue;
                }
                if (element.Id == "judgementImage") {
                    // FitInParent otherwise overwrites the authored rectangle.
                    var fitter = target.GetComponent<AspectRatioFitter>();
                    if (fitter != null) fitter.enabled = false;
                    SetCanvasSkinRect(target, canvas, element);
                }
                ApplySkinVisual(target.gameObject, element, IsDynamicLayoutElement(element.Id));
            }
        }

        private void ClearGameplaySkinImageOverrides()
        {
            // Image.overrideSprite is the skin-owned layer. Clearing only this
            // layer restores the component's original sprite, while leaving
            // runtime-loaded song jackets and RawImage judgement textures intact.
            foreach (string id in new[] {
                "songInfo", "songCover", "scoreHud", "comboHud", "judgement",
                "judgementImage", "pauseButton", "pausePanel", "progressBar", "fps",
                "countdown", "touchpad", "airZone", "airBoundaryBottom", "airBoundaryTop",
                "airGuide", "trackMask", "touchFeedback"
            })
            {
                RectTransform target = LayoutTarget(id);
                if (target == null) continue;
                foreach (Image image in target.GetComponentsInChildren<Image>(true))
                    image.overrideSprite = null;
            }
        }

        private GameplayLayoutOverride LayoutOverride(AppSettings source, string id)
        {
            if (source?.GameplayLayoutOverrides == null || string.IsNullOrEmpty(id)) return null;
            string skinId = GameplaySkinManager.Active?.Id ?? GameplaySkinManager.BuiltinId;
            for (int i = source.GameplayLayoutOverrides.Count - 1; i >= 0; i--)
            {
                GameplayLayoutOverride item = source.GameplayLayoutOverrides[i];
                if (item != null && string.Equals(item.SkinId, skinId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ElementId, id, StringComparison.Ordinal)) return item;
            }
            return null;
        }

        private void ApplyGameplayLayoutOverrides(GameplaySkinStyle style, AppSettings source)
        {
            if (style == null || source?.GameplayLayoutOverrides == null) return;
            if (!pages.TryGetValue(AppPage.Gameplay, out RectTransform canvas)) return;
            string skinId = GameplaySkinManager.Active?.Id ?? GameplaySkinManager.BuiltinId;
            foreach (GameplayLayoutOverride item in source.GameplayLayoutOverrides)
            {
                if (item == null || !string.Equals(item.SkinId, skinId, StringComparison.OrdinalIgnoreCase)) continue;
                if (item.ElementId == "trackMask") continue;
                if (item.ElementId == "songCover" && !item.Independent) continue;
                RectTransform target = LayoutTarget(item.ElementId);
                if (target == null || item.Width <= 0f || item.Height <= 0f) continue;
                if (item.ReferenceWidth <= 0f || item.ReferenceHeight <= 0f)
                {
                    item.ReferenceWidth = item.Width;
                    item.ReferenceHeight = item.Height;
                }
                float scaleX = Mathf.Clamp(item.Width / Mathf.Max(.001f, item.ReferenceWidth), MinimumReadableLayoutScale, 8f);
                float scaleY = Mathf.Clamp(item.Height / Mathf.Max(.001f, item.ReferenceHeight), MinimumReadableLayoutScale, 8f);
                // Keep the target's reference rectangle while scaling its
                // contents. This prevents Text components and pixel offsets
                // from being clipped when the editor rectangle is reduced.
                float referenceX = item.X + (item.Width - item.ReferenceWidth) * .5f;
                float referenceY = item.Y + (item.Height - item.ReferenceHeight) * .5f;
                SetCanvasSkinRect(target, canvas, referenceX, referenceY,
                    item.ReferenceWidth, item.ReferenceHeight);
                target.localScale = new Vector3(scaleX, scaleY, 1f);
                GameplaySkinElementDefinition definition = style.LayoutElement(item.ElementId);
                if (definition != null)
                {
                    ApplySkinVisual(target.gameObject, definition, IsDynamicLayoutElement(item.ElementId));
                }
                // Panels such as pause/countdown/judgement are controlled by
                // gameplay state. A seeded fallback override must not make an
                // inactive runtime panel appear merely because it is editable.
                if (!IsDynamicLayoutElement(item.ElementId) || !item.Visible)
                    target.gameObject.SetActive(item.Visible);
            }
        }

        private static void SetCanvasSkinRect(RectTransform target, RectTransform canvas,
            float x, float y, float width, float height)
        {
            if (target == null || canvas == null || !(target.parent is RectTransform parent) ||
                parent.rect.width <= 0 || parent.rect.height <= 0) return;
            Rect bounds = canvas.rect;
            // Keep the rectangle's centre even when the reference rectangle
            // is larger than the edited rectangle and therefore extends past
            // the canvas edge. Clamping the reference rectangle to [0, 1]
            // moves its centre toward the opposite edge (the visible symptom
            // was text and icons drifting to the upper-right while shrinking).
            // Unity UI anchors may safely be outside the parent range; the
            // canvas/parent clipping still controls what is visible.
            Vector3 min = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(
                bounds.xMin + bounds.width * x, bounds.yMin + bounds.height * y)));
            Vector3 max = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(
                bounds.xMin + bounds.width * (x + width),
                bounds.yMin + bounds.height * (y + height))));
            target.anchorMin = new Vector2((min.x - parent.rect.xMin) / parent.rect.width, (min.y - parent.rect.yMin) / parent.rect.height);
            target.anchorMax = new Vector2((max.x - parent.rect.xMin) / parent.rect.width, (max.y - parent.rect.yMin) / parent.rect.height);
            target.offsetMin = target.offsetMax = Vector2.zero;
            target.pivot = new Vector2(.5f, .5f);
            target.localRotation = Quaternion.identity;
        }

        private bool IsSkinLayoutEditable(string id)
        {
            // A missing skin element means "use the game UI", not "lock the
            // element". The runtime target is the source of the fallback
            // rectangle and can therefore always be edited locally.
            if (string.IsNullOrEmpty(id) || LayoutTarget(id) == null) return false;
            return true;
        }

        private bool UseSkinEditorOverride(string id)
        {
            // Preserve an authored rectangle, or a previously saved local
            // override. A missing element is seeded from the current runtime
            // rectangle before this predicate becomes true.
            if (!IsSkinLayoutEditable(id)) return false;
            GameplaySkinElementDefinition element = GameplaySkinManager.Style?.LayoutElement(id);
            bool authored = element != null && element.X >= 0f && element.Y >= 0f &&
                element.Width > 0f && element.Height > 0f;
            return authored || LayoutOverride(draftSettings ?? settings, id) != null;
        }

        private bool TryGetCanvasRect(string id, out Rect rect)
        {
            rect = default;
            if (!pages.TryGetValue(AppPage.Gameplay, out RectTransform canvas)) return false;
            RectTransform target = LayoutTarget(id);
            if (target == null || canvas.rect.width <= 0f || canvas.rect.height <= 0f) return false;
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 min = canvas.InverseTransformPoint(corners[0]);
            Vector3 max = canvas.InverseTransformPoint(corners[2]);
            Rect bounds = canvas.rect;
            float x = Mathf.InverseLerp(bounds.xMin, bounds.xMax, min.x);
            float y = Mathf.InverseLerp(bounds.yMin, bounds.yMax, min.y);
            float right = Mathf.InverseLerp(bounds.xMin, bounds.xMax, max.x);
            float top = Mathf.InverseLerp(bounds.yMin, bounds.yMax, max.y);
            rect = new Rect(Mathf.Clamp01(x), Mathf.Clamp01(y),
                Mathf.Clamp(right - x, .001f, 1f), Mathf.Clamp(top - y, .001f, 1f));
            return true;
        }

        private string ActiveLayoutSkinId => GameplaySkinManager.Active?.Id ?? GameplaySkinManager.BuiltinId;

        private void EnsureAllDraftLayoutOverrides()
        {
            foreach (string id in SkinEditorElementIds) EnsureDraftLayoutOverride(id);
        }

        private GameplayLayoutOverride EnsureDraftLayoutOverride(string id)
        {
            if (draftSettings == null || !IsSkinLayoutEditable(id)) return null;
            if (draftSettings.GameplayLayoutOverrides == null)
                draftSettings.GameplayLayoutOverrides = new List<GameplayLayoutOverride>();
            GameplayLayoutOverride item = LayoutOverride(draftSettings, id);
            if (item != null)
            {
                if (item.ReferenceWidth <= 0f || item.ReferenceHeight <= 0f)
                {
                    item.ReferenceWidth = item.Width;
                    item.ReferenceHeight = item.Height;
                }
                return item;
            }
            GameplaySkinElementDefinition source = GameplaySkinManager.Style.LayoutElement(id);
            Rect fallback;
            if (source != null && source.X >= 0f && source.Y >= 0f && source.Width > 0f && source.Height > 0f)
                fallback = new Rect(source.X, source.Y, source.Width, source.Height);
            else if (!TryGetCanvasRect(id, out fallback)) return null;
            item = new GameplayLayoutOverride {
                SkinId = ActiveLayoutSkinId, ElementId = id,
                X = fallback.x, Y = fallback.y, Width = fallback.width, Height = fallback.height,
                ReferenceWidth = fallback.width, ReferenceHeight = fallback.height,
                Visible = source == null || source.Visible,
                // Merely seeding the editor with a fallback rectangle must
                // not detach the cover from songInfo. Selecting songCover in
                // the element picker is what opts into independent editing.
                Independent = false
            };
            draftSettings.GameplayLayoutOverrides.Add(item);
            return item;
        }

        private void EditSkinElementPosition(string id, Vector2 center, Action fallback)
        {
            GameplayLayoutOverride item = EnsureDraftLayoutOverride(id);
            if (item == null) { fallback?.Invoke(); return; }
            item.X = Mathf.Clamp01(center.x - item.Width * .5f);
            item.Y = Mathf.Clamp01(center.y - item.Height * .5f);
        }

        private void EditSkinElementSize(string id, float delta, Action fallback)
        {
            GameplayLayoutOverride item = EnsureDraftLayoutOverride(id);
            if (item == null) { fallback?.Invoke(); return; }
            float scale = 1f + delta / 220f;
            float minimumWidth = Mathf.Min(item.ReferenceWidth > 0f ? item.ReferenceWidth * MinimumReadableLayoutScale : .01f, 1f - item.X);
            float minimumHeight = Mathf.Min(item.ReferenceHeight > 0f ? item.ReferenceHeight * MinimumReadableLayoutScale : .01f, 1f - item.Y);
            float width = Mathf.Clamp(item.Width * scale, minimumWidth, 1f - item.X);
            float height = Mathf.Clamp(item.Height * scale, minimumHeight, 1f - item.Y);
            item.X = Mathf.Clamp01(item.X + (item.Width - width) * .5f);
            item.Y = Mathf.Clamp01(item.Y + (item.Height - height) * .5f);
            item.Width = width; item.Height = height;
        }

        private void EditEditorTouchpadWidth(float normalizedX)
        {
            if (draftSettings == null) return;
            int widthPercent = Mathf.Clamp(Mathf.RoundToInt((normalizedX - .5f) * 200f), 60, 100);
            draftSettings.TouchpadWidth = widthPercent;

            GameplayLayoutOverride item = EnsureDraftLayoutOverride("touchpad");
            if (item == null) return;
            float center = item.X + item.Width * .5f;
            item.Width = widthPercent / 100f;
            item.X = Mathf.Clamp(center - item.Width * .5f, 0f, 1f - item.Width);
        }

        private void ApplySkinEditorVisibility()
        {
            editorPauseHandle?.gameObject.SetActive(IsSkinLayoutEditable("pauseButton"));
            editorHudHandle?.gameObject.SetActive(IsSkinLayoutEditable("scoreHud"));
            editorComboHandle?.gameObject.SetActive(IsSkinLayoutEditable("comboHud"));
            editorJudgementHandle?.gameObject.SetActive(IsSkinLayoutEditable("judgement"));
            editorSongInfoHandle?.gameObject.SetActive(IsSkinLayoutEditable("songInfo"));
            editorTouchpadHandle?.gameObject.SetActive(IsSkinLayoutEditable("touchpad"));
            editorTouchpadWidthHandle?.gameObject.SetActive(IsSkinLayoutEditable("touchpad"));
            editorAirTopHandle?.gameObject.SetActive(IsSkinLayoutEditable("airZone"));
        }

        private void ApplyGameplayVisibilitySettings(AppSettings source)
        {
            if (source == null) return;
            // Skin/layout application runs after the game baseline and can
            // enable static panels again. User visibility switches remain
            // authoritative, including in the visual-editor preview.
            bool fullscreen = IsFullscreenTouchpadActive();
            if (!source.ShowTouchpadVisual || fullscreen) gameplayTouchpad?.gameObject.SetActive(false);
            if (!source.ShowInputBoundary || fullscreen) {
                gameplayAirBoundaryBottom?.gameObject.SetActive(false);
                gameplayAirBoundaryTop?.gameObject.SetActive(false);
            }
            if (!source.ShowGameplaySongInfo) gameplaySongInfoBlock?.gameObject.SetActive(false);
            if (!source.ShowGameplayFps) gameplayFpsLabel?.gameObject.SetActive(false);
            if (!source.ShowAirGuide || fullscreen) gameplayAirGuide?.gameObject.SetActive(false);
        }

        private RectTransform LayoutTarget(string id)
        {
            switch (id)
            {
                case "songInfo": return gameplaySongInfoBlock;
                // The mask is the layout owner. Editing the Image child itself
                // leaves its parent rectangle unchanged, so an override could
                // appear saved while the runtime cover remained attached to
                // the song-info block. The mask also keeps the cover centered
                // and preserves clipping for both built-in and skin images.
                case "songCover": return gameplaySongInfoCoverMask;
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
        private void ApplySkinInputAreas(GameplaySkinStyle style, AppSettings source = null)
        {
            // The room's fullscreen MOD remains authoritative. A local skin
            // cannot restore AIR or shrink that MOD's full-screen ground area.
            if (IsFullscreenTouchpadActive() || inputRig == null || style == null) return;
            source ??= settings;
            GameplaySkinElementDefinition ground = style.LayoutElement("touchpad");
            GameplayLayoutOverride groundOverride = LayoutOverride(source, "touchpad");
            Rect viewport = SkinCanvasViewport();
            if (groundOverride != null) inputRig.Ground?.SetPlayArea(MapSkinArea(
                new Rect(groundOverride.X, groundOverride.Y, groundOverride.Width, groundOverride.Height), viewport));
            else if (TrySkinArea(ground, out Rect groundArea)) inputRig.Ground?.SetPlayArea(MapSkinArea(groundArea, viewport));
            GameplaySkinElementDefinition air = style.LayoutElement("airZone");
            GameplayLayoutOverride airOverride = LayoutOverride(source, "airZone");
            if (airOverride != null) inputRig.Air?.SetAirArea(MapSkinArea(
                new Rect(airOverride.X, airOverride.Y, airOverride.Width, airOverride.Height), viewport));
            else if (TrySkinArea(air, out Rect airArea)) inputRig.Air?.SetAirArea(MapSkinArea(airArea, viewport));
        }

        private void SyncBoundaryLayoutOverrides(AppSettings source)
        {
            if (source == null) return;
            int divider = Mathf.Clamp(source.TouchpadHeight, 8, 45);
            int top = Mathf.Clamp(Mathf.Max(source.AirAreaTop, divider + 5), divider + 5, 100);
            source.TouchpadHeight = divider;
            source.GroundAreaHeight = divider;
            source.AirAreaBottom = divider;
            source.AirAreaTop = top;

            float divider01 = divider / 100f;
            float top01 = top / 100f;
            GameplayLayoutOverride ground = LayoutOverride(source, "touchpad");
            if (ground != null)
            {
                // Keep a skin's horizontal touchpad layout, but the touchpad
                // always starts at the bottom and ends at the shared divider.
                ground.X = Mathf.Clamp01(ground.X);
                ground.Width = Mathf.Clamp(ground.Width, .01f, 1f - ground.X);
                ground.Y = 0f;
                ground.Height = divider01;
            }
            GameplayLayoutOverride air = LayoutOverride(source, "airZone");
            if (air != null)
            {
                // AIR always begins at the same divider and ends at the AIR
                // top boundary. Only its horizontal skin layout is retained.
                air.X = Mathf.Clamp01(air.X);
                air.Width = Mathf.Clamp(air.Width, .01f, 1f - air.X);
                air.Y = divider01;
                air.Height = Mathf.Max(.01f, top01 - divider01);
            }
            GameplayLayoutOverride bottomLine = LayoutOverride(source, "airBoundaryBottom");
            if (bottomLine != null)
            {
                // The lower AIR boundary has its own line and follows only
                // the touchpad/AIR divider value.
                bottomLine.X = 0f;
                bottomLine.Width = 1f;
                bottomLine.Y = Mathf.Clamp01(divider01 - .002f);
                bottomLine.Height = .004f;
            }
            GameplayLayoutOverride topLine = LayoutOverride(source, "airBoundaryTop");
            if (topLine != null)
            {
                // The upper AIR boundary is a separate line and follows only
                // the AIR top value; it must never share the lower line's Y.
                topLine.X = 0f;
                topLine.Width = 1f;
                topLine.Y = Mathf.Clamp01(top01 - .002f);
                topLine.Height = .004f;
            }
            GameplayLayoutOverride mask = LayoutOverride(source, "trackMask");
            if (mask != null)
            {
                // Track mask geometry is controlled by the dedicated mask
                // height setting. A stale generic override must not replace
                // the configured top-of-track rectangle or re-enable it.
                float maskHeight = Mathf.Clamp(source.TrackMaskHeight, 4, 45) / 100f;
                mask.X = 0f;
                mask.Y = 1f - maskHeight;
                mask.Width = 1f;
                mask.Height = maskHeight;
                mask.Visible = source.TrackMaskEnabled;
            }
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
            bool coreHud = element.Id == "scoreHud" || element.Id == "comboHud";
            if (dynamic) {
                // Dynamic panels are activated by gameplay state. A hidden
                // element stays disabled; a visible one keeps its runtime
                // state (for example, the pause panel starts closed).
                if (!element.Visible) target.SetActive(false);
            } else target.SetActive(coreHud || element.Visible);
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.AddComponent<CanvasGroup>();
            // Score and combo are game-owned HUDs. A skin may style them, but
            // an omitted/false visibility flag must not hide the live score
            // stream while the same elements remain visible in the editor.
            // scoreHud/comboHud are game-owned live counters.  Their source
            // Text and digit-renderer layer must remain drawable even when an
            // older skin omitted `opacity` (JsonUtility reads that missing
            // field as 0) or authored a decorative zero-opacity value.  The
            // visibility switch for these two counters is handled by the
            // runtime HUD contract, so never let a skin CanvasGroup erase it.
            group.alpha = coreHud ? 1f : (element.Visible ? Mathf.Clamp01(element.Opacity) : 0f);
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
            Text styledText = target.GetComponentInChildren<Text>(true);
            if (styledText != null)
            {
                if (element.TextSize > 0) styledText.fontSize = element.TextSize;
                if (!string.IsNullOrEmpty(element.TextColor) &&
                    ColorUtility.TryParseHtmlString(element.TextColor, out Color textColor))
                    styledText.color = textColor;
            }
        }

        private void ApplySkinImage(GameObject target, string path)
        {
            if (target == null) return;
            RawImage raw = target.GetComponent<RawImage>();
            if (raw != null) {
                // A missing/invalid authored image keeps the texture already
                // supplied by the game (for example the current judgement).
                // Do not assign null here: that would erase the original UI.
                if (!string.IsNullOrEmpty(path)) {
                    Texture2D texture = GameplaySkinManager.LoadSkinImage(GameplaySkinManager.Active, path);
                    if (texture != null) raw.texture = texture;
                }
                raw.raycastTarget = false;
                return;
            }
            Image image = target.GetComponent<Image>();
            if (image != null) {
                // Clearing overrideSprite reveals the component's original
                // sprite when a package omits an image or references a file it
                // does not contain. A valid package image remains an override.
                image.overrideSprite = string.IsNullOrEmpty(path)
                    ? null
                    : GameplaySkinManager.LoadSkinSprite(GameplaySkinManager.Active, path);
                // Button graphics need their raycast target. Decorative images
                // remain non-interactive so they never steal gameplay touches.
                if (target.GetComponent<Button>() == null) image.raycastTarget = false;
            }
        }
    }
}
