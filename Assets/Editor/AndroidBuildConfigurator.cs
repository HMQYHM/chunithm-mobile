#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ChunithmMobile.Editor
{
    [InitializeOnLoad]
    public static class AndroidBuildConfigurator
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string JdkPath = @"F:\zhongermate\.unity-toolchains\OpenJDK";
        private const string SdkPath = @"F:\zhongermate\.unity-toolchains\SDK";
        private const string NdkPath = @"F:\zhongermate\.unity-toolchains\NDK";

        static AndroidBuildConfigurator()
        {
            EditorApplication.delayCall += Apply;
        }

        [MenuItem("CHUNITHM Mobile/Apply Android Settings")]
        public static void Apply()
        {
            ConfigureExternalTools();
            PlayerSettings.productName = "CHUNITHM Mobile";
            PlayerSettings.companyName = "HMQYHM";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.zhongermate.chunithmmobile");
            PlayerSettings.bundleVersion = "0.2.8";
            PlayerSettings.Android.bundleVersionCode = 208;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            // Use the installed stable platform explicitly. Auto would currently
            // pick the Android 36.1 preview platform from Android Studio, which
            // Unity 2022.3 does not treat as a normal release API.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Runtime UI and audio services are created from code rather than
            // scene references. Keep their engine-side component classes.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            // Vulkan is primary. GLES3 remains a compatibility fallback for Android 7 devices
            // whose vendor image does not expose a stable Vulkan driver.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            ConfigureApplicationIcon();
            EnsureBootstrapScene();
        }

        private static void ConfigureApplicationIcon()
        {
            const string path = "Assets/Branding/ChunithmLauncher_027_20260908.png";
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null) throw new BuildFailedException("Missing application icon: " + path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)) {
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            foreach (PlatformIconKind kind in new[] { AndroidPlatformIconKind.Legacy, AndroidPlatformIconKind.Round }) {
                PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
                foreach (PlatformIcon slot in slots) slot.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, slots);
            }
            const string backgroundPath = "Assets/Branding/ChunithmLauncherBackground_027_20260908.png";
            Texture2D background = AssetDatabase.LoadAssetAtPath<Texture2D>(backgroundPath);
            if (background == null) {
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
                texture.Apply();
                System.IO.File.WriteAllBytes(backgroundPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(backgroundPath);
                background = AssetDatabase.LoadAssetAtPath<Texture2D>(backgroundPath);
            }
            PlatformIcon[] adaptive = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, AndroidPlatformIconKind.Adaptive);
            foreach (PlatformIcon slot in adaptive) slot.SetTextures(new[] { background, icon });
            PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, AndroidPlatformIconKind.Adaptive, adaptive);
        }

        private static void ConfigureExternalTools()
        {
            // Toolchains stay outside the Unity editor installation. This keeps the
            // project reproducible and avoids requiring administrator privileges.
            if (System.IO.File.Exists(System.IO.Path.Combine(JdkPath, "bin", "java.exe")))
                AndroidExternalToolsSettings.jdkRootPath = JdkPath;
            if (System.IO.Directory.Exists(SdkPath))
                AndroidExternalToolsSettings.sdkRootPath = SdkPath;
            if (System.IO.File.Exists(System.IO.Path.Combine(NdkPath, "source.properties")))
                AndroidExternalToolsSettings.ndkRootPath = NdkPath;
        }

        [MenuItem("CHUNITHM Mobile/Build Android APK")]
        public static void BuildApk()
        {
            Apply();
            string output = System.Environment.GetEnvironmentVariable("CHUNITHM_APK_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Build/CHUNITHM-Mobile-0.2.8-unity-arm64.apk";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(report.summary.result.ToString());
        }

        private static void EnsureBootstrapScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!System.IO.File.Exists(ScenePath)) {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
#endif
