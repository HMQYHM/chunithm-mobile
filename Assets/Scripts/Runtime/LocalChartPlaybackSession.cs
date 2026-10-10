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
    // 单局播放会话：串联谱面解析、音频准备、正解音合成、时钟、渲染器、判定器和暂停恢复。
    // 每次加载都有 generation；旧异步任务返回时不得修改当前会话，这是防止联机卡首帧和串歌的关键。
    /// <summary>
    /// Development bridge for imported, non-embedded charts. ADB can place a
    /// song folder under persistentDataPath/Songs and the first C2S is rendered
    /// with the same DSP clock that will later be owned by the song-select flow.
    /// </summary>
    public sealed class LocalChartPlaybackSession : MonoBehaviour
    {
        private readonly DspChartClock clock = new DspChartClock();
        private ChartPlaybackRenderer chartRenderer;
        private bool pauseRequested;
        private bool applicationPaused;
        private bool applicationFocused = true;
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
        private bool audioRouteRebindPending;
        // Every load gets a generation. Async file/audio continuations from an
        // older load must never mutate the current gameplay session.
        private int playbackGeneration;

        public DspChartClock Clock => clock;
        public bool IsPlaying => loadedChart != null && clock.Playing;
        public bool IsPrepared => loadedChart != null && songSource != null && songSource.clip != null;
        public double PlaybackTimeMs => loadedChart == null ? 0 : Math.Max(0, clock.TimeMs);
        public double PlaybackDurationMs => loadedChart?.DurationMs ?? 0;
        public int JudgeTotal => loadedChart?.OriginalJudgeTotal ?? 0;
        public GameplayScoreState ScoreState => GetComponent<GameplayJudgeController>()?.ScoreState;
        public event Action PlaybackCompleted;
        public event Action PlaybackPaused;

        // Unity 生命周期函数：这里只获取组件和加载遮罩，不提前加载歌曲。
        // Awake 可能早于 AppUiShell 恢复设置，因此资源准备必须从 PlaySong 开始。
        private void Awake()
        {
            chartRenderer = GetComponent<ChartPlaybackRenderer>();
            loading = FindObjectOfType<RuntimeLoadingOverlay>();
            chartRenderer.enabled = false;
        }

        /// <summary>
        /// 异步准备一局歌曲。generation 拒绝旧的文件读取/音频解码回调；onReady 仅表示
        /// 组件已绑定，不代表播放已经开始。实际的播放起点由本类统一安排。
        /// </summary>
        public async void PlaySong(SongEntry song, SongChartEntry selectedChart, float playbackRate = 1f,
            bool autoPlay = false, bool airAreaAuto = false, bool directionalAirAuto = false,
            bool mineImmunity = false, bool airLongNoMotion = false,
            int musicOffsetMs = 0,
            int judgementOffsetMs = 0,
            Action onReady = null, Action<string> onFailed = null, bool keepLoadingVisible = false,
            float startDelaySeconds = .2f, bool deferStart = false, bool mirrorChart = false,
            bool nonMissJusticeCritical = false, bool flickAllTap = false,
            bool uniformSpeed = false)
        {
            if (song == null || selectedChart == null || !File.Exists(selectedChart.Path)) {
                onFailed?.Invoke("谱面文件不存在或歌曲数据无效");
                return;
            }
            // 先递增代次再释放旧会话。每个 await 返回后都会检查代次，防止旧任务写回新页面。
            int generation = ++playbackGeneration;
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

            string loadStage = "parse";
            try {
                // 后续渲染、判定和正解音合成都使用这一份解析结果，保证时间和判定点一致。
                ParsedChart chart = C2sParser.Parse(selectedChart.Path);
                loadStage = "bind-renderer";
                if (mirrorChart) ChartMirror.Apply(chart);
                loadedChart = chart;
                completionRaised = false;
                clock.SetRate(playbackRate);
                chartRenderer.Bind(chart, clock, uniformSpeed);
                List<ScheduledJudgePoint> judgePoints = JudgePointScheduler.Build(chart);
                Debug.Log($"C2S parsed: notes={chart.Notes.Count}, judgePoints={judgePoints.Count}, " +
                    $"declaredJudgePoints={chart.DeclaredJudgeTotal}, " +
                    $"duration={chart.DurationMs:F1}ms, scrollEvents={chart.ScrollEvents.Count}, " +
                    $"mirror={mirrorChart}, memory={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                if (chart.DeclaredJudgeTotal != judgePoints.Count)
                    Debug.LogWarning($"C2S T_JUDGE_ALL mismatch: declared={chart.DeclaredJudgeTotal}, " +
                        $"scheduled={judgePoints.Count}, chart={selectedChart.Path}. " +
                        "Gameplay will use the scheduled total instead of fabricating points.");
                loading?.SetProgress("谱面已完成，正在准备音乐…", .15f);
                loadStage = "load-audio";
                Task<bool> audioPreparation = TryLoadAndStartAudio(selectedChart.Path, chart, musicOffsetMs,
                    startDelaySeconds, false, generation);
                Task completedPreparation = await Task.WhenAny(audioPreparation, Task.Delay(30000));
                if (completedPreparation != audioPreparation) {
                    if (!IsCurrentGeneration(generation)) return;
                    playbackGeneration++;
                    Debug.LogError("Gameplay preparation timed out while loading audio.");
                    MultiplayerLog.Error("gameplay-load-timeout",
                        $"stage={loadStage} chart={selectedChart.Path} song={song.Name}");
                    ReleasePlayback();
                    loading?.SetProgress("歌曲准备超时", 1f);
                    onFailed?.Invoke("歌曲资源准备超时，请重新尝试。");
                    return;
                }
                if (!await audioPreparation) {
                    if (!IsCurrentGeneration(generation)) return;
                    chartRenderer.enabled = false;
                    string message = string.IsNullOrWhiteSpace(lastAudioLoadError)
                        ? "歌曲音乐加载失败。" : lastAudioLoadError;
                    Debug.LogError($"Gameplay preparation stopped: {message}");
                    MultiplayerLog.Error("gameplay-audio-failed",
                        $"stage={loadStage} chart={selectedChart.Path} song={song.Name} reason={message}");
                    ReleasePlayback();
                    loading?.SetProgress("歌曲准备失败", 1f);
                    onFailed?.Invoke(message);
                    return;
                }
                if (!IsCurrentGeneration(generation)) return;
                // 音频准备成功后才绑定判定器，避免加载页阶段出现后台时间轴和无输入判定。
                loadStage = "bind-judgement";
                GetComponent<GameplayJudgeController>()?.Bind(chart, clock, FindObjectOfType<ArcadeInputRig>(),
                    autoPlay, airAreaAuto, directionalAirAuto, mineImmunity, airLongNoMotion,
                    judgementOffsetMs, nonMissJusticeCritical, judgePoints, flickAllTap);
                Camera gameplayCamera = transform.parent != null
                    ? transform.parent.Find("Gameplay Camera")?.GetComponent<Camera>() : null;
                if (gameplayCamera == null || !gameplayCamera.isActiveAndEnabled)
                    throw new InvalidOperationException("Gameplay camera is not ready for first-frame preparation.");
                loading?.SetProgress("正在准备首帧画面…", .99f);
                loadStage = "prepare-first-frame";
                // 首帧预热只创建/更新视觉对象，不推进时钟；页面准备完成后才允许正式启动。
                chartRenderer.PrepareFirstFrame(-Math.Max(0, -preparedMusicOffsetMs) * clock.Rate, gameplayCamera);
                if (!keepLoadingVisible) loading?.Hide();
                onReady?.Invoke();
                // 联机可以 deferStart，等待服务器统一起点；单机直接进入本地预备阶段。
                if (!deferStart) StartPreparedWithPreparation();
                Debug.Log($"Started selected song: {song.Name} / {selectedChart.Difficulty} {selectedChart.Level} | " +
                          $"{selectedChart.Path} | notes={chart.Notes.Count} | " +
                          $"globalScroll={chart.ScrollEvents.Count} | localProfiles={chart.FieldScrollProfiles.Count} | " +
                          $"assignments={chart.ScrollAssignments.Count}");
            }
            catch (Exception exception) {
                if (!IsCurrentGeneration(generation)) return;
                Debug.LogError("C2S playback load failed: " + selectedChart.Path);
                Debug.LogException(exception);
                MultiplayerLog.Error("gameplay-load-failed",
                    $"stage={loadStage} chart={selectedChart.Path} song={song.Name} " +
                    $"difficulty={selectedChart.Difficulty} type={exception.GetType().FullName} " +
                    $"message={exception.Message} stack={exception}");
                chartRenderer.enabled = false;
                ReleasePlayback();
                loading?.SetProgress("歌曲准备失败", 1f);
                onFailed?.Invoke(exception.Message);
            }
        }

        private bool IsCurrentGeneration(int generation) => generation == playbackGeneration;

        private async Task<bool> TryLoadAndStartAudio(string chartPath, ParsedChart chart,
            int musicOffsetMs, float startDelaySeconds, bool startPlayback = true, int generation = -1)
        {
            string songRoot = FindSongRoot(chartPath);
            if (string.IsNullOrWhiteSpace(songRoot)) {
                lastAudioLoadError = "找不到歌曲目录。";
                Debug.LogError("Audio preparation failed: song root is empty.");
                MultiplayerLog.Error("gameplay-audio-failed", $"stage=find-song-root chart={chartPath}");
                return false;
            }
            SongAudioSet audioFiles = SongAudioFiles.Resolve(songRoot);
            string source = audioFiles.Gameplay;
            if (source == null) {
                lastAudioLoadError = "歌曲目录中没有可播放的 gameplay.mp3 或 gameplay.wav。";
                Debug.LogError("Audio preparation failed: song has no playable gameplay audio: " + songRoot);
                MultiplayerLog.Error("gameplay-audio-failed",
                    $"stage=resolve-audio chart={chartPath} root={songRoot} reason=no-playable-gameplay-audio");
                return false;
            }

            AudioClip decodedClip = null;
            AudioClip preparedMixedClip = null;
            try {
                int normalizedMusicOffset = Mathf.Clamp(musicOffsetMs, -500, 500);
                AudioType audioType = SongAudioSet.GetUnityAudioType(source);
                string formatName = audioType == AudioType.WAV ? "WAV" : "MP3";
                Debug.Log($"{formatName} load begin: path={source}, musicOffset={normalizedMusicOffset}ms, " +
                    $"systemMemory={SystemInfo.systemMemorySize}MB, managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                loading?.SetProgress("正在加载 " + formatName + " 音乐…", .17f);
                string uri = new Uri(source).AbsoluteUri;
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType)) {
                    var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                    handler.streamAudio = false;
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone) {
                        loading?.SetProgress("正在加载 " + formatName + " 音乐…", .15f + Mathf.Max(0, request.downloadProgress) * .80f);
                        await Task.Yield();
                    }
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new InvalidDataException(formatName + " 加载失败：" + request.error);
                    decodedClip = DownloadHandlerAudioClip.GetContent(request);
                    if (generation >= 0 && !IsCurrentGeneration(generation)) {
                        if (decodedClip != null) Destroy(decodedClip);
                        return false;
                    }
                }
                if (decodedClip == null || decodedClip.samples <= 0) throw new InvalidDataException(formatName + " 音频为空");
                Debug.Log($"{formatName} load complete: path={source}, samples={decodedClip.samples}, " +
                    $"frequency={decodedClip.frequency}Hz, channels={decodedClip.channels}, " +
                    $"length={decodedClip.length:F2}s, managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
                loading?.SetProgress("正在按谱面合并正解音…", .96f);
                AudioClip answerClip = null;
                AppUiShell shell = FindObjectOfType<AppUiShell>();
                if (shell != null && shell.AnswerSoundEnabled && shell.AnswerSoundVolume > 0f)
                    answerClip = await LoadGameplaySkinAudioAsync("se_touch_answer.wav", AudioType.WAV);
                if (generation >= 0 && !IsCurrentGeneration(generation)) {
                    if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer"))
                        Destroy(answerClip);
                    if (decodedClip != null) Destroy(decodedClip);
                    return false;
                }
                preparedMixedClip = BuildMixedSongClip(decodedClip, chart, answerClip);
                if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer"))
                    Destroy(answerClip);
                if (generation >= 0 && !IsCurrentGeneration(generation)) {
                    if (preparedMixedClip != null) Destroy(preparedMixedClip);
                    if (decodedClip != null) Destroy(decodedClip);
                    return false;
                }
                songClip = decodedClip;
                mixedSongClip = preparedMixedClip;
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
                if (generation >= 0 && !IsCurrentGeneration(generation)) {
                    if (preparedMixedClip != null) Destroy(preparedMixedClip);
                    if (decodedClip != null) Destroy(decodedClip);
                    return false;
                }
                lastAudioLoadError = exception.Message;
                Debug.LogError("MP3 load failed; gameplay will not start: " + source);
                Debug.LogException(exception);
                if (preparedMixedClip != null && preparedMixedClip != mixedSongClip) Destroy(preparedMixedClip);
                if (decodedClip != null && decodedClip != songClip) Destroy(decodedClip);
                if (mixedSongClip != null) Destroy(mixedSongClip);
                mixedSongClip = null;
                if (songClip != null) Destroy(songClip);
                songClip = null;
                return false;
            }
        }

        public bool StartPreparedAtUnixMilliseconds(long startAtUnixMilliseconds)
        {
            if (!IsPrepared) return false;
            if (clock.Playing) return true;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float delay = Mathf.Max(.02f, (float)((startAtUnixMilliseconds - nowUnix) / 1000.0));
            SchedulePreparedPlayback(preparedMusicOffsetMs, delay);
            return true;
        }

        /// <summary>
        /// Starts an already prepared round at an absolute value from the same
        /// monotonic DSP clock used by AudioSource.PlayScheduled and judgement.
        /// This is the multiplayer path and is independent of wall-clock jumps.
        /// </summary>
        public bool StartPreparedAtDspTime(double startAtDspSeconds)
        {
            if (!IsPrepared) return false;
            if (clock.Playing) return true;
            SchedulePreparedPlaybackAtDsp(preparedMusicOffsetMs,
                Math.Max(PlatformServices.Current.AudioTime.Seconds + .02, startAtDspSeconds));
            return true;
        }

        private int preparedMusicOffsetMs;

        public void StartPreparedWithPreparation()
        {
            if (loadedChart == null || songSource == null || songSource.clip == null) return;
            SchedulePreparedPlayback(preparedMusicOffsetMs, PreparationSeconds);
        }

        private void SchedulePreparedPlayback(int musicOffsetMs, float startDelaySeconds)
        {
            double baseDsp = PlatformServices.Current.AudioTime.Seconds + Mathf.Max(.02f, startDelaySeconds);
            SchedulePreparedPlaybackAtDsp(musicOffsetMs, baseDsp);
        }

        private void SchedulePreparedPlaybackAtDsp(int musicOffsetMs, double baseDsp)
        {
            preparedMusicOffsetMs = Mathf.Clamp(musicOffsetMs, -500, 500);
            preparationEndDsp = baseDsp;
            double chartStartDsp = baseDsp;
            double audioStartDsp = baseDsp;
            if (preparedMusicOffsetMs >= 0) audioStartDsp += preparedMusicOffsetMs / 1000.0;
            else chartStartDsp += -preparedMusicOffsetMs / 1000.0;
            clock.PlayScheduled(chartStartDsp, 0, baseDsp);
            chartRenderer.enabled = true;
            // Chart/input use the interpolated audio-domain clock, while
            // Unity's scheduler requires the backend's raw DSP epoch.
            double rawAudioStartDsp = PlatformServices.Current.AudioTime.ToRawSeconds(audioStartDsp);
            songSource.PlayScheduled(rawAudioStartDsp);
            if (pauseRequested || applicationPaused || !applicationFocused) PausePlayback();
            Debug.Log($"Audio scheduled: remaining={Math.Max(0, baseDsp - PlatformServices.Current.AudioTime.Seconds):F3}s, " +
                      $"chartDsp={chartStartDsp:F3}, audioClockDsp={audioStartDsp:F3}, " +
                      $"rawAudioDsp={rawAudioStartDsp:F3}, musicOffset={preparedMusicOffsetMs}ms");
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
                int answerOffsetMs = shell.AnswerSoundOffsetMs;
                int frequency = Math.Max(1, source.frequency);
                var answerStarts = new List<int>();
                foreach (ScheduledJudgePoint point in points)
                {
                    ChartNote note = chart.Notes[point.NoteIndex];
                    // One chart-timed cue per visible head, including mines.
                    // KEEP checkpoints and hidden continuations do not add cues.
                    if (!ShouldMergeAnswerSound(note, point)) continue;
                    AddAnswerStart(answerStarts, source,
                        point.TimeMs + JudgementEngine.Frames(JudgementEngine.AnswerOffsetFrames) + answerOffsetMs, frequency);
                }

                // The tail is a sound event, not a scoring event. The original
                // long-note lifecycle keeps its final visual tail separate from
                // KEEP checkpoints, so add one chart-timed sound without adding
                // a judgement point or changing the score total.
                foreach (ChartNote note in chart.Notes)
                {
                    if (!ShouldMergeTailAnswerSound(note)) continue;
                    AddAnswerStart(answerStarts, source,
                        note.EndTimeMs + JudgementEngine.Frames(JudgementEngine.AnswerOffsetFrames) + answerOffsetMs, frequency);
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
                NoteKind.Mine or NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or NoteKind.Air or NoteKind.AirHold or
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
            // The chart can legitimately end before the audio (for example,
            // when the last note has a shorter tail than the song).  Ending
            // from chart duration alone would open the result page while the
            // song is still audible.  Convert the clip's real playback length
            // into chart-clock time, including pitch/rate and the configured
            // music offset, then wait two real seconds after the later end.
            double playbackEndMs = loadedChart.DurationMs;
            if (songSource != null && songSource.clip != null)
            {
                double pitch = Math.Max(.01, Math.Abs(songSource.pitch));
                double audioDurationMs = songSource.clip.length * 1000.0 / pitch;
                double audioEndChartMs = (audioDurationMs + preparedMusicOffsetMs) * clock.Rate;
                playbackEndMs = Math.Max(playbackEndMs, audioEndChartMs);
            }
            double completionDelayMs = calibrationPlayback ? 0 : 2000.0 * clock.Rate;
            if (clock.TimeMs < playbackEndMs + completionDelayMs) return;
            completionRaised = true;
            if (songSource != null) songSource.Stop();
            Debug.Log($"Chart playback completed: chartEnd={loadedChart.DurationMs:F1}ms, " +
                $"audioEnd={playbackEndMs:F1}ms, delay={completionDelayMs:F1}ms, rate={clock.Rate:F2}");
            PlaybackCompleted?.Invoke();
        }

        public void PausePlayback()
        {
            pauseRequested = true;
            PlaybackPaused?.Invoke();
            if (loadedChart == null || !clock.Playing) return;
            pausedPreparationRemaining = clock.Preparing
                ? (float)Math.Max(0, preparationEndDsp - PlatformServices.Current.AudioTime.Seconds) : 0;
            if (songSource != null) {
                if (pausedPreparationRemaining > 0) songSource.Stop();
                else songSource.Pause();
            }
            clock.Pause();
        }

        /// <summary>
        /// Bluetooth route changes can invalidate the native AudioSource
        /// cursor while Unity recreates the output device. Pause the chart at
        /// its DSP position first; LowLatencyAudioController calls
        /// RebindAudioOutput after the route has remained stable.
        /// </summary>
        public void NotifyAudioRouteChanged()
        {
            if (loadedChart == null || songSource == null) return;
            audioRouteRebindPending = true;
            PausePlayback();
        }

        public void RebindAudioOutput()
        {
            if (!audioRouteRebindPending || loadedChart == null || songSource == null || songSource.clip == null)
                return;
            double chartTimeMs = Math.Max(0, clock.TimeMs);
            int frequency = Math.Max(1, songSource.clip.frequency);
            double audioTimeMs = Math.Max(0, chartTimeMs - preparedMusicOffsetMs);
            int sample = Mathf.Clamp(Mathf.RoundToInt((float)(audioTimeMs * frequency / 1000.0)),
                0, Math.Max(0, songSource.clip.samples - 1));
            // Keep the source paused while writing timeSamples. Stopping it
            // would make AudioSource.UnPause a no-op on several Android
            // Bluetooth backends, forcing the next resume to restart audio.
            songSource.Pause();
            songSource.timeSamples = sample;
            audioRouteRebindPending = false;
            Debug.Log($"Audio route rebound while paused: chart={chartTimeMs:F1}ms, " +
                      $"audio={audioTimeMs:F1}ms, sample={sample}, frequency={frequency}Hz");
        }

        public void ResumePlayback()
        {
            if (applicationPaused || !applicationFocused) return;
            pauseRequested = false;
            if (loadedChart == null || clock.Playing) return;
            if (audioRouteRebindPending) RebindAudioOutput();
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
            playbackGeneration++;
            completionRaised = true;
            ReleasePlayback();
        }

        private void OnDestroy()
        {
            ReleasePlayback();
        }

        private void ReleasePlayback()
        {
            pauseRequested = applicationPaused || !applicationFocused;
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
            audioRouteRebindPending = false;
            loadedChart = null;
            calibrationPlayback = false;
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) PausePlayback();
        }

        private void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            if (!focused) PausePlayback();
        }
    }
}
