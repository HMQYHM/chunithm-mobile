using System;
using System.IO;
using System.IO.Compression;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    /// <summary>Checks the complete PNG digit skin path, including text fallback.</summary>
    public static class GameplaySkinDigitsSelfTest
    {
        public static void Run()
        {
            string id = "digit_test_" + Guid.NewGuid().ToString("N");
            string archivePath = Path.Combine("Build", id + ".cmgameplay");
            GameplaySkinInfo imported = null;
            Texture2D texture = new Texture2D(2, 2);
            GameObject root = null;
            try
            {
                byte[] png = texture.EncodeToPNG();
                using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("skin.json").Open()))
                        writer.Write("{\"id\":\"" + id + "\",\"name\":\"Digit test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    using (var writer = new StreamWriter(archive.CreateEntry("ui/gameplay-ui.json").Open()))
                        writer.Write("{\"combo\":{\"digits\":[{\"digit\":\"1\",\"image\":\"ui/numbers/combo/1.png\"},{\"digit\":\"2\",\"image\":\"ui/numbers/combo/2.png\"}]}}");
                    foreach (string digit in new[] { "1", "2" })
                    {
                        using (Stream output = archive.CreateEntry("ui/numbers/combo/" + digit + ".png").Open())
                            output.Write(png, 0, png.Length);
                    }
                }
                imported = GameplaySkinManager.InstallPackage(archivePath);
                Require(GameplaySkinManager.SetActive(id), "digit skin activation failed");
                Require(GameplaySkinManager.LoadDigitSprite("combo", '1') != null &&
                    GameplaySkinManager.LoadDigitSprite("combo", '2') != null,
                    "digit PNGs were not loaded");
                Require(GameplaySkinManager.LoadDigitSprite("combo", '3') == null,
                    "incomplete digit set did not report fallback");

                root = new GameObject("Digit renderer test", typeof(Text));
                Text text = root.GetComponent<Text>();
                text.text = "12\nCOMBO";
                var renderer = root.AddComponent<GameplaySkinDigitRenderer>();
                renderer.Initialize(text, "combo");
                Require(text.color.a == 0f && root.transform.childCount == 1,
                    "complete digit value did not switch to image rendering");
                text.text = "13\nCOMBO";
                // SendMessage trips ShouldRunBehaviour in batch edit mode. Invoke the
                // same update method directly so fallback verification is clean.
                typeof(GameplaySkinDigitRenderer).GetMethod("LateUpdate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(renderer, null);
                Require(text.color.a > 0f, "missing digit did not restore text fallback");
                Debug.Log("Gameplay skin digit regression passed: import, PNG lookup, image rendering and text fallback.");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                GameplaySkinManager.SetActive(GameplaySkinManager.BuiltinId);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                if (File.Exists(archivePath)) File.Delete(archivePath);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
