using UnityEngine;

namespace ChunithmMobile.Rendering
{
    // 轨道坐标转换：集中定义 16 lane、AIR 高度、判定线和屏幕空间之间的几何关系。
    // 改动轨道尺寸时应优先修改这里或统一布局参数，避免各渲染器各自计算造成错位。
    /// <summary>Single source of truth shared by track, notes, effects and hit tests.</summary>
    public static class ChartSpace
    {
        public const int Lanes = 16;
        public const float NearHalfWidth = 8f;
        public const float NearZ = -3.5f;
        public const float JudgementZ = .25f;
        // The ground plane and the gold bar are the mobile judgement anchor.
        // Input-zone layout must not move this visual/gameplay coordinate.
        public const float GroundY = .025f;
        public const float FarZ = 44f;
        private static float farWidthScale = 1f;

        public static float FarHalfWidth => NearHalfWidth * farWidthScale;

        public static bool SetFarWidthPercent(int percent)
        {
            float next = Mathf.Clamp(percent, 45, 160) / 100f;
            if (Mathf.Approximately(next, farWidthScale)) return false;
            farWidthScale = next;
            return true;
        }

        public static float HalfWidthAt(float z)
        {
            return Mathf.Lerp(NearHalfWidth, FarHalfWidth, Mathf.Clamp01(z / FarZ));
        }

        public static float LaneX(float lanePosition, float z)
        {
            float normalized = lanePosition / Lanes * 2f - 1f;
            return normalized * HalfWidthAt(z);
        }

        public static float NoteCenterX(float lane, float width, float z) => LaneX(lane + width * .5f, z);
        public static float NoteHalfWidth(float width, float z) => HalfWidthAt(z) * width / Lanes;
        public static float AirY(float authoredHeight) => 1.25f + authoredHeight * .34f;
    }
}
