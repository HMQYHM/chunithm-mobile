using System;
using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>A PCM layer uses clip frames, not interleaved sample indices.
    /// All layers sum before the final limiter, preserving cancellation/order.
    /// Loop phase is anchored to the authored start and never render-frame time.</summary>
    public sealed class GameplayAudioMixLayer
    {
        private readonly float[] pcm;
        private readonly int channels, inputRate, outputRate, startFrame, endFrame;
        private readonly float volume;
        private readonly bool loop;
        public GameplayAudioMixLayer(AudioClip clip, int rate, double startMs, double endMs, bool repeats, float gain)
        {
            channels = clip.channels; inputRate = clip.frequency; outputRate = rate;
            pcm = new float[checked(clip.samples * channels)];
            if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("Unreadable PCM: " + clip.name);
            startFrame = (int)Math.Round(startMs * rate / 1000.0);
            endFrame = repeats ? (int)Math.Round(endMs * rate / 1000.0) : startFrame + (int)Math.Ceiling(clip.samples * (double)rate / inputRate);
            loop = repeats; volume = gain;
        }
        // Multiple events can share decoded PCM without allocating one entire
        // waveform per chart point. Each instance owns timing only after copying.
        public GameplayAudioMixLayer(GameplayAudioMixLayer sample, double startMs, double endMs, bool repeats, float gain)
        {
            pcm = sample.pcm; channels = sample.channels; inputRate = sample.inputRate; outputRate = sample.outputRate;
            startFrame = (int)Math.Round(startMs * outputRate / 1000.0);
            endFrame = repeats ? (int)Math.Round(endMs * outputRate / 1000.0) : startFrame + (int)Math.Ceiling(pcm.Length / channels * (double)outputRate / inputRate);
            loop = repeats; volume = gain;
        }
        public void AddTo(float[] chunk, int offsetFrame, int outputChannels)
        {
            int start = Math.Max(offsetFrame, startFrame), end = Math.Min(offsetFrame + chunk.Length / outputChannels, endFrame);
            int inputFrames = pcm.Length / channels;
            for (int frame = start; frame < end; frame++) {
                double position = (frame - (double)startFrame) * inputRate / outputRate;
                int a = (int)position; float fraction = (float)(position - a);
                a = loop ? a % inputFrames : Math.Min(a, inputFrames - 1);
                int b = loop ? (a + 1) % inputFrames : Math.Min(a + 1, inputFrames - 1);
                for (int c = 0; c < outputChannels; c++) {
                    float value;
                    if (outputChannels == 1 && channels > 1) {
                        value = 0; for (int k = 0; k < channels; k++) value += Mathf.Lerp(pcm[a*channels+k], pcm[b*channels+k], fraction) / channels;
                    } else { int k = Math.Min(c, channels-1); value = Mathf.Lerp(pcm[a*channels+k], pcm[b*channels+k], fraction); }
                    chunk[(frame-offsetFrame)*outputChannels+c] += value * volume;
                }
            }
        }
    }
}
