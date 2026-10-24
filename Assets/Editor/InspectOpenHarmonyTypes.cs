using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class InspectOpenHarmonyTypes
{
    public static void Run()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch { continue; }
            foreach (Type type in types.Where(t => t.FullName != null && t.FullName.IndexOf("OpenHarmony", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string members = string.Join(",", type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(m => m.Name.IndexOf("sdk", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => m.MemberType + ":" + m.Name));
                Debug.Log($"OPENHARMONY_TYPE name={type.FullName} assembly={assembly.GetName().Name} members={members}");
            }
        }
        EditorApplication.Exit(0);
    }
}
