using UnityEngine;

namespace ChunithmMobile.Runtime
{
    // 原版滚速参数表：将用户可见流速转换成谱面播放映射使用的倍率。
    // 这里不推进时间；真正的时间推进由 DspChartClock 完成。
    /// <summary>Values decoded from the original PlayOptionSpeedTableRecord.bin.</summary>
    public static class OriginalNoteSpeed
    {
        // The visible speed is a decimal value.  The default remains the same
        // 4.8 parameter used by the previous table. The visible control is
        // intentionally capped at 17.0; values above that make the gameplay
        // preview and settings list unnecessarily large without adding a
        // useful playable range.
        public const int DefaultOption = 48;
        public const float DefaultParameter = 4.8f;
        public const float Step = .1f;
        public const float MaximumParameter = 17f;
        public const float GlobalSpeedMultiplier = 2f;

        private static readonly string[] Labels = BuildLabels();

        public static int Count => Mathf.RoundToInt(MaximumParameter / Step) + 1;

        public static int ClampOption(int option) => Mathf.Clamp(option, 0, Count - 1);

        public static string Label(int option) => Labels[ClampOption(option)];

        public static float Parameter(int option)
        {
            option = ClampOption(option);
            return option * Step;
        }

        public static float VisualMultiplier(int option) =>
            Parameter(option) / DefaultParameter * 1.2f * GlobalSpeedMultiplier;

        public static int FromLegacyOption(int option)
        {
            option = Mathf.Clamp(option, 0, 61);
            float legacyParameter = option <= 56 ? .8f + option * .2f
                : option <= 60 ? 14f + (option - 57) * 2f : 60f;
            return ClampOption(Mathf.RoundToInt(legacyParameter / Step));
        }

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
            for (int option = 0; option < labels.Length; option++)
                labels[option] = (option * Step).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return labels;
        }
    }
}
