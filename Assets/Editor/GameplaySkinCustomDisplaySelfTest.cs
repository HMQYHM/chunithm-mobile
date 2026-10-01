using System;
using System.IO;
using System.IO.Compression;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>
    /// Verifies declarative custom-display parsing without requiring a Live2D
    /// model. This catches regressions where the rules are parsed only inside
    /// the optional live2d manifest block.
    /// </summary>
    public static class GameplaySkinCustomDisplaySelfTest
    {
        public static void Run()
        {
            string package = Path.Combine("Build", "custom-display-regression.zip");
            Texture2D fixtureTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            GameplaySkinInfo imported = null;
            string previous = GameplaySkinManager.BuiltinId;
            string id = "custom_display_regression_" + Guid.NewGuid().ToString("N");
            try
            {
                GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
                previous = GameplaySkinManager.Active.Id;
                using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
                {
                    using (StreamWriter writer = new StreamWriter(archive.CreateEntry("skin.json").Open()))
                        writer.Write("{\"id\":\"" + id + "\",\"name\":\"Custom display test\",\"gameplaySkin\":true,\"formatVersion\":1}");
                    using (StreamWriter writer = new StreamWriter(archive.CreateEntry("ui/gameplay-ui.json").Open()))
                        writer.Write("{\"customDisplays\":[" +
                            "{\"id\":\"combo100\",\"trigger\":\"combo\",\"condition\":\"equal\",\"threshold\":100,\"image\":\"ui/custom/combo100.png\"}," +
                            "{\"id\":\"airRise\",\"trigger\":\"air\",\"value\":\"rising\",\"image\":\"ui/custom/air.png\"}," +
                            "{\"id\":\"rankLoss\",\"trigger\":\"displayed_metric\",\"mode\":\"sss+\",\"condition\":\"less\",\"threshold\":0,\"image\":\"ui/custom/air.png\"}," +
                            "{\"id\":\"badPath\",\"trigger\":\"combo\",\"threshold\":1,\"image\":\"../outside.png\"}," +
                            "{\"id\":\"combo100\",\"trigger\":\"combo\",\"threshold\":2,\"image\":\"ui/custom/duplicate.png\"}]}" );
                    byte[] png = fixtureTexture.EncodeToPNG();
                    using (Stream output = archive.CreateEntry("ui/custom/combo100.png").Open())
                        output.Write(png, 0, png.Length);
                    using (Stream output = archive.CreateEntry("ui/custom/air.png").Open())
                        output.Write(png, 0, png.Length);
                }

                imported = GameplaySkinManager.InstallPackage(package);
                Require(imported != null, "custom display fixture was not imported");
                Require(GameplaySkinManager.SetActive(imported.Id), "custom display fixture was not activated");
                Require(GameplaySkinManager.Style.CustomDisplays.Count == 3,
                    "custom displays were not parsed independently of Live2D");
                Require(GameplaySkinManager.Style.CustomDisplays[0].Id == "combo100" &&
                    GameplaySkinManager.Style.CustomDisplays[1].Trigger == "air",
                    "custom display rules were parsed in the wrong shape");
                Texture2D decoded = GameplaySkinManager.LoadCustomDisplayImage(
                    imported, "ui/custom/combo100.png", 16L * 1024L * 1024L);
                Require(decoded != null && decoded.width == 2 && decoded.height == 2,
                    "custom display PNG was not decoded");
                UnityEngine.Object.DestroyImmediate(decoded);
                Require(GameplaySkinManager.LoadCustomDisplayImage(
                    imported, "../outside.png", 16L * 1024L * 1024L) == null,
                    "custom display path escaped the skin directory");
                Debug.Log("Custom display regression passed: non-Live2D parsing, duplicate/path rejection, bounded PNG decode.");
            }
            finally
            {
                GameplaySkinManager.SetActive(previous);
                if (imported != null) GameplaySkinManager.DeleteSkin(imported);
                if (File.Exists(package)) File.Delete(package);
                UnityEngine.Object.DestroyImmediate(fixtureTexture);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
