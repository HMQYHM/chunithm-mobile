using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Plays looping audio after a song is selected and before gameplay starts.
    /// ACB/AWB is converted to preview.mp3 during import. Songs without ACB
    /// reuse gameplay.mp3, keeping the runtime player portable.
    /// </summary>
    public sealed class SongSelectionPreviewPlayer : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip loadedClip;
        private Coroutine loadingRoutine;
        private int requestVersion;

        public string SelectedSongRoot { get; private set; }
        public bool IsPlaying => source != null && source.isPlaying;
        public bool IsLoading => loadingRoutine != null;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
        }

        public void SelectSong(string songRoot)
        {
            CancelPendingLoad();
            SelectedSongRoot = songRoot;
            int version = ++requestVersion;
            // A short settle delay prevents rapid browsing from opening and decoding
            // every full MP3 the finger passes over. The previous preview keeps playing
            // until the final selection is ready, so the UI response stays immediate.
            loadingRoutine = StartCoroutine(LoadSelectionAfterDelay(songRoot, version));
        }

        private IEnumerator LoadSelectionAfterDelay(string songRoot, int version)
        {
            yield return new WaitForSecondsRealtime(.10f);
            if (version != requestVersion) yield break;
            SongAudioSet files = SongAudioFiles.Resolve(songRoot);
            if (!files.IsSelectionPreviewReady)
            {
                if (files.NeedsPreviewGeneration)
                    Debug.Log($"Selection preview is waiting for ACB/AWB import conversion: {songRoot}");
                else
                    Debug.LogWarning($"Song has neither ACB preview nor gameplay MP3: {songRoot}");
                loadingRoutine = null;
                yield break;
            }
            yield return LoadAndPlay(files.SelectionPreview, version, true, null);
        }

        public void PlayGameplay(string songRoot, Action onStarted)
        {
            StopPreview();
            SelectedSongRoot = songRoot;
            SongAudioSet files = SongAudioFiles.Resolve(songRoot);
            if (string.IsNullOrWhiteSpace(files.GameplayMp3))
            {
                Debug.LogWarning($"Gameplay MP3 is missing: {songRoot}");
                onStarted?.Invoke();
                return;
            }
            int version = ++requestVersion;
            loadingRoutine = StartCoroutine(LoadAndPlay(files.GameplayMp3, version, false, onStarted));
        }

        public void StopPreview()
        {
            ++requestVersion;
            CancelPendingLoad();
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }
            if (loadedClip != null)
            {
                Destroy(loadedClip);
                loadedClip = null;
            }
        }

        private void CancelPendingLoad()
        {
            if (loadingRoutine == null) return;
            StopCoroutine(loadingRoutine);
            loadingRoutine = null;
        }

        private IEnumerator LoadAndPlay(string path, int version, bool loop, Action onStarted)
        {
            if (!File.Exists(path)) yield break;
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                if (request.downloadHandler is DownloadHandlerAudioClip audioHandler)
                {
                    audioHandler.streamAudio = loop;
                    audioHandler.compressed = true;
                }
                yield return request.SendWebRequest();
                loadingRoutine = null;
                if (version != requestVersion) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Selection preview MP3 load failed: {path} | {request.error}");
                    onStarted?.Invoke();
                    yield break;
                }
                AudioClip nextClip = DownloadHandlerAudioClip.GetContent(request);
                if (nextClip == null) yield break;
                AudioClip previousClip = loadedClip;
                loadedClip = nextClip;
                source.clip = loadedClip;
                source.loop = loop;
                source.Play();
                if (previousClip != null && previousClip != loadedClip) Destroy(previousClip);
            }

            if (loadedClip == null || version != requestVersion) yield break;
            Debug.Log($"{(loop ? "Selection preview" : "Gameplay MP3")} started: {path}");
            onStarted?.Invoke();
        }

        private void OnDestroy() => StopPreview();
    }
}
