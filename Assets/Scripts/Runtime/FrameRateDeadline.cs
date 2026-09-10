using System;

namespace ChunithmMobile.Runtime
{
    /// <summary>Absolute monotonic frame deadlines, retaining fractional refresh phase.</summary>
    public sealed class FrameRateDeadline
    {
        private double next;
        private int rate;
        private bool initialized;

        public void Reset() { initialized = false; }

        public double Next(double now, int framesPerSecond)
        {
            if (framesPerSecond <= 0) { Reset(); return now; }
            double period = 1d / framesPerSecond;
            if (!initialized || rate != framesPerSecond || now > next + period) {
                next = now;
                rate = framesPerSecond;
                initialized = true;
            }
            double due = Math.Max(now, next);
            next += period;
            return due;
        }
    }
}
