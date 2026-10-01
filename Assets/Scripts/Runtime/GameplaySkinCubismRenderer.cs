using System;
using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Displays an imported Live2D model inside the skin-authored UI rectangle.
    /// Touch data is read-only: rendering and motions never change gameplay judgement.
    /// </summary>
    public sealed class GameplaySkinCubismRenderer : MonoBehaviour
    {
        private RawImage output;
        private Material composite;
        private GameplaySkinModelSurface surface;
        private GameplaySkinStyle style;
        private GameplaySkinEvents.TouchTelemetrySnapshot telemetry;
        private bool leftPressed, rightPressed;

        public void Initialize(RectTransform parent)
        {
            if (parent == null) return;
            var root = GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var child = new GameObject("Live2D skin output", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            child.transform.SetParent(root, false);
            output = child.GetComponent<RawImage>();
            output.raycastTarget = false;
            composite = new Material(Resources.Load<Shader>("SkinLive2D/Composite"));
            output.material = composite;
            GameplaySkinEvents.TouchTelemetryUpdated += OnTouch;
            GameplaySkinEvents.SkinMotionRequested += OnSkinMotion;
            RefreshSkin();
        }

        /// <summary>Rebuilds all owned resources when the active skin changes.</summary>
        public void RefreshSkin()
        {
            DisposeSurface();
            style = GameplaySkinManager.Style;
            var skin = GameplaySkinManager.Active;
            if (output == null || skin == null || skin.BuiltIn || !style.Live2DEnabled) return;
            output.rectTransform.anchorMin = new Vector2(style.Live2DX, style.Live2DY);
            output.rectTransform.anchorMax = new Vector2(style.Live2DX + style.Live2DWidth, style.Live2DY + style.Live2DHeight);
            output.rectTransform.offsetMin = output.rectTransform.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            var rect = output.rectTransform.rect;
            float aspect = rect.height > 1 ? rect.width / rect.height : 1f;
            try
            {
                surface = new GameplaySkinModelSurface(skin, style.Live2DModelPath, aspect);
                surface.Camera.enabled = isActiveAndEnabled;
                surface.Model.gameObject.SetActive(isActiveAndEnabled);
                output.texture = surface.Texture;
                output.gameObject.SetActive(true);
                Debug.Log("[skin/live2d] Loaded " + skin.Id + " model=" + style.Live2DModelPath);
            }
            catch (Exception exception)
            {
                // Keep the ordinary skin usable, but retain the full failure stack
                // in existing game logs instead of silently showing just the tablet.
                Debug.LogError("[skin/live2d] Failed to load " + skin.Id + ": " + exception);
                DisposeSurface();
            }
        }

        private void OnTouch(GameplaySkinEvents.TouchTelemetrySnapshot value)
        {
            telemetry = value;
            bool left = value.InputActive && value.Left.Active;
            bool right = value.InputActive && value.Right.Active;
            if (surface != null)
            {
                if (left && !leftPressed) surface.PlayMotion(style.Live2DLeftPressMotion);
                if (right && !rightPressed) surface.PlayMotion(style.Live2DRightPressMotion);
            }
            leftPressed = left;
            rightPressed = right;
        }

        private void OnSkinMotion(string group)
        {
            if (!isActiveAndEnabled || surface == null || style == null) return;
            foreach (var rule in style.CustomDisplays)
                if (rule.Action == "live2dMotion" && rule.Motion == group)
                {
                    surface.PlayMotion(group);
                    break;
                }
        }

        private void LateUpdate()
        {
            if (surface == null) return;
            surface.ResumeIdle();
            // Apply input after animation sampling so authored idle curves cannot
            // overwrite the fingers. Paused input resets paws to their idle pose.
            ApplyTrack(telemetry.Left, telemetry.InputActive, style.Live2DLeftXParameter,
                style.Live2DLeftYParameter, style.Live2DLeftVisibleParameter, style.Live2DLeftDownParameter,
                style.Live2DLeftXRange, style.Live2DLeftYRange, true);
            ApplyTrack(telemetry.Right, telemetry.InputActive, style.Live2DRightXParameter,
                style.Live2DRightYParameter, style.Live2DRightVisibleParameter, style.Live2DRightDownParameter,
                style.Live2DRightXRange, style.Live2DRightYRange, false);
            surface.UpdateFrame();
        }

        private void ApplyTrack(GameplaySkinEvents.TouchTrack track, bool inputActive,
            string x, string y, string visible, string down, Vector2 xRange, Vector2 yRange, bool left)
        {
            bool pressed = inputActive && track.Active;
            // Each paw uses its own half of the input, then the skin's calibrated
            // model range so a gamepad's outer travel cannot put it off the tablet.
            float localX = Mathf.Clamp01(left ? track.CenterX * 2 : (track.CenterX - .5f) * 2);
            SetAxis(x, pressed ? localX : .5f, xRange);
            SetAxis(y, pressed ? track.CenterY : .5f, yRange);
            SetParameter(visible, pressed ? 1 : 0);
            SetParameter(down, pressed ? 1 : 0);
        }

        private void SetAxis(string id, float normalized, Vector2 range)
        {
            var parameter = Find(id);
            if (parameter != null)
            {
                float calibrated = Mathf.Lerp(range.x, range.y, Mathf.Clamp01(normalized));
                parameter.Value = Mathf.Lerp(parameter.MinimumValue, parameter.MaximumValue, calibrated);
            }
        }

        private CubismParameter Find(string id) => string.IsNullOrWhiteSpace(id) ? null : surface?.Model.Parameters.FindById(id);
        private void SetParameter(string id, float value)
        {
            var parameter = Find(id);
            if (parameter != null) parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }

        private void OnEnable()
        {
            if (surface == null) return;
            surface.Model.gameObject.SetActive(true);
            surface.Camera.enabled = true;
        }
        private void OnDisable()
        {
            telemetry = default;
            leftPressed = rightPressed = false;
            if (surface != null)
            {
                surface.Camera.enabled = false;
                surface.Model.gameObject.SetActive(false);
            }
        }

        private void DisposeSurface()
        {
            if (output != null) { output.texture = null; output.gameObject.SetActive(false); }
            surface?.Dispose();
            surface = null;
            telemetry = default;
            leftPressed = rightPressed = false;
        }

        private void OnDestroy()
        {
            GameplaySkinEvents.TouchTelemetryUpdated -= OnTouch;
            GameplaySkinEvents.SkinMotionRequested -= OnSkinMotion;
            DisposeSurface();
            if (composite != null)
            {
                if (Application.isPlaying) Destroy(composite);
                else DestroyImmediate(composite);
            }
        }
    }
}
