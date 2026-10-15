using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    // 可视化布局回归测试：验证拖动手柄、统一长度比例、触控范围线和保存恢复。
    public static class LayoutFeedbackSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void BuildVerifiedApk()
        {
            Run();
            ModDialogSelfTest.Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            VerifyPreview(false);
            VerifyPreview(true);
            VerifyFullscreenTouchpadRuntimeOverride();
            VerifyVisualLifetimes();
            Debug.Log("Layout feedback regression passed: draggable entry bounds, preview toggles, exact contact width, fullscreen touchpad runtime override, brightness, separated multi-touch ranges and non-judged note lifetimes.");
        }

        // The accessibility MOD must override only the active gameplay surface.
        // It may never write its enlarged input area back into the saved layout,
        // otherwise opening the visual editor after a round would corrupt it.
        private static void VerifyFullscreenTouchpadRuntimeOverride()
        {
            var root = new GameObject("Fullscreen touchpad runtime regression");
            try {
                ArcadeInputRig rig = root.AddComponent<ArcadeInputRig>();
                Call(rig, "Awake");
                var shell = root.AddComponent<AppUiShell>();
                var font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font);
                Set(shell, "titleFont", font);
                var canvasObject = new GameObject("Fullscreen MOD canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                Set(shell, "canvas", canvasObject.GetComponent<Canvas>());
                var settings = new AppSettings {
                    GroundAreaHeight = 24,
                    AirAreaBottom = 24,
                    AirAreaTop = 91,
                    TouchpadHeight = 24,
                    TouchpadOpacity = 37,
                    TouchEffectOpacity = 68,
                    TouchHighlightBrightness = 142,
                    ShowTouchpadVisual = true,
                    ShowTouchFeedback = true,
                    ShowInputBoundary = true
                };
                Set(shell, "settings", settings);
                Set(shell, "inputRig", rig);
                Call(shell, "BuildGameplay");
                ModSettings mods = Get<ModSettings>(shell, "mods");
                mods.FullscreenTouchpad = true;
                Call(shell, "ApplyRuntimeSettings");

                Require(settings.GroundAreaHeight == 24 && settings.AirAreaBottom == 24 &&
                    settings.AirAreaTop == 91 && settings.TouchpadHeight == 24 &&
                    settings.TouchpadOpacity == 37 && settings.TouchEffectOpacity == 68 &&
                    settings.TouchHighlightBrightness == 142, "fullscreen MOD changed saved layout values");
                Require(!Get<RectTransform>(shell, "gameplayTouchpad").gameObject.activeSelf,
                    "fullscreen MOD leaves touchpad visual visible");
                Require(!Get<RectTransform>(shell, "gameplayAirZone").gameObject.activeSelf,
                    "fullscreen MOD leaves AIR visual visible");
                Require(!Get<Image>(shell, "gameplayAirBoundaryBottom").gameObject.activeSelf &&
                    !Get<Image>(shell, "gameplayAirBoundaryTop").gameObject.activeSelf,
                    "fullscreen MOD leaves boundary lines visible");
                Rect groundArea = Get<Rect>(rig.Ground, "normalizedPlayArea");
                Rect airArea = Get<Rect>(rig.Air, "normalizedAirArea");
                Require(Mathf.Abs(groundArea.height - .95f) < .0001f, "fullscreen ground area is not 95%");
                Require(airArea.height <= .0001f, "fullscreen MOD leaves a physical AIR area");
                TouchContactOverlay contacts = Get<TouchContactOverlay>(shell, "gameplayTouchContacts");
                Require(!Get<bool>(contacts, "visible") && Mathf.Abs(Get<float>(contacts, "opacity")) < .0001f,
                    "fullscreen MOD leaves touch feedback enabled");

                mods.FullscreenTouchpad = false;
                Call(shell, "ApplyRuntimeSettings");
                groundArea = Get<Rect>(rig.Ground, "normalizedPlayArea");
                airArea = Get<Rect>(rig.Air, "normalizedAirArea");
                Require(Mathf.Abs(groundArea.height - .24f) < .0001f && airArea.height > .6f,
                    "disabling fullscreen MOD did not restore normal input areas");
                Require(Get<RectTransform>(shell, "gameplayTouchpad").gameObject.activeSelf &&
                    Get<RectTransform>(shell, "gameplayAirZone").gameObject.activeSelf,
                    "disabling fullscreen MOD did not restore normal visuals");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void VerifyPreview(bool highContrast)
        {
            var root = new GameObject("Layout feedback regression");
            try {
                var shell = root.AddComponent<AppUiShell>();
                var font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font);
                Set(shell, "titleFont", font);
                var settings = new AppSettings { HighContrastUi = highContrast };
                Set(shell, "settings", settings);
                Set(shell, "draftSettings", new AppSettings { HighContrastUi = highContrast });
                var canvasObject = new GameObject("Test canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
                Set(shell, "canvas", canvas);
                Call(shell, "BuildGameplay");
                RectTransform songInfo = Get<RectTransform>(shell, "gameplaySongInfoBlock");
                RectTransform songCover = Get<RectTransform>(shell, "gameplaySongInfoCoverMask");
                Require(songCover.parent == songInfo,
                    "song cover is not owned by the song information element");
                Set(shell, "draftSettings", new AppSettings());
                Call(shell, "EnsureAllDraftLayoutOverrides");
                var seeded = Get<AppSettings>(shell, "draftSettings").GameplayLayoutOverrides
                    .Find(item => item != null && item.ElementId == "songCover");
                Require(seeded != null && !seeded.Independent,
                    "fallback cover layout detached the cover before explicit editing");
                Call(shell, "BuildLayoutEditor");
                var layer = Get<RectTransform>(shell, "layoutEditorLayer");
                layer.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                var entry = Get<RectTransform>(shell, "layoutEditorFloatingButton");
                Require(entry.GetComponent<UiDragHandle>() != null && entry.sizeDelta == new Vector2(70, 70), "menu button missing drag or size");
                foreach (Vector2 point in new[] { Vector2.zero, Vector2.one, new Vector2(.5f, .6f) }) {
                    Call(shell, "PositionLayoutEditorFloatingButton", point);
                    Require(entry.anchorMin.x > 0 && entry.anchorMin.x < 1 && entry.anchorMin.y > 0 && entry.anchorMin.y < 1,
                        "menu button escaped screen");
                }
                var draft = Get<AppSettings>(shell, "draftSettings");
                var touchpad = Get<RectTransform>(shell, "gameplayTouchpad");
                var guide = Get<Image>(shell, "editorTouchRangeGuide");
                var fill = Get<Image>(shell, "editorTouchRangeFill");
                foreach (bool visible in new[] { false, true }) {
                    Get<Button>(shell, "layoutEditorTouchpadButton").onClick.Invoke();
                    Get<Button>(shell, "layoutEditorBoundaryButton").onClick.Invoke();
                    Require(touchpad.gameObject.activeSelf == visible, "touchpad preview ignores visibility");
                    Require(Get<Image>(shell, "gameplayAirBoundaryBottom").gameObject.activeSelf == visible &&
                        Get<Image>(shell, "gameplayAirBoundaryTop").gameObject.activeSelf == visible,
                        "boundary preview ignores visibility");
                }
                foreach (int width in new[] { 5, 10, 30 }) {
                    Get<Slider>(shell, "editorTouchRangeSlider").value = width;
                    Require(draft.TouchContactRangeTenths == width, "range slider did not update draft settings");
                    Require(Mathf.Abs((guide.rectTransform.anchorMax.x - guide.rectTransform.anchorMin.x) * 16 - width / 10f) < .001f,
                        "range line does not match configured lane width");
                    Require(fill.rectTransform.anchorMin.x == guide.rectTransform.anchorMin.x &&
                        fill.rectTransform.anchorMax.x == guide.rectTransform.anchorMax.x, "highlight differs from range line");
                }
                draft.TouchHighlightBrightness = 0;
                Call(shell, "RefreshLayoutEditor");
                Require(guide.color.a == 0 && fill.color.a == 0, "zero brightness still lights preview");
                draft.TouchHighlightBrightness = 100;
                Call(shell, "RefreshLayoutEditor");
                Require(guide.color.a > 0 && fill.color.a > 0, "brightness preview does not recover");
                var frame = new ArcadeInputFrame();
                frame.Ground.Add(new GroundContact { Phase = GroundPhase.Stationary, CoveredLaneMask = 3 });
                frame.Ground.Add(new GroundContact { Phase = GroundPhase.Down, CoveredLaneMask = 1 << 12 });
                Call(shell, "UpdateTouchpadFeedback", settings, frame, false);
                var lines = Get<List<Image>>(shell, "gameplayTouchRangeGuides");
                Require(lines[0].gameObject.activeSelf && lines[1].gameObject.activeSelf && lines[12].gameObject.activeSelf &&
                    !lines[6].gameObject.activeSelf && !guide.gameObject.activeSelf, "multi-touch fills untouched gap");
                Require(typeof(AppSettings).GetField("TouchpadTilt") == null, "removed slope setting remains");
                Call(shell, "RefreshLayoutEditor");
                Call(shell, "PositionLayoutEditorFloatingButton", new Vector2(.96f, .94f));
                Get<RectTransform>(shell, "layoutEditorMenu").gameObject.SetActive(true);
                entry.SetAsLastSibling();
                VerifyModalAboveLayoutEditor(shell, canvas);
                CapturePreview(root, canvas, highContrast);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void CapturePreview(GameObject root, Canvas canvas, bool highContrast)
        {
            var cameraObject = new GameObject("Preview test camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 540;
            camera.aspect = 16f / 9f;
            canvas.worldCamera = camera;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes("Build/layout-feedback-preview-" + highContrast + ".png", image.EncodeToPNG());
            }
            finally {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void VerifyModalAboveLayoutEditor(AppUiShell shell, Canvas canvas)
        {
            // The confirmation dialog must live above the full-screen editor,
            // not below it inside the safe-area container. This also verifies
            // the raycast state that makes the dialog buttons tappable.
            Call(shell, "BuildModal");
            var callback = (UnityEngine.Events.UnityAction)(() => { });
            Call(shell, "ShowModal", "放弃未保存修改？", "测试确认", callback, true);
            RectTransform modal = Get<RectTransform>(shell, "modalLayer");
            CanvasGroup group = modal.GetComponent<CanvasGroup>();
            Button confirm = Get<Button>(shell, "modalConfirm");
            Require(modal.parent == canvas.transform && modal.GetSiblingIndex() == canvas.transform.childCount - 1,
                "modal is not above the full-screen layout editor");
            Require(group != null && group.interactable && group.blocksRaycasts,
                "modal does not block the underlying editor for pointer input");
            Require(confirm != null && confirm.interactable && confirm.gameObject.activeSelf,
                "modal confirmation button is not interactable");
            Call(shell, "HideModal");
            Require(!modal.gameObject.activeSelf && !group.interactable && !group.blocksRaycasts,
                "hidden modal kept intercepting pointer input");
        }

        private static void VerifyVisualLifetimes()
        {
            var root = new GameObject("Note lifetime regression");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (Get<IDictionary>(renderer, "headMaterials").Count == 0) Call(renderer, "Awake");
                foreach (NoteKind kind in new[] { NoteKind.Hold, NoteKind.Slide, NoteKind.AirHold, NoteKind.AirAction }) {
                    var note = new ChartNote { Kind = kind, TimeMs = 1000, EndTimeMs = 2000, Lane = 4,
                        Width = 2, EndLane = 6, EndWidth = 2, HasVisualHead = true, VisibleHead = false,
                        IsLongChainTail = true, VisibleTail = true, JudgePointCount = 0 };
                    var chart = new ParsedChart(new List<ChartNote> { note }, 4000, 120, 0,
                        new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(), new List<ScrollAssignment>());
                    renderer.Bind(chart, new DspChartClock());
                    Call(renderer, "ProcessNote", 0, 990.0);
                    object visual = Get<IDictionary>(renderer, "active")[0];
                    Require(Part(visual, "Head").activeSelf, "visual head disappeared before arrival: " + kind);
                    Call(renderer, "ProcessNote", 0, 1001.0);
                    Require(!Part(visual, "Head").activeSelf && Part(visual, "Body").activeSelf,
                        "visual head passed line or consumed body: " + kind);
                    Call(renderer, "ProcessNote", 0, 1990.0);
                    Require(Part(visual, "Tail").activeSelf, "tail disappeared before arrival: " + kind);
                    Call(renderer, "ProcessNote", 0, 2001.0);
                    // The renderer intentionally keeps the final strip inside
                    // its small judgement-plane hysteresis band. This is the
                    // frame-to-frame stability guarantee for curved HOLD/AIR
                    // ribbons; the tail must disappear after the band, not on
                    // the first sample below the plane.
                    Require(Part(visual, "Tail").activeSelf, "tail flickered inside judgement hysteresis: " + kind);
                    Call(renderer, "ProcessNote", 0, 2010.0);
                    Require(!Part(visual, "Tail").activeSelf, "tail remained after judgement hysteresis: " + kind);
                    renderer.Clear();
                }
                var tap = new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000, EndTimeMs = 1000, Lane = 4, Width = 2 };
                renderer.Bind(new ParsedChart(new List<ChartNote> { tap }, 3000, 120, 1,
                    new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(), new List<ScrollAssignment>()), new DspChartClock());
                Call(renderer, "ProcessNote", 0, 1001.0);
                object tapVisual = Get<IDictionary>(renderer, "active")[0];
                Require(Part(tapVisual, "Head").activeSelf, "judgeable TAP lost late window");
                renderer.MarkJudged(0, 0);
                Call(renderer, "ProcessNote", 0, 1002.0);
                Require(!Part(tapVisual, "Head").activeSelf, "judged TAP remained visible");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject Part(object visual, string name) => (GameObject)visual.GetType().GetField(name).GetValue(visual);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
