using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ChunithmMobile.Editor
{
    /// <summary>
    /// Selects the Cubism library for each target before export. SDK r.5 ships
    /// Unity 6 plugin metadata; explicitly configure it for this Unity 2022 project
    /// and never link multiple device/simulator variants into the same iOS binary.
    /// </summary>
    public sealed class GameplaySkinNativePlugins : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string configuration = EditorUserBuildSettings.iOSXcodeBuildConfig == XcodeBuildConfig.Debug ? "Debug" : "Release";
            string architecture = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.DeviceSDK ? "iphoneos" :
                PlayerSettings.iOS.simulatorSdkArchitecture == AppleMobileArchitectureSimulator.X86_64 ?
                    "iphonesimulator-x86_64" : "iphonesimulator-arm64";
            string selected = "/" + configuration + "-" + architecture + "/";
            foreach (string guid in AssetDatabase.FindAssets("libLive2DCubismCore", new[] { "Assets/Live2D/Cubism/Plugins/iOS" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".a", StringComparison.Ordinal)) continue;
                var plugin = AssetImporter.GetAtPath(path) as PluginImporter;
                if (plugin == null) throw new BuildFailedException("Cubism native plugin importer missing: " + path);
                plugin.SetCompatibleWithAnyPlatform(false);
                plugin.SetCompatibleWithEditor(false);
                plugin.SetCompatibleWithPlatform(BuildTarget.iOS, path.Contains(selected));
                plugin.SetPlatformData(BuildTarget.iOS, "CPU", architecture == "iphonesimulator-x86_64" ? "x86_64" : "ARM64");
                plugin.SaveAndReimport();
            }
        }
    }
}
