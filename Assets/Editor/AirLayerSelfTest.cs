using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class AirLayerSelfTest
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void BuildVerifiedApk()
        {
            Run();
            SkinSettingsSelfTest.BuildVerifiedApk();
        }

        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var root = new GameObject("AIR layer regression");
            var cameraObject = new GameObject("AIR layer camera");
            var owned = new List<UnityEngine.Object>();
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                var heads = Get<Dictionary<string, Material>>(renderer, "headMaterials");
                if (heads.Count == 0) typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                var ground = heads["tap_04"];
                var air = new List<Material>();
                foreach (string field in new[] { "airHoldBody", "heavenRoof", "heavenRoofPink", "heavenWall",
                    "airActionTrailRainbowBody", "airActionTrailGreenBody", "airActionBarBody", "airLadderTop",
                    "airLadderPost", "airLadderEdge", "airLadderStem", "airLadderRim", "airActionTrailGreenLine",
                    "airActionTrailRainbowLine", "defaultAirHead", "defaultAirArrow" })
                    air.Add(Get<Material>(renderer, field));
                air.AddRange(heads.Where(pair => pair.Key.StartsWith("air_") || pair.Key.StartsWith("ah")).Select(pair => pair.Value));
                air.AddRange(Get<Dictionary<string, Material>>(renderer, "airLineMaterials").Values);
                air.AddRange(Get<Dictionary<string, Material>>(renderer, "airLineBodyMaterials").Values);
                Require(air.All(material => material.renderQueue > ground.renderQueue), "AIR pass overlaps ground queue");
                renderer.RefreshSkin();
                Require(Get<Material>(renderer, "airActionTrailGreenLine").color.a == 1f &&
                    Get<Material>(renderer, "airActionTrailGreenBody").color.a == .28f &&
                    Get<Material>(renderer, "airHoldBody").color.a == .28f &&
                    Get<Material>(renderer, "airActionBarBody").color.a == .28f,
                    "skin refresh merged core and outer opacity");

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                var lightObject = new GameObject("AIR test light");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1;
                var red = Texture(Color.red, owned);
                var green = Texture(new Color(0, 1, 0, .65f), owned);
                var hole = Texture(new Color(0, 1, 0, 0), owned);
                var blue = Texture(Color.blue, owned);
                // Use every distinct AIR shader/queue pair, not a replacement test shader.
                foreach (Material source in air.GroupBy(m => m.shader.name + ":" + m.renderQueue).Select(g => g.First())) {
                    foreach (bool reverse in new[] { false, true }) {
                        var stage = new GameObject("overlap");
                        stage.transform.SetParent(root.transform);
                        var groundCopy = Copy(ground, red, owned);
                        var airCopy = Copy(source, green, owned);
                        if (reverse) {
                            Quad(stage.transform, airCopy, 1);
                            Quad(stage.transform, groundCopy, 0);
                        } else {
                            Quad(stage.transform, groundCopy, 0);
                            Quad(stage.transform, airCopy, 1);
                        }
                        Color pixel = Sample(camera, 320, 180);
                        bool solidCore = source.shader.name == "ChunithmMobile/AirTrailCore";
                        Require(solidCore ? pixel.g > .8f && pixel.r < .1f : pixel.g > .25f && pixel.r > .1f,
                            "AIR core/sheet opacity wrong: " + source.shader.name);
                        airCopy.mainTexture = hole;
                        pixel = Sample(camera, 320, 180);
                        Require(solidCore ? pixel.g > .8f && pixel.r < .1f : pixel.r > .8f && pixel.g < .1f,
                            "core must stay opaque; outer texture holes must remain transparent");
                        airCopy.mainTexture = green;
                        // A later AIR cap farther away must not fail a depth write from the sheet.
                        var headCopy = Copy(heads["air_up_04"], blue, owned);
                        headCopy.renderQueue = Math.Max(headCopy.renderQueue, airCopy.renderQueue + 1);
                        Quad(stage.transform, headCopy, 2);
                        pixel = Sample(camera, 320, 180);
                        Require(pixel.b > .8f && pixel.g < .1f, "transparent surface wrote blocking depth");
                        UnityEngine.Object.DestroyImmediate(stage);
                    }
                }
                // At equal semantic priority, near AIR must cover far AIR regardless of creation order.
                foreach (bool reverse in new[] { false, true }) {
                    var stage = new GameObject("air distance sort");
                    stage.transform.SetParent(root.transform);
                    Material near = Copy(heads["ahtap_04"], green, owned);
                    Material far = Copy(heads["ahtap_04"], blue, owned);
                    if (reverse) { Quad(stage.transform, near, 0); Quad(stage.transform, far, 2); }
                    else { Quad(stage.transform, far, 2); Quad(stage.transform, near, 0); }
                    Color pixel = Sample(camera, 320, 180);
                    Require(pixel.g > pixel.b, "same-layer AIR ignores camera distance");
                    UnityEngine.Object.DestroyImmediate(stage);
                }
                // A real opaque occluder still works; the fix must not turn depth testing off globally.
                var opaque = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
                owned.Add(opaque);
                Quad(root.transform, opaque, -1);
                Quad(root.transform, Copy(heads["air_up_04"], blue, owned), 1);
                Require(Sample(camera, 320, 180).r > .8f, "AIR depth test disabled globally");
                Debug.Log("AIR layer regression passed: all air queues, transparency holes, no depth writes, creation-order independence and near/far sorting.");
            }
            finally {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var item in owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
        }

        private static Texture2D Texture(Color color, List<UnityEngine.Object> owned)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, 4).ToArray());
            texture.Apply(); owned.Add(texture); return texture;
        }
        private static Material Copy(Material source, Texture texture, List<UnityEngine.Object> owned)
        {
            var material = new Material(source) { mainTexture = texture, color = Color.white };
            owned.Add(material); return material;
        }
        private static void Quad(Transform parent, Material material, float depth)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0, 0, depth);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static Color Sample(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                camera.targetTexture = target; camera.aspect = (float)width / height;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                return texture.GetPixel(width / 2, height / 2);
            }
            finally {
                camera.targetTexture = null; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
