using System.IO;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ExportOpenHarmony
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string DefaultOutput = "Build/HarmonyOS/CHUNITHM-Mobile-Unity-OpenHarmony-0.3.7-export-r2";

    public static void Run()
    {
        ConfigureExternalTools();
        string output = System.Environment.GetEnvironmentVariable("CHUNITHM_OPENHARMONY_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = DefaultOutput;
        Directory.CreateDirectory(output);

        PlayerSettings.productName = "Chunithm Mobile";
        PlayerSettings.companyName = "HMQYHM";
        PlayerSettings.bundleVersion = "0.3.7";

        if (!TryGetTargets(out BuildTargetGroup targetGroup, out BuildTarget target))
            throw new BuildFailedException("OpenHarmony platform support is not installed.");

        if (!BuildPipeline.IsBuildTargetSupported(targetGroup, target))
            throw new BuildFailedException("OpenHarmony platform support is not installed.");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });

        Debug.Log($"OPENHARMONY_EXPORT result={report.summary.result} output={output} size={report.summary.totalSize}");
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException(report.summary.result.ToString());
        EditorApplication.Exit(0);
    }

    private static bool TryGetTargets(out BuildTargetGroup group, out BuildTarget target)
    {
        group = default;
        target = default;
        try
        {
            group = (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), "OpenHarmony");
            target = (BuildTarget)Enum.Parse(typeof(BuildTarget), "OpenHarmony");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ConfigureExternalTools()
    {
        string sdk = System.Environment.GetEnvironmentVariable("CHUNITHM_OPENHARMONY_SDK");
        if (string.IsNullOrWhiteSpace(sdk))
            sdk = "/Users/wangqianyi/Applications/DevEco-Studio-copy.app/Contents/sdk/default/openharmony";
        string jdk = System.Environment.GetEnvironmentVariable("CHUNITHM_OPENHARMONY_JDK");
        if (string.IsNullOrWhiteSpace(jdk))
            jdk = "/Users/wangqianyi/Applications/DevEco-Studio-copy.app/Contents/jbr/Contents/Home";
        string node = System.Environment.GetEnvironmentVariable("CHUNITHM_OPENHARMONY_NODE");
        if (string.IsNullOrWhiteSpace(node))
            node = "/Users/wangqianyi/Applications/DevEco-Studio-copy.app/Contents/tools/node";

        Type settingsType = Type.GetType("UnityEditor.OpenHarmony.OpenHarmonyExternalToolsSettings, UnityEditor.OpenHarmony.Extensions");
        if (settingsType == null) throw new BuildFailedException("OpenHarmony external tools settings are unavailable.");
        SetPath(settingsType, "sdkRootPath", sdk);
        SetPath(settingsType, "jdkRootPath", jdk);
        SetPath(settingsType, "nodejsRootPath", node);
        Debug.Log($"OPENHARMONY_TOOLS sdk={sdk} jdk={jdk} node={node}");
    }

    private static void SetPath(Type settingsType, string propertyName, string value)
    {
        if (!Directory.Exists(value)) throw new BuildFailedException($"OpenHarmony tool path does not exist: {value}");
        PropertyInfo property = settingsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property == null || !property.CanWrite) throw new BuildFailedException($"OpenHarmony setting is not writable: {propertyName}");
        property.SetValue(null, value);
    }
}
