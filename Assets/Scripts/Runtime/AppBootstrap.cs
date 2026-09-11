using UnityEngine;
using System;
using ChunithmMobile.Input;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Judgement;

namespace ChunithmMobile.Runtime
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            if (FindObjectOfType<AppBootstrap>() != null) return;
            var root = new GameObject("CHUNITHM Mobile Runtime");
            DontDestroyOnLoad(root);
            root.AddComponent<LowLatencyAudioController>();
            root.AddComponent<AppBootstrap>();
            root.AddComponent<RuntimeLoadingOverlay>();
            root.AddComponent<SongSelectionPreviewPlayer>();
            root.AddComponent<ArcadeInputRig>();
            CreatePlayfield(root.transform);
            root.AddComponent<AppUiShell>();
        }

        private static void CreatePlayfield(Transform parent)
        {
            var cameraObject = new GameObject("Gameplay Camera");
            cameraObject.transform.SetParent(parent, false);
            // Keep the Android-adapted playfield camera. The mobile layout uses
            // this perspective and the touchpad boundary is tuned against it;
            // changing to the arcade camera moves the judgement line and makes
            // the Android track feel like a different game.
            cameraObject.transform.position = new Vector3(0, 11.45f, -9.2f);
            cameraObject.transform.rotation = Quaternion.Euler(35f, 0, 0);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.005f, .008f, .015f);
            cameraObject.AddComponent<GameplayBackdropRenderer>();

            var field = new GameObject("Shared chart-space playfield");
            field.transform.SetParent(parent, false);
            // Do not even initialize meshes/materials on loading, home, song
            // select or results. SetGameplayScene activates this object only
            // after the player has committed to a chart.
            field.SetActive(false);
            field.AddComponent<PlayfieldRenderer>();
            field.AddComponent<ChartPlaybackRenderer>();
            field.AddComponent<GameplayJudgeController>();
            field.AddComponent<LocalChartPlaybackSession>();

            // Non-gameplay pages are rendered first. Keeping the playfield alive
            // behind the overlay leaked tiny pieces of the gold judge line at the
            // screen edges on wide devices. Gameplay loading explicitly enables
            // these two objects when that module is entered.
            cameraObject.SetActive(false);
        }

        private void Awake()
        {
            try
            {
                PlatformStoragePaths.EnsureAppDirectories();
            }
            catch (Exception exception)
            {
                Debug.LogError("Unable to create first-run application folders: " + exception);
            }
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            // Android treats -1 as 30 fps. A target above the display rate
            // lets Unity use the current refresh rate without a custom cap.
            Application.targetFrameRate = int.MaxValue;
            IPlatformServices platform = PlatformServices.Current;
            Debug.Log($"CHUNITHM Mobile {Application.version} Unity prototype | {platform.DisplayName} | " +
                      $"{SystemInfo.graphicsDeviceType} | {SystemInfo.processorType} | " +
                      $"display={Screen.currentResolution.refreshRateRatio.value:F2}Hz | " +
                      $"safeArea={platform.SafeAreaPixels}");
        }

        private void OnApplicationPause(bool paused)
        {
            // Suspend menu audio too. Gameplay keeps its own explicit pause
            // until the player chooses to continue.
            AudioListener.pause = paused;
        }
    }
}
