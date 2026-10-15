using UnityEngine;
using System.Collections;

namespace ChunithmMobile.Runtime
{
    // 低延迟音频配置入口：负责读取/应用 DSP buffer 并触发必要的界面音乐重播。
    // 不使用 Android 硬件倍速 API；播放速率由 Unity 软件 DSP 与谱面时钟共同控制。
    /// <summary>Applies the user-selected Unity software DSP buffer before gameplay audio starts.</summary>
    public sealed class LowLatencyAudioController : MonoBehaviour
    {
        private const int DefaultDspBufferSamples = 512;
        private AudioConfiguration observedConfiguration;
        private Coroutine routeStabilization;

        private void Awake()
        {
            Apply(AppSettings.Load().DspBufferSize);
            observedConfiguration = AudioSettings.GetConfiguration();
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
            if (routeStabilization != null) StopCoroutine(routeStabilization);
        }

        private void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            AudioConfiguration active = AudioSettings.GetConfiguration();
            Debug.Log($"Audio output configuration changed: deviceChanged={deviceWasChanged}, " +
                      $"sampleRate={observedConfiguration.sampleRate}->{active.sampleRate}Hz, " +
                      $"dspBuffer={observedConfiguration.dspBufferSize}->{active.dspBufferSize}, " +
                      $"speakers={observedConfiguration.speakerMode}->{active.speakerMode}");
            observedConfiguration = active;

            MobileAudioDiagnostics.Report(active.dspBufferSize, active.sampleRate);
            if (!deviceWasChanged) return;
            LocalChartPlaybackSession session = FindObjectOfType<LocalChartPlaybackSession>();
            if (session == null || !session.IsPlaying) return;

            // Connecting/disconnecting Bluetooth destroys and recreates the
            // Android output route. Continuing with the old scheduled source
            // can produce a visible chart jump even when the new route starts
            // successfully, so pause at the last valid chart position. The
            // session's PlaybackPaused event opens the existing pause panel;
            // the player resumes explicitly after the route is stable.
            Debug.LogWarning("Audio output device changed during gameplay; pausing and waiting for the route to stabilize.");
            session.NotifyAudioRouteChanged();
            if (routeStabilization != null) StopCoroutine(routeStabilization);
            routeStabilization = StartCoroutine(RebindAfterRouteStabilizes(session));
        }

        private IEnumerator RebindAfterRouteStabilizes(LocalChartPlaybackSession session)
        {
            // Android may emit more than one configuration callback while a
            // Bluetooth profile switches sample rate. Keep the chart paused
            // until three short observations have completed, then repair the
            // AudioSource cursor without auto-resuming the user's round.
            for (int sample = 0; sample < 3; sample++)
                yield return new WaitForSecondsRealtime(.08f);
            if (session != null) session.RebindAudioOutput();
            routeStabilization = null;
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
            MobileAudioDiagnostics.Report(active.dspBufferSize, active.sampleRate);
            Debug.Log($"Audio DSP configured: requested={requested}, actual={active.sampleRate}Hz, " +
                      $"buffer={active.dspBufferSize} samples ({bufferMs:F2}ms), " +
                      $"speakers={active.speakerMode}");
        }
    }
}
