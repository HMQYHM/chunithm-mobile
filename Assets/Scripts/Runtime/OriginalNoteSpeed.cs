using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 原版滚速参数表：将用户可见流速转换成谱面播放映射使用的倍率。
    // 这里不推进时间；真正的时间推进由 DspChartClock 完成。
    /// <summary>Values decoded from the original PlayOptionSpeedTableRecord.bin.</summary>
    public static class OriginalNoteSpeed
    {
        public const int DefaultOption = 20;
        public const float DefaultParameter = 4.8f;

        private static readonly string[] Labels = BuildLabels();

        public static int Count => 62;

        public static int ClampOption(int option) => Mathf.Clamp(option, 0, Count - 1);

        public static string Label(int option) => Labels[ClampOption(option)];

        public static float Parameter(int option)
        {
            option = ClampOption(option);
            if (option <= 56) return .8f + option * .2f;
            if (option <= 60) return 14f + (option - 57) * 2f;
            return 60f;
        }

        public static float VisualMultiplier(int option) => Parameter(option) / DefaultParameter * 1.2f;

        public static int FromLegacyPercent(int percent)
        {
            float target = DefaultParameter * Mathf.Clamp(percent, 50, 250) / 100f;
            int best = DefaultOption;
            float bestDistance = float.MaxValue;
            for (int option = 0; option < Count; option++) {
                float distance = Mathf.Abs(Parameter(option) - target);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = option;
            }
            return best;
        }

        private static string[] BuildLabels()
        {
            var labels = new string[Count];
            for (int option = 0; option <= 56; option++)
                labels[option] = (1f + option * .25f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            // Keep the visible high-speed choices on the same quarter-step
            // grid. The old integer branch jumped from 15.00 to 16/17/18/19,
            // which made the setting appear to skip speeds above 10.
            for (int option = 57; option <= 60; option++)
                labels[option] = (15f + (option - 56) * .25f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            labels[61] = "SONIC";
            return labels;
        }
    }
}
