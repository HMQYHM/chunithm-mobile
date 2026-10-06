using UnityEngine;
using System;
using ChunithmMobile.Input;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Judgement;

namespace ChunithmMobile.Runtime
{
    // 应用启动入口：在首场景创建跨场景运行时根节点和基础服务。
    // 创建顺序很重要：音频、加载遮罩、输入、轨道和 UI 互相查找组件；此处只负责装配，
    // 具体歌曲加载、页面刷新和判定生命周期分别由对应模块负责。
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
            // Keep the launch orientation stable while Unity builds the UI and
            // scans the library. Autorotation is enabled only after the loading
            // page has completed, avoiding a configuration change in startup.
            Screen.orientation = ResolveLaunchOrientation();
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
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

        private static ScreenOrientation ResolveLaunchOrientation()
        {
            // Android has already selected the current landscape side from the
            // manifest before Unity starts. Preserve that side when available;
            // otherwise use the standard landscape orientation.
            if (Screen.orientation == ScreenOrientation.LandscapeRight)
                return ScreenOrientation.LandscapeRight;
            return ScreenOrientation.LandscapeLeft;
        }

        public static void EnableLandscapeSensorRotation()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        private void OnApplicationPause(bool paused)
        {
            // Suspend menu audio too. Gameplay keeps its own explicit pause
            // until the player chooses to continue.
            AudioListener.pause = paused;
        }
    }
}
/*
 * 模块说明：应用启动入口。
 * 生命周期：由 Unity 创建后执行 Awake/Start，准备平台服务、存储目录和全局 UI。
 * 边界：这里只负责启动编排，不承载歌曲解析、判定或页面业务；这样重启和测试时可以独立替换启动依赖。
 * 维护提示：新增全局服务时，应说明创建顺序、是否跨场景存活以及失败后的回退行为。
 */
