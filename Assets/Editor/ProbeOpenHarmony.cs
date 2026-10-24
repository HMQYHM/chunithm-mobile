using UnityEditor;
using UnityEngine;

public static class ProbeOpenHarmony
{
    public static void Run()
    {
        // Keep this probe compilable in Unity editors where the OpenHarmony
        // extension is not installed. The enum values are supplied by the
        // extension at runtime when it is available.
        if (!TryGetTargets(out BuildTargetGroup group, out BuildTarget target))
        {
            Debug.Log("OPENHARMONY_SUPPORT_PROBE supported=false reason=platform-extension-missing");
            EditorApplication.Exit(42);
            return;
        }

        bool supported = BuildPipeline.IsBuildTargetSupported(group, target);
        Debug.Log($"OPENHARMONY_SUPPORT_PROBE supported={supported}");
        EditorApplication.Exit(supported ? 0 : 42);
    }

    private static bool TryGetTargets(out BuildTargetGroup group, out BuildTarget target)
    {
        group = default;
        target = default;
        try
        {
            group = (BuildTargetGroup)System.Enum.Parse(typeof(BuildTargetGroup), "OpenHarmony");
            target = (BuildTarget)System.Enum.Parse(typeof(BuildTarget), "OpenHarmony");
            return true;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }
}
