using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    // Verifies that a partial skin never hides the built-in gameplay layer.
    public static class GameplaySkinFallbackSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            string package = Path.Combine("Build", "skin-fallback-regression.zip");
            GameplaySkinInfo imported = null;
            GameObject root = null;
            GameObject cameraObject = null;
            Texture2D baseTexture = null;
            Texture2D customTexture = null;
            Sprite baseSprite = null;
            Sprite staleOverride = null;
            string id = "skin_fallback_regression_" + Guid.NewGuid().ToString("N");
            try
            {
                GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
                using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
                {
                    using (StreamWriter writer = new StreamWriter(archive.CreateEntry("skin.json").Open()))
                        writer.Write("{\"id\":\"" + id + "\",\"name\":\"Fallback test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    using (StreamWriter writer = new StreamWriter(archive.CreateEntry("ui/gameplay-ui.json").Open()))
                        writer.Write("{\"elements\":[{\"id\":\"songCover\",\"visible\":true,\"image\":\"ui/missing-cover.png\"}]}");
                }

                imported = GameplaySkinManager.InstallPackage(package);
                Require(imported != null && GameplaySkinManager.SetActive(imported.Id),
                    "fallback skin was not activated");
                GameplaySkinElementDefinition cover = GameplaySkinManager.Style.LayoutElement("songCover");
                Require(cover != null && GameplaySkinManager.LoadSkinSprite(imported, cover.Image) == null,
                    "missing cover was treated as a valid skin asset");
                Require(GameplaySkinManager.Style.LayoutElementVisible("scoreHud"),
                    "omitted score HUD was hidden instead of using the original");

                root = new GameObject("Gameplay skin fallback check");
                AppUiShell shell = root.AddComponent<AppUiShell>();
                Set(shell, "settings", new AppSettings());
                Font font = Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular");
                Set(shell, "font", font); Set(shell, "titleFont", font);
                GameObject canvasObject = new GameObject("Canvas", typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Set(shell, "canvas", canvas);
                cameraObject = new GameObject("Gameplay skin fallback camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                canvas.worldCamera = camera;
                Invoke(shell, "BuildGameplay");

                Image coverImage = Get<Image>(shell, "gameplaySongInfoCover");
                baseTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                customTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                baseSprite = Sprite.Create(baseTexture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
                staleOverride = Sprite.Create(customTexture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
                coverImage.sprite = baseSprite;
                coverImage.overrideSprite = staleOverride;
                Invoke(shell, "ApplyGameplaySkinLayout", GameplaySkinManager.Style);
                Require(coverImage.overrideSprite == baseSprite && coverImage.sprite == baseSprite,
                    "omitted or missing skin cover did not restore the original sprite");
                Debug.Log("Gameplay skin fallback regression passed: omitted elements and missing song cover preserve built-in UI.");
            }
            finally
            {
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                if (File.Exists(package)) File.Delete(package);
                if (baseSprite != null) UnityEngine.Object.DestroyImmediate(baseSprite);
                if (staleOverride != null) UnityEngine.Object.DestroyImmediate(staleOverride);
                if (baseTexture != null) UnityEngine.Object.DestroyImmediate(baseTexture);
                if (customTexture != null) UnityEngine.Object.DestroyImmediate(customTexture);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static T Get<T>(object target, string name) where T : class
        {
            return typeof(AppUiShell).GetField(name, Private)?.GetValue(target) as T;
        }

        private static void Set(object target, string name, object value)
        {
            typeof(AppUiShell).GetField(name, Private)?.SetValue(target, value);
        }

        private static void Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = typeof(AppUiShell).GetMethod(name, Private);
            method.Invoke(target, args);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
