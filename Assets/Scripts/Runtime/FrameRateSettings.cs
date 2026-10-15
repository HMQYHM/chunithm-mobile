using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Shared frame pacing policy for the interface and active gameplay.
    /// A limit of zero means that Unity should follow the display refresh rate
    /// without an application cap. Android interprets -1 inconsistently, so
    /// the uncapped value intentionally uses int.MaxValue.
    /// </summary>
    public static class FrameRateSettings
    {
        public static readonly int[] Options = { 60, 90, 120, 144, 165, 240, 0 };

        public static int NormalizeLimit(int value)
        {
            for (int i = 0; i < Options.Length; i++)
                if (Options[i] == value) return value;
            return 0;
        }

        public static string Label(int value)
        {
            return NormalizeLimit(value) == 0 ? UiLocalization.Translate("无上限") : NormalizeLimit(value) + " FPS";
        }

        public static int Step(int current, int direction)
        {
            int normalized = NormalizeLimit(current);
            int index = 0;
            for (int i = 0; i < Options.Length; i++)
                if (Options[i] == normalized) { index = i; break; }
            index = Mathf.Clamp(index + (direction < 0 ? -1 : 1), 0, Options.Length - 1);
            return Options[index];
        }

        /// <summary>
        /// Leaves menus, settings, song selection and result pages uncapped.
        /// Vertical sync is disabled here as well; otherwise a desktop build
        /// could still be limited by the display refresh interval while the
        /// user is outside gameplay.
        /// </summary>
        public static void ApplyInterfaceFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = int.MaxValue;
        }

        /// <summary>
        /// Applies the saved cap only while a chart is actually playing.
        /// This changes Unity's scheduling target and never changes the
        /// Android/iOS display refresh rate or system settings.
        /// </summary>
        public static void ApplyGameplayFrameRate(AppSettings settings)
        {
            if (settings == null) return;
            settings.FrameRateLimit = NormalizeLimit(settings.FrameRateLimit);
            // Desktop honors vSyncCount; Android/iOS schedule frames through
            // the OS and ignore it. Their pacing uses targetFrameRate and is
            // limited to refresh rates supported by the active display.
            QualitySettings.vSyncCount = settings.VerticalSync ? 1 : 0;
            Application.targetFrameRate = settings.FrameRateLimit == 0
                ? int.MaxValue : settings.FrameRateLimit;
        }

        // Compatibility entry point for older callers. Runtime UI code should
        // use ApplyInterfaceFrameRate; gameplay scheduling uses the explicit
        // ApplyGameplayFrameRate method above.
        public static void Apply(AppSettings settings)
        {
            ApplyInterfaceFrameRate();
        }
    }
}
