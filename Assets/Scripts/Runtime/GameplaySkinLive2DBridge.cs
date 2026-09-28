using System;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Live2D 皮肤的安全桥接层。
    /// 皮肤包只声明模型文件和参数名，游戏负责把只读触控快照转换成参数值。
    /// 具体 Cubism 渲染器可以在后续接入时订阅 ParametersUpdated，不执行皮肤代码。
    /// </summary>
    public sealed class GameplaySkinLive2DBridge : MonoBehaviour
    {
        /// <summary>供 Cubism 适配器读取的归一化模型参数快照。</summary>
        public readonly struct ParameterFrame
        {
            public readonly string LeftX;
            public readonly string LeftY;
            public readonly string RightX;
            public readonly string RightY;
            public readonly string LeftDown;
            public readonly string RightDown;
            public readonly float LeftXValue;
            public readonly float LeftYValue;
            public readonly float RightXValue;
            public readonly float RightYValue;
            public readonly float LeftDownValue;
            public readonly float RightDownValue;
            public readonly string IdleMotion;
            public readonly string LeftPressMotion;
            public readonly string RightPressMotion;

            public ParameterFrame(GameplaySkinStyle style,
                GameplaySkinEvents.TouchTelemetrySnapshot telemetry)
            {
                LeftX = style.Live2DLeftXParameter;
                LeftY = style.Live2DLeftYParameter;
                RightX = style.Live2DRightXParameter;
                RightY = style.Live2DRightYParameter;
                LeftDown = style.Live2DLeftDownParameter;
                RightDown = style.Live2DRightDownParameter;
                LeftXValue = MapAxis(telemetry.Left.CenterX);
                LeftYValue = MapAxis(telemetry.Left.CenterY);
                RightXValue = MapAxis(telemetry.Right.CenterX);
                RightYValue = MapAxis(telemetry.Right.CenterY);
                LeftDownValue = telemetry.InputActive && telemetry.Left.Active ? 1f : 0f;
                RightDownValue = telemetry.InputActive && telemetry.Right.Active ? 1f : 0f;
                IdleMotion = style.Live2DIdleMotion;
                LeftPressMotion = style.Live2DLeftPressMotion;
                RightPressMotion = style.Live2DRightPressMotion;
            }

            private static float MapAxis(float value) => Mathf.Clamp(value * 2f - 1f, -1f, 1f);
        }

        /// <summary>
        /// 供内置 Cubism 渲染器订阅的安全参数事件。外部皮肤包不能订阅 C# 事件。
        /// </summary>
        public static event Action<ParameterFrame> ParametersUpdated;

        private GameplaySkinStyle style;
        private bool warnedMissingSdk;

        /// <summary>读取当前皮肤的 Live2D 配置并绑定触控快照事件。</summary>
        public void Initialize()
        {
            GameplaySkinEvents.TouchTelemetryUpdated += OnTouchTelemetryUpdated;
            RefreshSkin();
        }

        private void OnDestroy() => GameplaySkinEvents.TouchTelemetryUpdated -= OnTouchTelemetryUpdated;

        /// <summary>皮肤切换时刷新模型声明；实际模型由 Cubism 适配器负责加载。</summary>
        public void RefreshSkin()
        {
            style = GameplaySkinManager.Style;
            if (!style.Live2DEnabled) return;
            if (!HasCubismRuntime() && !warnedMissingSdk)
            {
                warnedMissingSdk = true;
                Debug.LogWarning("Live2D skin is configured, but Cubism SDK is not installed; model rendering is disabled.");
            }
        }

        private void OnTouchTelemetryUpdated(GameplaySkinEvents.TouchTelemetrySnapshot telemetry)
        {
            if (style == null || !style.Live2DEnabled) return;
            ParametersUpdated?.Invoke(new ParameterFrame(style, telemetry));
        }

        /// <summary>
        /// 通过类型探测避免在未安装 Cubism SDK 时产生硬编译依赖。
        /// </summary>
        private static bool HasCubismRuntime()
        {
            return Type.GetType("Live2D.Cubism.Core.CubismModel, Live2D.Cubism") != null ||
                   Type.GetType("Live2D.Cubism.Core.CubismModel, Live2D.Cubism.Core") != null;
        }
    }
}
