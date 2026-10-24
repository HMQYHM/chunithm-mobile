using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class InspectOpenHarmonySettings
{
    public static void Run()
    {
        Type type = typeof(PlayerSettings).GetNestedType("OpenHarmony", BindingFlags.Public | BindingFlags.NonPublic);
        Debug.Log($"OPENHARMONY_SETTINGS_TYPE type={type}");
        if (type != null)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                string value = "<unreadable>";
                try { value = Convert.ToString(property.GetValue(null)); } catch (Exception e) { value = e.GetType().Name; }
                Debug.Log($"OPENHARMONY_SETTINGS_PROPERTY name={property.Name} type={property.PropertyType} value={value}");
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                Debug.Log($"OPENHARMONY_SETTINGS_FIELD name={field.Name} type={field.FieldType}");
        }
        Type external = Type.GetType("UnityEditor.OpenHarmony.OpenHarmonyExternalToolsSettings, UnityEditor.OpenHarmony.Extensions");
        Debug.Log($"OPENHARMONY_EXTERNAL_TYPE type={external}");
        if (external != null)
        {
            foreach (string propertyName in new[] { "sdkRootPath", "jdkRootPath", "nodejsRootPath" })
            {
                PropertyInfo property = external.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                string value = "<missing>";
                try { value = Convert.ToString(property?.GetValue(null)); } catch (Exception e) { value = e.GetType().Name; }
                Debug.Log($"OPENHARMONY_EXTERNAL_PROPERTY name={propertyName} value={value}");
            }
        }
        EditorApplication.Exit(0);
    }
}
