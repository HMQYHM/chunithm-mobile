using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Runtime
{
    // 界面背景音乐控制器：管理入口音乐、结算音乐、渐弱和页面切换时的连续播放。
    // 游玩歌曲由 LocalChartPlaybackSession 管理，不能由本类抢占或重置。
    /// <summary>Owns application BGM and the fixed cue sequence used by page transitions.</summary>
    public sealed class InterfaceAudioController : MonoBehaviour
    {
        private const float EntryVolume = .72f;
        private const float ResultsVolume = .82f;

        private AudioSource entrySource;
        private AudioSource resultsSource;
        private AudioSource cueSource;
        private AudioSource gameplayKeySource;
        private AudioClip entryClip;
        private AudioClip resultsClip;
        private AudioClip switchClip;
        private AudioClip answerClip;
        private Coroutine musicFade;
        private Coroutine cueSequence;
        private Coroutine skinAudioLoad;
        private string loadedSkinId;
        private int skinAudioGeneration;
        private bool keySoundEnabled = true;
        private bool answerSoundEnabled = true;
        private float keySoundVolume = .8f;
        private float answerSoundVolume = .8f;
        private float backgroundMusicVolume = 1f;

        private enum MusicMode { Silent, Entry, Results }

        private void Awake()
        {
            entrySource = CreateSource("Interface entry music", true, false);
            resultsSource = CreateSource("Result music", true, false);
            cueSource = CreateSource("Page transition cues", false, true);
            gameplayKeySource = CreateSource("Gameplay key sounds", false, false);
            gameplayKeySource.volume = 1f;
            entryClip = Resources.Load<AudioClip>("Audio/ui_entry");
            resultsClip = Resources.Load<AudioClip>("Audio/ui_results");
            switchClip = Resources.Load<AudioClip>("Audio/delay_ready");
            answerClip = Resources.Load<AudioClip>("Audio/se_touch_answer");
            entrySource.clip = entryClip;
            resultsSource.clip = resultsClip;
            // Force the first runtime settings pass to load audio from the
            // active external skin. Awake runs immediately after the skin
            // manager is initialized, so treating the ID as already loaded
            // would silently keep the built-in BGM on a cold launch.
            loadedSkinId = null;
        }

        public void RefreshSkinAudio()
        {
            string skinId = GameplaySkinManager.Active.Id;
            if (string.Equals(loadedSkinId, skinId, System.StringComparison.OrdinalIgnoreCase)) return;
            loadedSkinId = skinId;
            int generation = ++skinAudioGeneration;
            if (skinAudioLoad != null) StopCoroutine(skinAudioLoad);
            skinAudioLoad = StartCoroutine(LoadSkinAudio(skinId, generation));
        }

        private IEnumerator LoadSkinAudio(string skinId, int generation)
        {
            AudioClip nextEntry = Resources.Load<AudioClip>("Audio/ui_entry");
            AudioClip nextResults = Resources.Load<AudioClip>("Audio/ui_results");
            AudioClip nextSwitch = Resources.Load<AudioClip>("Audio/delay_ready");
            AudioClip nextAnswer = Resources.Load<AudioClip>("Audio/se_touch_answer");
            yield return LoadSkinClip("ui_entry.mp3", AudioType.MPEG, value => { if (IsUsableClip(value)) nextEntry = value; });
            if (generation != skinAudioGeneration) yield break;
            yield return LoadSkinClip("ui_results.mp3", AudioType.MPEG, value => { if (IsUsableClip(value)) nextResults = value; });
            if (generation != skinAudioGeneration) yield break;
            yield return LoadSkinClip("delay_ready.wav", AudioType.WAV, value => { if (IsUsableClip(value)) nextSwitch = value; });
            if (generation != skinAudioGeneration) yield break;
            yield return LoadSkinClip("se_touch_answer.wav", AudioType.WAV, value => { if (IsUsableClip(value)) nextAnswer = value; });
            if (generation != skinAudioGeneration ||
                !string.Equals(loadedSkinId, skinId, System.StringComparison.OrdinalIgnoreCase)) yield break;

            if (nextEntry != null) entryClip = nextEntry;
            if (nextResults != null) resultsClip = nextResults;
            if (nextSwitch != null) switchClip = nextSwitch;
            if (nextAnswer != null) answerClip = nextAnswer;
            bool entryWasPlaying = entrySource != null && entrySource.isPlaying;
            bool resultsWasPlaying = resultsSource != null && resultsSource.isPlaying;
            if (entrySource != null) {
                entrySource.clip = entryClip;
                if (entryWasPlaying) { entrySource.Stop(); entrySource.Play(); }
            }
            if (resultsSource != null) {
                resultsSource.clip = resultsClip;
                if (resultsWasPlaying) { resultsSource.Stop(); resultsSource.Play(); }
            }
            skinAudioLoad = null;
        }

        private IEnumerator LoadSkinClip(string fileName, AudioType type, System.Action<AudioClip> receive)
        {
            string path = GameplaySkinManager.GetAudioPath(fileName);
            if (string.IsNullOrWhiteSpace(path)) { receive?.Invoke(null); yield break; }
            // File-backed audio can fail once on slower Android storage while
            // the package is still being indexed. Retry the decode once before
            // falling back to the built-in clip; this prevents a transient
            // request failure from leaving the interface silent for the rest
            // of the session.
            for (int attempt = 0; attempt < 2; attempt++) {
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                    new System.Uri(path).AbsoluteUri, type)) {
                    if (request.downloadHandler is DownloadHandlerAudioClip handler) {
                        handler.streamAudio = false;
                        handler.compressed = false;
                    }
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                        if (IsUsableClip(clip)) {
                            receive?.Invoke(clip);
                            yield break;
                        }
                        Debug.LogWarning("Gameplay skin audio is empty or undecodable: " + fileName);
                    } else {
                        Debug.LogWarning("Gameplay skin audio failed: " + fileName +
                            " | attempt=" + (attempt + 1) + " | " + request.error);
                    }
                }
                if (attempt == 0) yield return new WaitForSecondsRealtime(.1f);
            }
            receive?.Invoke(null);
        }

        private static bool IsUsableClip(AudioClip clip) => clip != null &&
            clip.samples > 0 && clip.frequency > 0 && clip.channels > 0 && clip.length > .01f;

        public void Configure(bool enableKeySound, float keyVolume,
            bool enableAnswerSound, float answerVolume, float backgroundVolume)
        {
            keySoundEnabled = enableKeySound;
            answerSoundEnabled = enableAnswerSound;
            keySoundVolume = Mathf.Clamp01(keyVolume);
            answerSoundVolume = Mathf.Clamp01(answerVolume);
            backgroundMusicVolume = Mathf.Clamp01(backgroundVolume);
            if (entrySource != null && entrySource.isPlaying)
                entrySource.volume = EntryVolume * backgroundMusicVolume;
            if (resultsSource != null && resultsSource.isPlaying)
                resultsSource.volume = ResultsVolume * backgroundMusicVolume;
        }

        public void BeginTransition(AppPage from, AppPage to, float duration)
        {
            if (from == to) return;
            PlayTransitionCue(duration);
            MusicMode target = ModeFor(to);
            if (musicFade != null) StopCoroutine(musicFade);
            musicFade = StartCoroutine(FadeMusic(target, Mathf.Max(.05f, duration)));
        }

        public void PlayMineKeySound()
        {
            if (!keySoundEnabled || keySoundVolume <= 0f || gameplayKeySource == null) return;
            AudioClip mineMiss = Resources.Load<AudioClip>("Audio/Judgement/cue_303");
            if (mineMiss != null) gameplayKeySource.PlayOneShot(mineMiss, keySoundVolume);
        }

        public void PlayStandaloneTransition(float duration)
        {
            PlayTransitionCue(Mathf.Max(.05f, duration));
        }

        public void SetPageImmediate(AppPage page)
        {
            if (musicFade != null) StopCoroutine(musicFade);
            MusicMode mode = ModeFor(page);
            SetSourceImmediate(entrySource, mode == MusicMode.Entry, EntryVolume * backgroundMusicVolume);
            SetSourceImmediate(resultsSource, mode == MusicMode.Results, ResultsVolume * backgroundMusicVolume);
        }

        public void RestartBackgroundMusic(AppPage page)
        {
            if (musicFade != null) {
                StopCoroutine(musicFade);
                musicFade = null;
            }
            MusicMode mode = ModeFor(page);
            if (mode == MusicMode.Entry) {
                resultsSource.Stop();
                entrySource.Stop();
                entrySource.clip = entryClip;
                entrySource.loop = true;
                entrySource.volume = EntryVolume * backgroundMusicVolume;
                if (entryClip != null) entrySource.Play();
            } else if (mode == MusicMode.Results) {
                entrySource.Stop();
                resultsSource.Stop();
                resultsSource.clip = resultsClip;
                resultsSource.loop = true;
                resultsSource.volume = ResultsVolume * backgroundMusicVolume;
                if (resultsClip != null) resultsSource.Play();
            } else {
                entrySource.Stop();
                resultsSource.Stop();
            }
        }

        public void StopBackgroundMusicForSongPreview()
        {
            if (musicFade != null) {
                StopCoroutine(musicFade);
                musicFade = null;
            }
            entrySource?.Stop();
            resultsSource?.Stop();
            if (entrySource != null) entrySource.volume = 0f;
            if (resultsSource != null) resultsSource.volume = 0f;
        }

        private IEnumerator FadeMusic(MusicMode target, float duration)
        {
            float entryStart = entrySource.volume;
            float resultsStart = resultsSource.volume;
            float entryTarget = target == MusicMode.Entry ? EntryVolume * backgroundMusicVolume : 0f;
            float resultsTarget = target == MusicMode.Results ? ResultsVolume * backgroundMusicVolume : 0f;

            if (entryTarget > 0f && !entrySource.isPlaying && entryClip != null) {
                entrySource.clip = entryClip;
                entrySource.loop = true;
                entrySource.Play();
            }
            if (resultsTarget > 0f && !resultsSource.isPlaying) {
                resultsSource.Stop();
                resultsSource.clip = resultsClip;
                resultsSource.loop = true;
                resultsSource.volume = 0f;
                resultsStart = 0f;
                if (resultsClip != null) resultsSource.Play();
            }

            float elapsed = 0f;
            while (elapsed < duration) {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                entrySource.volume = Mathf.Lerp(entryStart, entryTarget, t);
                resultsSource.volume = Mathf.Lerp(resultsStart, resultsTarget, t);
                yield return null;
            }

            entrySource.volume = entryTarget;
            resultsSource.volume = resultsTarget;
            if (entryTarget <= 0f) entrySource.Stop();
            if (resultsTarget <= 0f) resultsSource.Stop();
            musicFade = null;
        }

        private void PlayTransitionCue(float duration)
        {
            if (cueSequence != null) StopCoroutine(cueSequence);
            cueSource.Stop();
            cueSequence = StartCoroutine(TransitionCueSequence(duration));
        }

        private IEnumerator TransitionCueSequence(float duration)
        {
            if (keySoundEnabled && switchClip != null)
                cueSource.PlayOneShot(switchClip, keySoundVolume);
            yield return new WaitForSecondsRealtime(duration / 3f);
            if (answerSoundEnabled && answerClip != null)
                cueSource.PlayOneShot(answerClip, answerSoundVolume);
            yield return new WaitForSecondsRealtime(duration / 3f);
            if (answerSoundEnabled && answerClip != null)
                cueSource.PlayOneShot(answerClip, answerSoundVolume);
            cueSequence = null;
        }

        private AudioSource CreateSource(string sourceName, bool loop, bool ignoreListenerVolume)
        {
            var sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.ignoreListenerVolume = ignoreListenerVolume;
            source.volume = 0f;
            return source;
        }

        private static void SetSourceImmediate(AudioSource source, bool play, float volume)
        {
            source.volume = play ? volume : 0f;
            if (play) {
                if (source.clip != null && !source.isPlaying) source.Play();
            } else source.Stop();
        }

        private static MusicMode ModeFor(AppPage page)
        {
            if (page == AppPage.Results) return MusicMode.Results;
            if (page == AppPage.MultiplayerGroupResults) return MusicMode.Results;
            if (page is AppPage.Home or AppPage.Settings or AppPage.DataManagement or AppPage.DelayCalibrationResults
                or AppPage.Multiplayer or AppPage.MultiplayerBrowser or AppPage.MultiplayerRoom)
                return MusicMode.Entry;
            return MusicMode.Silent;
        }
    }
}
