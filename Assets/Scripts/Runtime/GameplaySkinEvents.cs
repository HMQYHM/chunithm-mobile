using System;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// 受限的游玩皮肤事件总线。皮肤只能接收游玩状态，不能反向修改判定、输入或账号数据。
    /// 事件总线不保存资源引用；订阅方必须在 OnEnable/OnDisable 中成对注册和注销。
    /// </summary>
    public static class GameplaySkinEvents
    {
        /// <summary>触控板四分区：左上、左下、右上、右下。</summary>
        public enum TouchZone { LeftTop, LeftBottom, RightTop, RightBottom }

        /// <summary>供皮肤读取的四区输入快照，不包含账号、设备或原始文件路径。</summary>
        public readonly struct TouchZoneSnapshot
        {
            public readonly bool LeftTop;
            public readonly bool LeftBottom;
            public readonly bool RightTop;
            public readonly bool RightBottom;
            public readonly int ActiveCount;

            public TouchZoneSnapshot(bool leftTop, bool leftBottom, bool rightTop, bool rightBottom)
            {
                LeftTop = leftTop;
                LeftBottom = leftBottom;
                RightTop = rightTop;
                RightBottom = rightBottom;
                ActiveCount = (leftTop ? 1 : 0) + (leftBottom ? 1 : 0) +
                              (rightTop ? 1 : 0) + (rightBottom ? 1 : 0);
            }
        }

        /// <summary>
        /// 聚合后的单侧触控轨迹。坐标和速度均为触控板局部的 0..1 标准化值，
        /// 不暴露手指 ID 或原始设备信息，皮肤只能读取而不能反向写入输入。
        /// </summary>
        public readonly struct TouchTrack
        {
            public readonly bool Active;
            public readonly int Count;
            public readonly float CenterX;
            public readonly float CenterY;
            public readonly float VelocityX;
            public readonly float VelocityY;

            public TouchTrack(bool active, int count, float centerX, float centerY,
                float velocityX, float velocityY)
            {
                Active = active;
                Count = count;
                CenterX = centerX;
                CenterY = centerY;
                VelocityX = velocityX;
                VelocityY = velocityY;
            }
        }

        /// <summary>
        /// 每帧供游玩皮肤读取的左右触控中心点快照。倒计时或暂停时，
        /// 调用方可以将 InputActive 设为 false，但不会影响皮肤布局。
        /// </summary>
        public readonly struct TouchTelemetrySnapshot
        {
            public readonly TouchTrack Left;
            public readonly TouchTrack Right;
            public readonly double TimeMs;
            public readonly bool InputActive;

            public TouchTelemetrySnapshot(TouchTrack left, TouchTrack right,
                double timeMs, bool inputActive)
            {
                Left = left;
                Right = right;
                TimeMs = timeMs;
                InputActive = inputActive;
            }
        }

        /// <summary>每帧发布当前触控板和 AIR 采样结果。</summary>
        public static event Action<ArcadeInputFrame> InputUpdated;

        /// <summary>每帧发布触控板四区的聚合状态。</summary>
        public static event Action<TouchZoneSnapshot> TouchZonesUpdated;

        /// <summary>发布左右触控中心点和运动速度，供皮肤动画使用。</summary>
        public static event Action<TouchTelemetrySnapshot> TouchTelemetryUpdated;

        /// <summary>发布 Combo、当前分数、最高 Combo 和当前评级。</summary>
        public static event Action<int, int, int, string> MetricsUpdated;

        /// <summary>The below-note mode and signed value, even after its text is hidden.</summary>
        public static event Action<string, int> DisplayMetricUpdated;

        private static readonly string[] displayMetricNames = {
            "combo", "remaining_combo", "score", "sss+", "sss", "ss+", "ss", "s+", "s", "lost_score", "best_score"
        };

        /// <summary>Stable package names matching the persisted HUD mode indices.</summary>
        public static string DisplayMetricName(int mode) =>
            mode >= 0 && mode < displayMetricNames.Length ? displayMetricNames[mode] : "unknown";

        /// <summary>发布一次判定结果；皮肤只能读取结果，不能修改成绩。</summary>
        public static event Action<Chart.Judgement> JudgementUpdated;

        /// <summary>通知皮肤当前是否处于有效游玩状态。</summary>
        public static event Action<bool> GameplayStateChanged;

        /// <summary>将输入采样广播给所有已启用的皮肤表现层。</summary>
        public static void PublishInput(ArcadeInputFrame frame) => InputUpdated?.Invoke(frame);

        /// <summary>发布四区触控状态；调用方只传递布尔状态，不暴露原始触控数据。</summary>
        public static void PublishTouchZones(TouchZoneSnapshot zones) => TouchZonesUpdated?.Invoke(zones);

        /// <summary>广播只读触控轨迹快照，不让皮肤包接触判定器或原始触点。</summary>
        public static void PublishTouchTelemetry(TouchTelemetrySnapshot telemetry) =>
            TouchTelemetryUpdated?.Invoke(telemetry);

        /// <summary>将成绩指标广播给皮肤表现层。</summary>
        public static void PublishMetrics(int combo, int score, int maxCombo, string rank) =>
            MetricsUpdated?.Invoke(combo, score, maxCombo, rank ?? "");

        /// <summary>Visual telemetry only; publishing this never changes score or judgement.</summary>
        public static void PublishDisplayMetric(int mode, int value) =>
            DisplayMetricUpdated?.Invoke(DisplayMetricName(mode), value);

        /// <summary>将判定等级广播给皮肤表现层。</summary>
        public static void PublishJudgement(Chart.Judgement judgement) => JudgementUpdated?.Invoke(judgement);

        /// <summary>广播游玩状态变化。</summary>
        public static void PublishGameplayState(bool playing) => GameplayStateChanged?.Invoke(playing);
    }
}
