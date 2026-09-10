using System;
using ChunithmMobile.Platform;
using UnityEngine;

namespace ChunithmMobile.Timing
{
    /// <summary>Monotonic chart clock referenced to Unity's audio DSP clock.</summary>
    public sealed class DspChartClock
    {
        private double anchorDsp;
        private double anchorChartMs;
        private double rate = 1;
        private bool playing;
        private double preparationEndDsp = double.NegativeInfinity;
        private double pausedPreparationSeconds;
        private readonly IAudioTimeSource timeSource;

        public DspChartClock(IAudioTimeSource timeSource = null)
        {
            this.timeSource = timeSource ?? PlatformServices.Current.AudioTime;
        }

        public double TimeMs { get { return playing ? anchorChartMs + (Math.Max(timeSource.Seconds, preparationEndDsp) - anchorDsp) * 1000.0 * rate : anchorChartMs; } }
        public bool Preparing => playing ? timeSource.Seconds < preparationEndDsp : pausedPreparationSeconds > 0;
        public double Rate { get { return rate; } }
        public bool Playing { get { return playing; } }

        /// <summary>
        /// Converts an input sample stamped with the same monotonic audio clock
        /// into chart time. Judgement must use this timestamp instead of the
        /// next rendered frame's TimeMs, otherwise 60/90/120/144 Hz devices
        /// receive different deltas for the same physical touch.
        /// </summary>
        public double AudioTimeMsToChartTimeMs(double audioTimeMs)
        {
            return anchorChartMs + (audioTimeMs * .001 - anchorDsp) * 1000.0 * rate;
        }

        public void Play(double chartTimeMs = 0)
        {
            preparationEndDsp = double.NegativeInfinity;
            pausedPreparationSeconds = 0;
            anchorChartMs = chartTimeMs;
            anchorDsp = timeSource.Seconds;
            playing = true;
        }

        public void PlayScheduled(double audioDspStartSeconds, double chartTimeAtAudioStartMs = 0,
            double preparationUntilDsp = double.NegativeInfinity)
        {
            preparationEndDsp = preparationUntilDsp;
            pausedPreparationSeconds = 0;
            anchorChartMs = chartTimeAtAudioStartMs;
            anchorDsp = audioDspStartSeconds;
            playing = true;
        }

        public void Pause()
        {
            if (!playing) return;
            pausedPreparationSeconds = Math.Max(0, preparationEndDsp - timeSource.Seconds);
            anchorChartMs = TimeMs;
            playing = false;
        }

        public void Resume()
        {
            if (playing) return;
            anchorDsp = timeSource.Seconds + pausedPreparationSeconds;
            preparationEndDsp = pausedPreparationSeconds > 0 ? anchorDsp : double.NegativeInfinity;
            pausedPreparationSeconds = 0;
            playing = true;
        }

        public void Seek(double chartTimeMs)
        {
            anchorChartMs = Math.Max(0, chartTimeMs);
            anchorDsp = timeSource.Seconds;
        }

        public void SetRate(double newRate)
        {
            double now = TimeMs;
            rate = Math.Max(.1, Math.Min(2, newRate));
            anchorChartMs = now;
            anchorDsp = timeSource.Seconds;
        }
    }
}
