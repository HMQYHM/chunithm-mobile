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
        // 原版 ANSWER 队列有三类会实际触发 SE_TOUCH_ANSWER 的事件：
        // 1=普通头部，2=长条尾/有效滑条节点，3=AIR 动作/阶梯节点。
        // 合成到单轨时仍需保留类别，因为原版按类别各播放一次；同类同刻事件只合并一次。
        private enum AnswerSoundCategory { Head = 1, LongNode = 2, AirAction = 3 }

        private readonly struct AnswerSoundEvent
        {
            public readonly int StartFrame;
            public readonly AnswerSoundCategory Category;

            public AnswerSoundEvent(int startFrame, AnswerSoundCategory category)
            {
                StartFrame = startFrame;
                Category = category;
            }
        }

        private readonly DspChartClock clock = new DspChartClock();
        private ChartPlaybackRenderer chartRenderer;
        private bool pauseRequested;
        private bool applicationPaused;
        private bool applicationFocused = true;
        private AudioSource songSource;
        private AudioClip songClip;
        private AudioClip mixedSongClip;
        private ParsedChart loadedChart;
        private bool fullAutoAudio;
        private JudgementAudioBank judgementAudioBank;
        private GameplayJudgementAudio judgementAudio;

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
            fullAutoAudio = autoPlay;
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
            JudgementAudioBank preparedBank = null;
            AudioClip answerClip = null;
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
                AppUiShell shell = FindObjectOfType<AppUiShell>();
                if (shell != null && shell.JudgementSoundEnabled && shell.JudgementSoundVolume > 0f) {
                    preparedBank = new JudgementAudioBank();
                    await preparedBank.LoadAsync();
                }
                if (shell != null && shell.AnswerSoundEnabled && shell.AnswerSoundVolume > 0f)
                    answerClip = await LoadGameplaySkinAudioAsync("se_touch_answer.wav", AudioType.WAV);
                if (generation >= 0 && !IsCurrentGeneration(generation)) {
                    preparedBank?.Dispose();
                    if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer"))
                        Destroy(answerClip);
                    if (decodedClip != null) Destroy(decodedClip);
                    return false;
                }
                preparedMixedClip = BuildMixedSongClip(decodedClip, chart, answerClip, normalizedMusicOffset, preparedBank);
                if (fullAutoAudio && preparedBank != null && preparedMixedClip == null)
                    throw new InvalidOperationException("Full AUTO audio mix failed; gameplay will not start with duplicate realtime cues.");
                if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer"))
                    Destroy(answerClip);
                if (generation >= 0 && !IsCurrentGeneration(generation)) {
                    if (preparedMixedClip != null) Destroy(preparedMixedClip);
                    if (decodedClip != null) Destroy(decodedClip);
                    return false;
                }
                judgementAudioBank = preparedBank;
                preparedBank = null;
                judgementAudio = GetComponent<GameplayJudgementAudio>() ?? gameObject.AddComponent<GameplayJudgementAudio>();
                judgementAudio.Bind(this, chart, judgementAudioBank, fullAutoAudio && preparedMixedClip != null,
                    shell != null && shell.JudgementSoundEnabled ? shell.JudgementSoundVolume : 0f);
                songClip = decodedClip;
                mixedSongClip = preparedMixedClip;
                if (mixedSongClip != null) {
                    // The mixed clip owns the PCM needed for playback. Release
                    // the decoded source to avoid keeping two full songs alive.
                    Destroy(songClip);
                    songClip = null;
                }
                songSource = gameObject.AddComponent<AudioSource>();
                songSource.priority = 16;
                songSource.spatialBlend = 0;
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
                preparedBank?.Dispose();
                if (answerClip != null && answerClip != Resources.Load<AudioClip>("Audio/se_touch_answer")) Destroy(answerClip);
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
            // This is the single scheduling gate for normal, delayed,
            // restarted, calibration and multiplayer playback. Apply the cap
            // here so loading screens and menus remain uncapped, while every
            // actual chart run uses the saved gameplay limit.
            FrameRateSettings.ApplyGameplayFrameRate(AppSettings.Load());
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

        private AudioClip BuildMixedSongClip(AudioClip source, ParsedChart chart, AudioClip answer,
            int musicOffsetMs, JudgementAudioBank feedback = null)
        {
            AppUiShell shell = FindObjectOfType<AppUiShell>();
            if (chart == null || source == null || shell == null) return null;
            bool useAnswer = answer != null && shell.AnswerSoundEnabled && shell.AnswerSoundVolume > 0;
            bool useFeedback = fullAutoAudio && feedback != null && shell.JudgementSoundEnabled && shell.JudgementSoundVolume > 0;
            if (!useAnswer && !useFeedback) return null;
            AudioClip mixed = null;
            try {
                int frequency = source.frequency, channels = source.channels;
                var layers = new List<GameplayAudioMixLayer>();
                int answerCount = 0, feedbackCount = 0;
                if (useAnswer) {
                    var sample = new GameplayAudioMixLayer(answer, frequency, 0, 0, false, 1);
                    var events = BuildAnswerSoundEvents(chart, JudgePointScheduler.Build(chart),
                        source.length * 1000.0, frequency, shell.AnswerSoundOffsetMs - musicOffsetMs);
                    answerCount = events.Count;
                    foreach (var cue in events)
                        layers.Add(new GameplayAudioMixLayer(sample, cue.StartFrame * 1000.0 / frequency, 0, false, shell.AnswerSoundVolume));
                }
                if (useFeedback) {
                    var pcm = new Dictionary<int, GameplayAudioMixLayer>();
                    foreach (var cue in AutoJudgementAudioPlan.Build(chart)) {
                        if (!pcm.TryGetValue(cue.Cue, out var sample))
                            pcm[cue.Cue] = sample = new GameplayAudioMixLayer(feedback.Get(cue.Cue), frequency, 0, 0, false, 1);
                        // The music is scheduled musicOffsetMs after the chart.
                        // Feedback follows chart time, ANSWER keeps its own user offset.
                        layers.Add(new GameplayAudioMixLayer(sample, cue.StartMs - musicOffsetMs,
                            cue.EndMs - musicOffsetMs, cue.Loop, shell.JudgementSoundVolume));
                        feedbackCount++;
                    }
                }
                mixed = AudioClip.Create(fullAutoAudio ? "Full AUTO music + ANSWER + judgement" : "Gameplay song with chart answer sounds",
                    source.samples, channels, frequency, false);
                const int chunkFrames = 32768;
                for (int offset = 0; offset < source.samples; offset += chunkFrames) {
                    int frames = Math.Min(chunkFrames, source.samples - offset);
                    var chunk = new float[frames * channels];
                    if (!source.GetData(chunk, offset)) throw new InvalidOperationException("Unreadable song PCM");
                    for (int i = 0; i < chunk.Length; i++) chunk[i] *= Mathf.Clamp01(shell.GameplayMusicVolume);
                    foreach (var layer in layers) layer.AddTo(chunk, offset, channels);
                    // Sum all layers first. A per-event clamp changes the result
                    // with event order and destroys cancellation between layers.
                    for (int i = 0; i < chunk.Length; i++) chunk[i] = Mathf.Clamp(chunk[i], -1, 1);
                    if (!mixed.SetData(chunk, offset)) throw new InvalidOperationException("Cannot write mixed PCM");
                }
                Debug.Log($"Gameplay audio mixed: fullAuto={fullAutoAudio}, answerEvents={answerCount}, " +
                    $"feedbackEvents={feedbackCount}, samples={source.samples}, channels={channels}, frequency={frequency}Hz");
                return mixed;
            } catch (Exception error) {
                if (mixed != null) Destroy(mixed);
                Debug.LogWarning("Gameplay audio mix failed: " + error.Message);
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

        private static List<AnswerSoundEvent> BuildAnswerSoundEvents(ParsedChart chart,
            List<ScheduledJudgePoint> points, double songDurationMs, int frequency, int answerOffsetMs)
        {
            var events = new List<AnswerSoundEvent>();
            var unique = new HashSet<long>();
            double timingOffsetMs = JudgementEngine.Frames(JudgementEngine.AnswerOffsetFrames) + answerOffsetMs;
            if (chart == null || points == null || frequency <= 0) return events;

            foreach (ScheduledJudgePoint point in points) {
                if (point.NoteIndex < 0 || point.NoteIndex >= chart.Notes.Count) continue;
                ChartNote note = chart.Notes[point.NoteIndex];
                if (note == null) continue;

                AnswerSoundCategory category;
                if (!TryGetAnswerSoundCategory(note, point, out category)) continue;
                AddAnswerEvent(events, unique, point.TimeMs + timingOffsetMs,
                    category, songDurationMs, frequency);
            }

            // Slide checkpoint ownership is a chart-node property, not a
            // KEEP sample property. Schedule it at the authored node time so
            // allocating extra continuous judge points cannot move the cue.
            foreach (ChartNote note in chart.Notes) {
                if (note == null || note.Kind != NoteKind.Slide || note.VisibleHead ||
                    !note.IsSlideCheckpoint || note.OriginalType is not ("SLD" or "SXD")) continue;
                AddAnswerEvent(events, unique, note.TimeMs + timingOffsetMs,
                    AnswerSoundCategory.LongNode, songDurationMs, frequency);
            }

            // HOLD/HEAVEN HOLD have a separate category-2 endpoint. SLIDE
            // endpoints are added only through their authored checkpoint flag;
            // a geometric tail alone is not an ANSWER event in the original.
            foreach (ChartNote note in chart.Notes) {
                if (note == null || !note.VisibleTail || note.EndTimeMs <= note.TimeMs) continue;
                if (note.Kind is NoteKind.Hold or NoteKind.HeavenHold)
                    AddAnswerEvent(events, unique, note.EndTimeMs + timingOffsetMs,
                        AnswerSoundCategory.LongNode, songDurationMs, frequency);
            }
            events.Sort((left, right) => {
                int frame = left.StartFrame.CompareTo(right.StartFrame);
                return frame != 0 ? frame : left.Category.CompareTo(right.Category);
            });
            return events;
        }

        private static bool TryGetAnswerSoundCategory(ChartNote note, ScheduledJudgePoint point,
            out AnswerSoundCategory category)
        {
            category = 0;
            // MINE remains part of the combo/checker path, but the original
            // normal-note binding explicitly excludes type 11 from ANSWER.
            if (note == null || note.Kind == NoteKind.Mine) return false;

            // ALD plates, ASD endpoint notes and AIR long action checkpoints
            // use category 3. They must not also receive a generic category-1
            // head cue when their visual cap is present.
            if (note.Kind == NoteKind.AirAction &&
                (note.OriginalType is "ALD" or "ASD_NODE" || !note.VisibleHead || point.PointIndex > 0)) {
                category = AnswerSoundCategory.AirAction;
                return true;
            }
            if (note.Kind == NoteKind.AirHold && (!note.VisibleHead || point.PointIndex > 0)) {
                category = AnswerSoundCategory.AirAction;
                return true;
            }

            if (point.PointIndex != 0 || !note.VisibleHead) return false;
            if (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or NoteKind.Air or
                NoteKind.AirHold or NoteKind.AirAction) {
                category = AnswerSoundCategory.Head;
                return true;
            }
            return false;
        }

        private static bool AddAnswerEvent(List<AnswerSoundEvent> events, HashSet<long> unique,
            double timeMs, AnswerSoundCategory category, double songDurationMs, int frequency)
        {
            if (timeMs < 0 || timeMs >= songDurationMs) return false;
            int startFrame = Math.Max(0, Mathf.RoundToInt((float)(timeMs * frequency / 1000.0)));
            long key = ((long)startFrame << 8) | (byte)category;
            if (!unique.Add(key)) return false;
            events.Add(new AnswerSoundEvent(startFrame, category));
            return true;
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
            judgementAudio?.Silence();
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
            judgementAudio?.Silence();
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
            judgementAudio?.Clear();
            judgementAudioBank?.Dispose();
            judgementAudioBank = null;
            fullAutoAudio = false;
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
