#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace ChunithmMobile.Editor
{
    /// <summary>
    /// Owns the settings used to export the shared Unity project as an iOS Xcode project.
    /// Android and iOS deliberately share scenes, assets and runtime code; only native
    /// plugins and build outputs are platform-specific.
    /// </summary>
    [InitializeOnLoad]
    public static class AppleBuildConfigurator
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string DefaultOutput = "Build/iOS/CHUNITHM-Mobile-iOS";
        private const string DefaultSimulatorOutput = "Build/iOS/CHUNITHM-Mobile-iOS-Simulator";
        private const string AppleBundleIdentifier = "com.hmqyhm.chunithmmobile";
        private const string DefaultAppleTeam = "L2N2FK3BD5";

        static AppleBuildConfigurator()
        {
            EditorApplication.delayCall += () => {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS) Apply();
            };
        }

        [MenuItem("CHUNITHM Mobile/Apply iOS Settings")]
        public static void Apply()
        {
            PlayerSettings.productName = "CHUNITHM Mobile";
            PlayerSettings.companyName = "HMQYHM";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,
                AppleBundleIdentifier);
            PlayerSettings.bundleVersion = "0.3.5";
            // Increment the iOS build number for the 0.3.5 release.
            PlayerSettings.iOS.buildNumber = "225";
            // Current Xcode SDKs no longer accept deployment targets below iOS 15.
            // Keep Unity and both generated Xcode targets aligned so Xcode does not
            // report a version mismatch between Unity-iPhone and UnityFramework.
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.requiresFullScreen = true;
            // Android development servers may use ws://, but iOS should not
            // inherit Android's broad clear-text exception. Local networking is
            // granted narrowly in the generated Info.plist below.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP);

            // Unity 2022 device exports are ARM64-only. DeviceSDK intentionally
            // excludes simulator output, where architecture and native plugins differ.
            PlayerSettings.SetArchitecture(BuildTargetGroup.iOS, 1);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS,
                new[] { GraphicsDeviceType.Metal });
            ConfigureApplicationIcon();
            ConfigureNativePlugins();
            ConfigureSigningFromEnvironment();
            EnsureBootstrapScene();
        }

        [MenuItem("CHUNITHM Mobile/Build iOS Xcode Project")]
        public static void BuildXcodeProject()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new BuildFailedException(
                    "This Unity installation has no iOS Build Support module. " +
                    "Install it on macOS with Unity Hub, then run this build entry again.");

            Apply();
            string output = Environment.GetEnvironmentVariable("CHUNITHM_IOS_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = DefaultOutput;
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            bool appendExistingProject = File.Exists(
                Path.Combine(output, "Unity-iPhone.xcodeproj", "project.pbxproj"));

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.iOS,
                options = appendExistingProject
                    ? BuildOptions.AcceptExternalModificationsToPlayer
                    : BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(report.summary.result.ToString());
        }

        [MenuItem("CHUNITHM Mobile/Build iOS Simulator Xcode Project")]
        public static void BuildSimulatorXcodeProject()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new BuildFailedException(
                    "This Unity installation has no iOS Build Support module. " +
                    "Install it with Unity Hub, then run this build entry again.");

            Apply();
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            string output = Environment.GetEnvironmentVariable("CHUNITHM_IOS_SIMULATOR_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = DefaultSimulatorOutput;
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            bool appendExistingProject = File.Exists(
                Path.Combine(output, "Unity-iPhone.xcodeproj", "project.pbxproj"));

            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.iOS,
                    options = appendExistingProject
                        ? BuildOptions.AcceptExternalModificationsToPlayer
                        : BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException(report.summary.result.ToString());
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            }
        }

        private static void ConfigureApplicationIcon()
        {
            const string iconPath = "Assets/Branding/ChunithmLauncher_027_20260908.png";
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon == null) throw new BuildFailedException("Missing application icon: " + iconPath);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.iOS, new[] { icon });
        }

        private static void ConfigureNativePlugins()
        {
            string[] pluginPaths = {
                "Assets/Plugins/iOS/ChunithmDocumentPicker.mm",
                "Assets/Plugins/iOS/ChunithmWebSocket.mm",
                "Assets/Plugins/iOS/ChunithmSceneDelegate.mm"
            };
            foreach (string pluginPath in pluginPaths)
            {
                var importer = AssetImporter.GetAtPath(pluginPath) as PluginImporter;
                if (importer == null)
                    throw new BuildFailedException("Missing iOS native plugin importer: " + pluginPath);

                bool changed = importer.GetCompatibleWithAnyPlatform() ||
                               importer.GetCompatibleWithEditor() ||
                               !importer.GetCompatibleWithPlatform(BuildTarget.iOS);
                if (!changed) continue;
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureSigningFromEnvironment()
        {
            // Keep developer-specific signing identities out of source control.
            // Keep the team in the generated project, but let the post-build
            // Xcode patch select manual Distribution signing. This avoids the
            // Unity-generated automatic-signing settings conflicting with an
            // App Store Distribution profile during Archive.
            string teamId = Environment.GetEnvironmentVariable("APPLE_DEVELOPMENT_TEAM");
            if (string.IsNullOrWhiteSpace(teamId)) teamId = DefaultAppleTeam;
            PlayerSettings.iOS.appleDeveloperTeamID = teamId.Trim();
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        }

        private static void EnsureBootstrapScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            if (!EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

#if UNITY_IOS
        [PostProcessBuild(100)]
        private static void ConfigureXcodeProject(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            root.SetString("NSLocalNetworkUsageDescription",
                "用于连接您指定的联机服务器并同步房间与谱面数据。");
            ConfigureSceneLifecycle(buildPath, root);

            // Permit plain-text connections only to the local network. Public
            // servers remain HTTPS/WSS-only; the project intentionally does not
            // add the broad NSAllowsArbitraryLoads exception.
            PlistElementDict transport = root.CreateDict("NSAppTransportSecurity");
            transport.SetBoolean("NSAllowsLocalNetworking", true);
            WriteLandscapeOrientations(root, "UISupportedInterfaceOrientations");
            WriteLandscapeOrientations(root, "UISupportedInterfaceOrientations~ipad");
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string mainTarget = project.GetUnityMainTargetGuid();
            string frameworkTarget = project.GetUnityFrameworkTargetGuid();
            project.SetBuildProperty(mainTarget, "TARGETED_DEVICE_FAMILY", "1,2");
            project.SetBuildProperty(mainTarget, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
            project.SetBuildProperty(frameworkTarget, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
            project.SetBuildProperty(mainTarget, "ENABLE_BITCODE", "NO");
            project.SetBuildProperty(frameworkTarget, "ENABLE_BITCODE", "NO");
            project.AddFrameworkToProject(frameworkTarget,
                "UniformTypeIdentifiers.framework", false);
            project.AddFrameworkToProject(mainTarget,
                "UniformTypeIdentifiers.framework", false);

            string teamId = Environment.GetEnvironmentVariable("APPLE_DEVELOPMENT_TEAM");
            if (string.IsNullOrWhiteSpace(teamId)) teamId = DefaultAppleTeam;
            ApplyManualDistributionSigning(project, mainTarget, teamId.Trim(), true);
            ApplyManualDistributionSigning(project, frameworkTarget, teamId.Trim(), false);
            project.WriteToFile(projectPath);
        }

        private static void ConfigureSceneLifecycle(string buildPath, PlistElementDict root)
        {
            PlistElementDict manifest = root.CreateDict("UIApplicationSceneManifest");
            manifest.SetBoolean("UIApplicationSupportsMultipleScenes", false);
            PlistElementArray configurations = manifest.CreateDict("UISceneConfigurations")
                .CreateArray("UIWindowSceneSessionRoleApplication");
            PlistElementDict configuration = configurations.AddDict();
            configuration.SetString("UISceneConfigurationName", "Default Configuration");
            configuration.SetString("UISceneDelegateClassName", "ChunithmSceneDelegate");

            string controllerPath = Path.Combine(buildPath, "Classes", "UnityAppController.mm");
            string controllerText = File.ReadAllText(controllerPath).Replace("\r\n", "\n");
            const string launchBefore = "    [self initUnityWithApplication: application];\n    return YES;";
            const string launchAfter = "    // The scene delegate initializes Unity after UIKit supplies its window.\n" +
                "    if ([[NSBundle mainBundle] objectForInfoDictionaryKey:@\"UIApplicationSceneManifest\"] == nil)\n" +
                "        [self initUnityWithApplication: application];\n    return YES;";
            if (controllerText.Contains(launchBefore))
                controllerText = controllerText.Replace(launchBefore, launchAfter);
            else if (!controllerText.Contains("objectForInfoDictionaryKey:@\"UIApplicationSceneManifest\""))
                throw new BuildFailedException("Unexpected Unity launch template; scene lifecycle patch was not applied.");

            const string windowBefore = "#if !PLATFORM_VISIONOS\n" +
                "    if (@available(iOS 13, tvOS 13, *))\n" +
                "        _window = [[UIWindow alloc] initWithWindowScene: [self pickStartupWindowScene: application.connectedScenes]];\n" +
                "    else\n" +
                "        _window = [[UIWindow alloc] initWithFrame: [UIScreen mainScreen].bounds];";
            const string windowAfter = "#if !PLATFORM_VISIONOS\n" +
                "    if (_window == nil)\n" +
                "    {\n" +
                "        if (@available(iOS 13, tvOS 13, *))\n" +
                "            _window = [[UIWindow alloc] initWithWindowScene: [self pickStartupWindowScene: application.connectedScenes]];\n" +
                "        else\n" +
                "            _window = [[UIWindow alloc] initWithFrame: [UIScreen mainScreen].bounds];\n" +
                "    }";
            if (controllerText.Contains(windowBefore))
                controllerText = controllerText.Replace(windowBefore, windowAfter);
            else if (!controllerText.Contains("if (_window == nil)"))
                throw new BuildFailedException("Unexpected Unity window template; scene window patch was not applied.");

            File.WriteAllText(controllerPath, controllerText);
        }

        private static void ApplyManualDistributionSigning(PBXProject project,
            string targetGuid, string teamId, bool mainTarget)
        {
            project.SetBuildProperty(targetGuid, "DEVELOPMENT_TEAM", teamId);
            project.SetBuildProperty(targetGuid, "CODE_SIGN_STYLE", "Manual");
            project.SetBuildProperty(targetGuid, "CODE_SIGN_IDENTITY[sdk=iphoneos*]", "Apple Distribution");
            // The app target may receive the profile name/UUID from the Mac
            // environment. UnityFramework deliberately has no independent
            // provisioning profile, matching the successful archive setup.
            string profile = mainTarget
                ? Environment.GetEnvironmentVariable("APPLE_PROVISIONING_PROFILE_SPECIFIER")
                : null;
            if (mainTarget && !string.IsNullOrWhiteSpace(profile))
                project.SetBuildProperty(targetGuid, "PROVISIONING_PROFILE_SPECIFIER", profile.Trim());
            else
                project.SetBuildProperty(targetGuid, "PROVISIONING_PROFILE_SPECIFIER", "");
            project.SetBuildProperty(targetGuid, "PROVISIONING_PROFILE", "");
        }

        private static void WriteLandscapeOrientations(PlistElementDict root, string key)
        {
            PlistElementArray orientations = root.CreateArray(key);
            orientations.AddString("UIInterfaceOrientationLandscapeLeft");
            orientations.AddString("UIInterfaceOrientationLandscapeRight");
        }
#endif
    }
}
#endif
