using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// 将安全皮肤包中的 Cubism 模型渲染到游戏 UI，并把左右触控轨迹映射到模型参数。
    /// 皮肤包只提供数据，模型实例和相机均由游戏创建，避免执行外部脚本。
    /// </summary>
    public sealed class GameplaySkinCubismRenderer : MonoBehaviour
    {
        private RectTransform root;
        private RawImage output;
        private Camera modelCamera;
        private RenderTexture renderTexture;
        private GameObject modelObject;
        private CubismModel model;
        private GameplaySkinInfo activeSkin;
        private GameplaySkinStyle style;

        /// <summary>创建模型输出层并读取当前皮肤。</summary>
        public void Initialize(RectTransform parent)
        {
            if (parent == null) return;
            root = GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            GameObject outputObject = new GameObject("Live2D skin output", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage));
            outputObject.transform.SetParent(root, false);
            output = outputObject.GetComponent<RawImage>();
            output.raycastTarget = false;
            output.color = Color.white;
            GameplaySkinEvents.TouchTelemetryUpdated += OnTouchTelemetryUpdated;
            RefreshSkin();
        }

        private void OnDestroy()
        {
            GameplaySkinEvents.TouchTelemetryUpdated -= OnTouchTelemetryUpdated;
            DestroyModelResources();
        }

        /// <summary>皮肤切换时重建模型，模型路径和位置由皮肤清单决定。</summary>
        public void RefreshSkin()
        {
            activeSkin = GameplaySkinManager.Active;
            style = GameplaySkinManager.Style;
            DestroyModelResources();
            if (output == null || activeSkin == null || activeSkin.BuiltIn || !style.Live2DEnabled)
            {
                if (output != null) output.gameObject.SetActive(false);
                return;
            }

            SetOutputRect();
            model = LoadModel(activeSkin, style.Live2DModelPath);
            if (model == null)
            {
                Debug.LogWarning("Live2D skin model could not be loaded: " + style.Live2DModelPath);
                output.gameObject.SetActive(false);
                return;
            }

            modelObject = model.gameObject;
            modelObject.name = "Gameplay skin Live2D model";
            modelObject.transform.SetParent(null, false);
            ConfigureCamera();
            output.texture = renderTexture;
            output.gameObject.SetActive(true);
        }

        private void SetOutputRect()
        {
            // Live2D position is kept in the skin manifest; these defaults leave
            // enough room for the game's judgement line and touch tablet.
            SetNormalizedRect(output.rectTransform, style.Live2DX, style.Live2DY,
                style.Live2DWidth, style.Live2DHeight);
        }

        private static void SetNormalizedRect(RectTransform target, float x, float y, float width, float height)
        {
            target.anchorMin = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
            target.anchorMax = new Vector2(Mathf.Clamp01(x + width), Mathf.Clamp01(y + height));
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }

        private static CubismModel LoadModel(GameplaySkinInfo skin, string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath)) return null;
            string normalized = modelPath.Replace('\\', '/').TrimStart('/');
            CubismModel3Json source = CubismModel3Json.LoadAtPath(normalized,
                (type, path) => GameplaySkinManager.LoadLive2DAsset(skin, type, path));
            if (source == null) return null;
            try { return source.ToModel(); }
            catch (Exception exception)
            {
                Debug.LogWarning("Live2D Cubism model creation failed: " + exception.Message);
                return null;
            }
        }

        private void ConfigureCamera()
        {
            GameObject cameraObject = new GameObject("Live2D skin camera");
            modelCamera = cameraObject.AddComponent<Camera>();
            modelCamera.clearFlags = CameraClearFlags.SolidColor;
            modelCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            modelCamera.orthographic = true;
            modelCamera.orthographicSize = 2.8f;
            modelCamera.nearClipPlane = .01f;
            modelCamera.farClipPlane = 100f;
            modelCamera.transform.position = new Vector3(0f, 0f, -10f);

            int layer = 30;
            modelCamera.cullingMask = 1 << layer;
            SetLayerRecursively(modelObject.transform, layer);
            CubismRenderController renderController = modelObject.GetComponent<CubismRenderController>();
            if (renderController != null) renderController.SortingOrder = 0;

            renderTexture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32)
            {
                name = "Live2D skin render texture",
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1
            };
            renderTexture.Create();
            modelCamera.targetTexture = renderTexture;
            modelObject.SetActive(true);
        }

        private static void SetLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++) SetLayerRecursively(target.GetChild(i), layer);
        }

        private void OnTouchTelemetryUpdated(GameplaySkinEvents.TouchTelemetrySnapshot telemetry)
        {
            if (model == null || style == null) return;
            SetParameter(style.Live2DLeftXParameter, telemetry.Left.CenterX * 2f - 1f);
            SetParameter(style.Live2DLeftYParameter, telemetry.Left.CenterY * 2f - 1f);
            SetParameter(style.Live2DRightXParameter, telemetry.Right.CenterX * 2f - 1f);
            SetParameter(style.Live2DRightYParameter, telemetry.Right.CenterY * 2f - 1f);
            SetParameter(style.Live2DLeftDownParameter,
                telemetry.InputActive && telemetry.Left.Active ? 1f : 0f);
            SetParameter(style.Live2DRightDownParameter,
                telemetry.InputActive && telemetry.Right.Active ? 1f : 0f);
        }

        private void SetParameter(string id, float value)
        {
            if (string.IsNullOrWhiteSpace(id) || model == null || model.Parameters == null) return;
            CubismParameter parameter = model.Parameters.FindById(id);
            if (parameter == null) return;
            parameter.Value = Mathf.Clamp(value, parameter.MinimumValue, parameter.MaximumValue);
        }

        private void DestroyModelResources()
        {
            if (modelObject != null) Destroy(modelObject);
            modelObject = null;
            model = null;
            if (modelCamera != null) Destroy(modelCamera.gameObject);
            modelCamera = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            renderTexture = null;
            if (output != null) output.texture = null;
        }
    }
}
