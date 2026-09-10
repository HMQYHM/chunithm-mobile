using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ChunithmMobile.Chart;
using ChunithmMobile.Platform;
using ChunithmMobile.Input;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Development bridge for imported, non-embedded charts. ADB can place a
    /// song folder under persistentDataPath/Songs and the first C2S is rendered
    /// with the same DSP clock that will later be owned by the song-select flow.
    /// </summary>
    public sealed class LocalChartPlaybackSession : MonoBehaviour
    {
        private readonly DspChartClock clock = new DspChartClock();
        private ChartPlaybackRenderer chartRenderer;
        private bool resumeAfterForeground;
        private AudioSource songSource;
        private AudioClip songClip;
        private AudioClip mixedSongClip;
        private ParsedChart loadedChart;
        private bool completionRaised;
        private bool calibrationPlayback;
        private RuntimeLoadingOverlay loading;
        private string lastAudioLoadError;
        public const float PreparationSeconds = 2f;
        private double preparationEndDsp;
        private float pausedPreparationRemaining;

        public DspChartClock Clock => clock;
        public bool IsPlaying => loadedChart != null && clock.Playing;
        public double PlaybackTimeMs => loadedChart == null ? 0 : Math.Max(0, clock.TimeMs);
        public double PlaybackDurationMs => loadedChart?.DurationMs ?? 0;
        public int JudgeTotal => loadedChart?.OriginalJudgeTotal ?? 0;
        public GameplayScoreState ScoreState => GetComponent<GameplayJudgeController>()?.ScoreState;
        public event Action PlaybackCompleted;

        private void Awake()
        {
            chartRenderer = GetComponent<ChartPlaybackRenderer>();
            loading = FindObjectOfType<RuntimeLoadingOverlay>();
            chartRenderer.enabled = false;
        }

        public async void PlaySong(SongEntry song, SongChartEntry selectedChart, float playbackRate = 1f,
            bool autoPlay = false, bool airAreaAuto = false, bool directionalAirAuto = false,
            bool mineImmunity = false, bool airLongNoMotion = false,
            int musicOffsetMs = 0,
            int judgementOffsetMs = 0,
            Action onReady = null, Action<string> onFailed = null, bool keepLoadingVisible = false,
            float startDelaySeconds = .2f, bool deferStart = false, bool mirrorChart = false,
            bool nonMissJusticeCritical = false)
        {
            if (song == null || selectedChart == null || !File.Exists(selectedChart.Path)) {
                onFailed?.Invoke("谱面文件不存在或歌曲数据无效");
                return;
            }
            ReleasePlayback();
            calibrationPlayback = string.Equals(selectedChart.Difficulty, "CALIBRATION", StringComparison.OrdinalIgnoreCase);
            lastAudioLoadError = null;
            Debug.Log($"Gameplay load begin: song={song.Name}, chart={selectedChart.Path}, " +
                $"difficulty={selectedChart.Difficulty}, musicOffset={musicOffsetMs}ms, " +
                $"judgeOffset={judgementOffsetMs}ms, mods=[auto={autoPlay},airAuto={airAreaAuto}," +
                $"directionalAirAuto={directionalAirAuto},mineImmunity={mineImmunity}," +
                $"airLongSimple={airLongNoMotion}], flickWindow={JudgementEngine.FlickWindowMs:F3}ms, " +
                $"memory={SystemInfo.systemMemorySize}MB, " +
                $"managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
            loading?.Show("正在读取谱面…", .04f);
            chartRenderer.enabled = false;

            try {
                ParsedChart chart = C2sParser.Parse(selectedChart.Path);
                if (mirrorChart) ChartMirror.Apply(chart);
                loadedChart = chart;
                completionRaised = false;
                clock.SetRate(playbackRate);
                chartRenderer.Bind(chart, clock);
                List<ScheduledJudgePoint> judgePoints = JudgePointScheduler.Build(chart);
                Debug.Log($"C2S parsed: notes={chart.Notes.Count}, judgePoints={judgePoints.Count}, " +
                    $"duration={chart.DurationMs:F1}ms, scrollEvents={chart.ScrollEvents.Count}, " +
                    $"mirror={mirrorChart}, memory={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                loading?.SetProgress("谱面已完成，正在准备音乐…", .15f);
                if (!await TryLoadAndStartAudio(selectedChart.Path, chart, musicOffsetMs, startDelaySeconds, false)) {
                    chartRenderer.enabled = false;
                    string message = string.IsNullOrWhiteSpace(lastAudioLoadError)
                        ? "歌曲音乐加载失败。" : lastAudioLoadError;
                    Debug.LogError($"Gameplay preparation stopped: {message}");
                    ReleasePlayback();
                    loading?.SetProgress("歌曲准备失败", 1f);
                    onFailed?.Invoke(message);
                    return;
                }
                GetComponent<GameplayJudgeController>()?.Bind(chart, clock, FindObjectOfType<ArcadeInputRig>(),
                    autoPlay, airAreaAuto, directionalAirAuto, mineImmunity, airLongNoMotion,
                    judgementOffsetMs, nonMissJusticeCritical, judgePoints);
                chartRenderer.PrepareFirstFrame(-Math.Max(0, -preparedMusicOffsetMs) * clock.Rate);
                if (!keepLoadingVisible) loading?.Hide();
                onReady?.Invoke();
                if (!deferStart) StartPreparedWithPreparation();
                Debug.Log($"Started selected song: {song.Name} / {selectedChart.Difficulty} {selectedChart.Level} | " +
                          $"{selectedChart.Path} | notes={chart.Notes.Count} | " +
                          $"globalScroll={chart.ScrollEvents.Count} | localProfiles={chart.FieldScrollProfiles.Count} | " +
                          $"assignments={chart.ScrollAssignments.Count}");
            }
            catch (Exception exception) {
                Debug.LogError("C2S playback load failed: " + selectedChart.Path);
                Debug.LogException(exception);
                chartRenderer.enabled = false;
                ReleasePlayback();
                loading?.SetProgress("歌曲准备失败", 1f);
                onFailed?.Invoke(exception.Message);
            }
        }

        private async Task<bool> TryLoadAndStartAudio(string chartPath, ParsedChart chart,
            int musicOffsetMs, float startDelaySeconds, bool startPlayback = true)
        {
            string songRoot = FindSongRoot(chartPath);
            if (string.IsNullOrWhiteSpace(songRoot)) {
                lastAudioLoadError = "找不到歌曲目录。";
                Debug.LogError("Audio preparation failed: song root is empty.");
                return false;
            }
            SongAudioSet audioFiles = SongAudioFiles.Resolve(songRoot);
            string source = audioFiles.GameplayMp3;
            if (source == null) {
                lastAudioLoadError = "歌曲目录中没有 gameplay.mp3。";
                Debug.LogError("Audio preparation failed: song has no gameplay.mp3: " + songRoot);
                return false;
            }

            try {
                int normalizedMusicOffset = Mathf.Clamp(musicOffsetMs, -500, 500);
                Debug.Log($"MP3 load begin: path={source}, musicOffset={normalizedMusicOffset}ms, " +
                    $"systemMemory={SystemInfo.systemMemorySize}MB, managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                loading?.SetProgress("正在加载 MP3 音乐…", .17f);
                string uri = new Uri(source).AbsoluteUri;
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG)) {
                    var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                    handler.streamAudio = false;
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone) {
                        loading?.SetProgress("正在加载 MP3 音乐…", .15f + Mathf.Max(0, request.downloadProgress) * .80f);
                        await Task.Yield();
                    }
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new InvalidDataException("MP3 加载失败：" + request.error);
                    songClip = DownloadHandlerAudioClip.GetContent(request);
                }
                if (songClip == null || songClip.samples <= 0) throw new InvalidDataException("MP3 音频为空");
                Debug.Log($"MP3 load complete: path={source}, samples={songClip.samples}, " +
                    $"frequency={songClip.frequency}Hz, channels={songClip.channels}, " +
                    $"length={songClip.length:F2}s, managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                loading?.SetProgress("正在按谱面合并正解音…", .96f);
                AudioClip answerClip = null;
                AppUiShell shell = FindObjectOfType<AppUiShell>();
                if (shell != null && shell.AnswerSoundEnabled && shell.AnswerSoundVolume > 0f)
                    answerClip = await LoadGameplaySkinAudioAsync("se_touch_answer.wav", AudioType.WAV);
                mixedSongClip = BuildMixedSongClip(songClip, chart, answerClip);
                if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer"))
                    Destroy(answerClip);
                if (mixedSongClip != null) {
                    // The mixed clip owns the PCM needed for playback. Release
                    // the decoded source to avoid keeping two full songs alive.
                    Destroy(songClip);
                    songClip = null;
                }
                songSource = gameObject.AddComponent<AudioSource>();
                songSource.playOnAwake = false;
                songSource.loop = false;
                songSource.clip = mixedSongClip ?? songClip;
                songSource.volume = mixedSongClip == null ?
                    (FindObjectOfType<AppUiShell>()?.GameplayMusicVolume ?? 1f) : 1f;
                // Rate conversion happens inside Unity's software DSP mixer before
                // PCM reaches Android. Do not replace this with AudioTrack,
                // PlaybackParams, SoundPool, or another device playback-rate API.
                songSource.pitch = (float)clock.Rate;
                preparedMusicOffsetMs = normalizedMusicOffset;
                if (startPlayback) SchedulePreparedPlayback(normalizedMusicOffset, startDelaySeconds);
                loading?.SetProgress(startPlayback ? "准备完成" : "谱面与音乐已准备完成", 1f);
                AudioClip scheduledClip = songSource.clip;
                AudioConfiguration activeAudio = AudioSettings.GetConfiguration();
                Debug.Log($"Audio prepared: deferred={!startPlayback}, " +
                          $"musicOffset={normalizedMusicOffset}ms, clip={(scheduledClip != null ? scheduledClip.name : "null")}, " +
                    $"volume={songSource.volume:F2}, pitch={songSource.pitch:F2}, " +
                    $"sampleRate={activeAudio.sampleRate}Hz, dspBuffer={activeAudio.dspBufferSize}, " +
                    $"managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                return true;
            }
            catch (Exception exception) {
                lastAudioLoadError = exception.Message;
                Debug.LogError("MP3 load failed; gameplay will not start: " + source);
                Debug.LogException(exception);
                if (mixedSongClip != null) Destroy(mixedSongClip);
                mixedSongClip = null;
                if (songClip != null) Destroy(songClip);
                songClip = null;
                return false;
            }
        }

        public void StartPreparedAtUnixMilliseconds(long startAtUnixMilliseconds)
        {
            if (loadedChart == null || songSource == null || songSource.clip == null) return;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float delay = Mathf.Max(.02f, (float)((startAtUnixMilliseconds - nowUnix) / 1000.0));
            SchedulePreparedPlayback(preparedMusicOffsetMs, delay);
        }

        private int preparedMusicOffsetMs;

        public void StartPreparedWithPreparation()
        {
            if (loadedChart == null || songSource == null || songSource.clip == null) return;
            SchedulePreparedPlayback(preparedMusicOffsetMs, PreparationSeconds);
        }

        private void SchedulePreparedPlayback(int musicOffsetMs, float startDelaySeconds)
        {
            preparedMusicOffsetMs = Mathf.Clamp(musicOffsetMs, -500, 500);
            double baseDsp = PlatformServices.Current.AudioTime.Seconds + Mathf.Max(.02f, startDelaySeconds);
            preparationEndDsp = baseDsp;
            double chartStartDsp = baseDsp;
            double audioStartDsp = baseDsp;
            if (preparedMusicOffsetMs >= 0) audioStartDsp += preparedMusicOffsetMs / 1000.0;
            else chartStartDsp += -preparedMusicOffsetMs / 1000.0;
            clock.PlayScheduled(chartStartDsp, 0, baseDsp);
            chartRenderer.enabled = true;
            songSource.PlayScheduled(audioStartDsp);
            Debug.Log($"Audio scheduled: preparation={startDelaySeconds:F3}s, chartDsp={chartStartDsp:F3}, audioDsp={audioStartDsp:F3}, musicOffset={preparedMusicOffsetMs}ms");
        }

        private AudioClip BuildMixedSongClip(AudioClip source, ParsedChart chart, AudioClip answer)
        {
            AppUiShell shell = FindObjectOfType<AppUiShell>();
            // The shell owns the user-facing audio toggle and volume. The
            // generated track is intentionally silent when the setting is off.
            if (answer == null || chart == null || source == null || shell == null ||
                !shell.AnswerSoundEnabled || shell.AnswerSoundVolume <= 0f)
                return null;

            try
            {
                int sourceChannels = Math.Max(1, source.channels);
                int answerChannels = Math.Max(1, answer.channels);
                int answerSampleCount = checked(answer.samples * answerChannels);
                var answerData = new float[answerSampleCount];
                if (!answer.GetData(answerData, 0))
                {
                    Debug.LogWarning("正解音合并失败：正解音没有可读 PCM 数据。");
                    return null;
                }
                var answerMono = new float[answer.samples];
                for (int frame = 0; frame < answer.samples; frame++) {
                    float sum = 0f;
                    for (int channel = 0; channel < answerChannels; channel++)
                        sum += answerData[frame * answerChannels + channel];
                    answerMono[frame] = sum / answerChannels;
                }

                List<ScheduledJudgePoint> points = JudgePointScheduler.Build(chart);
                float musicVolume = Mathf.Clamp01(shell.GameplayMusicVolume);
                float answerVolume = Mathf.Clamp01(shell.AnswerSoundVolume);
                int frequency = Math.Max(1, source.frequency);
                var answerStarts = new List<int>();
                foreach (ScheduledJudgePoint point in points)
                {
                    ChartNote note = chart.Notes[point.NoteIndex];
                    // The original runtime emits feedback only for the first
                    // judgement of a visible note. KEEP checkpoints, hidden
                    // continuation segments, mines, and visual-only objects do
                    // not create another answer sound.
                    if (!ShouldMergeAnswerSound(note, point)) continue;
                    AddAnswerStart(answerStarts, source,
                        point.TimeMs + JudgementEngine.Frames(JudgementEngine.AnswerOffsetFrames), frequency);
                }

                // The tail is a sound event, not a scoring event. The original
                // long-note lifecycle keeps its final visual tail separate from
                // KEEP checkpoints, so add one chart-timed sound without adding
                // a judgement point or changing the score total.
                foreach (ChartNote note in chart.Notes)
                {
                    if (!ShouldMergeTailAnswerSound(note)) continue;
                    AddAnswerStart(answerStarts, source,
                        note.EndTimeMs + JudgementEngine.Frames(JudgementEngine.AnswerOffsetFrames), frequency);
                }

                int droppedAnswerSounds = LimitConcurrentAnswerSounds(answerStarts, 4);

                AudioClip mixed = AudioClip.Create("Gameplay song with chart answer sounds",
                    source.samples, sourceChannels, frequency, false);
                const int chunkFrames = 32768;
                int answerFramesAtSourceRate = Mathf.CeilToInt(answer.samples *
                    (frequency / (float)Math.Max(1, answer.frequency)));
                for (int offset = 0; offset < source.samples; offset += chunkFrames) {
                    int frames = Math.Min(chunkFrames, source.samples - offset);
                    var chunk = new float[frames * sourceChannels];
                    if (!source.GetData(chunk, offset)) {
                        Destroy(mixed);
                        Debug.LogWarning("正解音合并失败：MP3 没有可读 PCM 数据。");
                        return null;
                    }
                    for (int sample = 0; sample < chunk.Length; sample++) chunk[sample] *= musicVolume;
                    int chunkEnd = offset + frames;
                    foreach (int answerStart in answerStarts) {
                        int overlapStart = Math.Max(offset, answerStart);
                        int overlapEnd = Math.Min(chunkEnd, answerStart + answerFramesAtSourceRate);
                        if (overlapStart >= overlapEnd) continue;
                        for (int sourceFrame = overlapStart; sourceFrame < overlapEnd; sourceFrame++) {
                            int answerFrame = Mathf.Clamp(Mathf.FloorToInt((sourceFrame - answerStart) *
                                (answer.frequency / (float)frequency)), 0, answerMono.Length - 1);
                            float value = answerMono[answerFrame] * answerVolume;
                            int chunkFrame = (sourceFrame - offset) * sourceChannels;
                            for (int channel = 0; channel < sourceChannels; channel++)
                                chunk[chunkFrame + channel] = Mathf.Clamp(chunk[chunkFrame + channel] + value, -1f, 1f);
                        }
                    }
                    if (!mixed.SetData(chunk, offset)) {
                        Destroy(mixed);
                        return null;
                    }
                }
                Debug.Log($"Merged chart answer sounds into gameplay audio: answerPoints={answerStarts.Count}, " +
                          $"concurrentLimit=4, droppedConcurrent={droppedAnswerSounds}, " +
                          $"samples={source.samples}, channels={sourceChannels}, chunkFrames={chunkFrames}, " +
                          $"managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                return mixed;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("正解音合并失败，将播放原歌曲：" + exception.Message);
                return null;
            }
        }

        private static async Task<AudioClip> LoadGameplaySkinAudioAsync(string fileName, AudioType type)
        {
            string path = GameplaySkinManager.GetAudioPath(fileName);
            if (string.IsNullOrWhiteSpace(path)) return Resources.Load<AudioClip>("Audio/se_touch_answer");
            try
            {
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                    new Uri(path).AbsoluteUri, type)) {
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();
                    if (request.result != UnityWebRequest.Result.Success) {
                        Debug.LogWarning("Gameplay skin answer sound failed: " + request.error);
                        return Resources.Load<AudioClip>("Audio/se_touch_answer");
                    }
                    return DownloadHandlerAudioClip.GetContent(request);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gameplay skin answer sound failed: " + exception.Message);
                return Resources.Load<AudioClip>("Audio/se_touch_answer");
            }
        }

        private static bool ShouldMergeAnswerSound(ChartNote note, ScheduledJudgePoint point)
        {
            if (note == null || point.PointIndex != 0 || !note.VisibleHead) return false;
            return note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or NoteKind.Air or NoteKind.AirHold or
                NoteKind.AirAction;
        }

        private static bool ShouldMergeTailAnswerSound(ChartNote note)
        {
            if (note == null || !note.VisibleTail || note.EndTimeMs <= note.TimeMs) return false;
            return note.Kind is NoteKind.Hold or NoteKind.Slide or NoteKind.AirHold or NoteKind.AirAction;
        }

        private static bool AddAnswerStart(List<int> starts, AudioClip source, double timeMs, int frequency)
        {
            if (timeMs < 0 || timeMs >= source.length * 1000.0) return false;
            int startFrame = Mathf.Clamp(Mathf.RoundToInt((float)(timeMs * frequency / 1000.0)), 0, source.samples - 1);
            starts.Add(startFrame);
            return true;
        }

        private static int LimitConcurrentAnswerSounds(List<int> starts, int maximumAtOneFrame)
        {
            if (starts == null || starts.Count <= maximumAtOneFrame) return 0;
            starts.Sort();
            int write = 0;
            int previousFrame = -1;
            int frameCount = 0;
            int dropped = 0;
            for (int read = 0; read < starts.Count; read++)
            {
                int frame = starts[read];
                if (frame != previousFrame) {
                    previousFrame = frame;
                    frameCount = 0;
                }
                if (frameCount >= maximumAtOneFrame) {
                    dropped++;
                    continue;
                }
                starts[write++] = frame;
                frameCount++;
            }
            if (write < starts.Count) starts.RemoveRange(write, starts.Count - write);
            return dropped;
        }

        private static string FindSongRoot(string chartPath)
        {
            DirectoryInfo directory = Directory.GetParent(chartPath);
            for (int depth = 0; directory != null && depth < 4; depth++, directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "audio")))
                    return directory.FullName;
            }
            return Directory.GetParent(chartPath)?.FullName;
        }

        private void Update()
        {
            if (loadedChart == null || !clock.Playing || completionRaised) return;
            if (clock.TimeMs < loadedChart.DurationMs + (calibrationPlayback ? 0 : 1000)) return;
            completionRaised = true;
            if (songSource != null) songSource.Stop();
            Debug.Log("Chart playback completed.");
            PlaybackCompleted?.Invoke();
        }

        public void PausePlayback()
        {
            if (loadedChart == null || !clock.Playing) return;
            pausedPreparationRemaining = clock.Preparing
                ? (float)Math.Max(0, preparationEndDsp - PlatformServices.Current.AudioTime.Seconds) : 0;
            if (songSource != null) {
                if (pausedPreparationRemaining > 0) songSource.Stop();
                else songSource.Pause();
            }
            clock.Pause();
        }

        public void ResumePlayback()
        {
            if (loadedChart == null || clock.Playing) return;
            if (pausedPreparationRemaining > 0) {
                SchedulePreparedPlayback(preparedMusicOffsetMs, pausedPreparationRemaining);
                pausedPreparationRemaining = 0;
                return;
            }
            clock.Resume();
            if (songSource != null) songSource.UnPause();
        }

        public void StopPlayback()
        {
            completionRaised = true;
            ReleasePlayback();
        }

        private void OnDestroy()
        {
            ReleasePlayback();
        }

        private void ReleasePlayback()
        {
            pausedPreparationRemaining = 0;
            preparationEndDsp = 0;
            clock.Pause();
            if (songSource != null) songSource.Stop();
            if (mixedSongClip != null) Destroy(mixedSongClip);
            if (songClip != null) Destroy(songClip);
            if (songSource != null) Destroy(songSource);
            if (chartRenderer != null) chartRenderer.Clear();
            songSource = null;
            mixedSongClip = null;
            songClip = null;
            loadedChart = null;
            calibrationPlayback = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) {
                resumeAfterForeground = clock.Playing;
                PausePlayback();
            } else if (resumeAfterForeground) {
                // The final UI inserts its three-second resume preparation here.
                ResumePlayback();
                resumeAfterForeground = false;
            }
        }
    }
}
