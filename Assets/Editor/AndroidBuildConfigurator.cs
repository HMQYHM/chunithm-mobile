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
    // Android 构建入口：集中设置包名、版本、架构、图标和 APK 输出路径。
    // 构建脚本不应混入运行时业务；这样命令行构建和 Unity 编辑器构建保持一致。
    [InitializeOnLoad]
    public static class AndroidBuildConfigurator
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string JdkPath = @"F:\zhongermate\.unity-toolchains\OpenJDK";
        private const string SdkPath = @"F:\zhongermate\.unity-toolchains\SDK";
        private const string NdkPath = @"F:\zhongermate\.unity-toolchains\NDK";

        static AndroidBuildConfigurator()
        {
            EditorApplication.delayCall += () => {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) Apply();
            };
        }

        [MenuItem("CHUNITHM Mobile/Apply Android Settings")]
        public static void Apply()
        {
            ConfigureExternalTools();
            ConfigureAndroidSigning();
            PlayerSettings.productName = "CHUNITHM Mobile";
            PlayerSettings.companyName = "HMQYHM";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.zhongermate.chunithmmobile");
            PlayerSettings.bundleVersion = "0.3.4";
            PlayerSettings.Android.bundleVersionCode = 304;
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
            // Start from the device's current orientation, but the project
            // autorotation mask below still restricts the result to landscape.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
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
        }

        private static void ConfigureExternalTools()
        {
            // Toolchains stay outside the Unity editor installation. This keeps the
            // project reproducible and avoids requiring administrator privileges.
            // Unity 2022.3.62f1 does not expose AndroidExternalToolsSettings
            // in the installed editor API. The build environment supplies the
            // toolchain paths through the project's configured Unity settings.
        }

        private static void ConfigureAndroidSigning()
        {
            // Signing credentials stay outside the project and are supplied by
            // the build environment. This lets a release use the original
            // keystore without committing its password or machine-specific
            // path to source control.
            string keystore = System.Environment.GetEnvironmentVariable("CHUNITHM_ANDROID_KEYSTORE");
            string storePassword = System.Environment.GetEnvironmentVariable("CHUNITHM_ANDROID_KEYSTORE_PASS");
            string alias = System.Environment.GetEnvironmentVariable("CHUNITHM_ANDROID_KEY_ALIAS");
            string aliasPassword = System.Environment.GetEnvironmentVariable("CHUNITHM_ANDROID_KEY_PASS");
            if (string.IsNullOrWhiteSpace(keystore) && string.IsNullOrWhiteSpace(alias)) return;
            if (string.IsNullOrWhiteSpace(keystore) || string.IsNullOrWhiteSpace(storePassword) ||
                string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(aliasPassword))
                throw new BuildFailedException("Android signing requires CHUNITHM_ANDROID_KEYSTORE, CHUNITHM_ANDROID_KEYSTORE_PASS, CHUNITHM_ANDROID_KEY_ALIAS and CHUNITHM_ANDROID_KEY_PASS.");
            if (!System.IO.File.Exists(keystore))
                throw new BuildFailedException("Android signing keystore not found: " + keystore);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = storePassword;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPassword;
        }

        [MenuItem("CHUNITHM Mobile/Build Android APK")]
        public static void BuildApk()
        {
            Apply();
            string output = System.Environment.GetEnvironmentVariable("CHUNITHM_APK_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Build/Android/CHUNITHM-Mobile-0.3.4-unity-arm64.apk";
            string outputDirectory = System.IO.Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(outputDirectory)) System.IO.Directory.CreateDirectory(outputDirectory);
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
