using UnityEngine;

namespace ChunithmMobile.Runtime
{
    /// <summary>Applies the user-selected Unity software DSP buffer before gameplay audio starts.</summary>
    public sealed class LowLatencyAudioController : MonoBehaviour
    {
        private const int DefaultDspBufferSamples = 512;

        private void Awake()
        {
            Apply(AppSettings.Load().DspBufferSize);
        }

        public static void Apply(int requestedSamples)
        {
            int requested = AppSettings.NormalizeDspBufferSize(
                requestedSamples <= 0 ? DefaultDspBufferSamples : requestedSamples);
            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            if (configuration.dspBufferSize != requested) {
                configuration.dspBufferSize = requested;
                if (!AudioSettings.Reset(configuration))
                    Debug.LogWarning($"Device rejected the {requested}-sample DSP buffer; " +
                                     "continuing with its current audio configuration.");
            }

            AudioConfiguration active = AudioSettings.GetConfiguration();
            double bufferMs = active.sampleRate > 0
                ? active.dspBufferSize * 1000.0 / active.sampleRate
                : 0;
            Debug.Log($"Audio DSP configured: requested={requested}, actual={active.sampleRate}Hz, " +
                      $"buffer={active.dspBufferSize} samples ({bufferMs:F2}ms), " +
                      $"speakers={active.speakerMode}");
        }
    }
}
