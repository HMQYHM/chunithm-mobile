using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Rendering;
using Live2D.Cubism.Rendering.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChunithmMobile.Runtime
{
    /// <summary>
    /// Owns an imported Cubism model and its built-in-pipeline render resources.
    /// Cubism evaluates model geometry; a camera command buffer draws that geometry
    /// without depending on the SDK's Unity 6 URP RenderGraph/picking path.
    /// </summary>
    public sealed class GameplaySkinModelSurface : IDisposable
    {
        public CubismModel Model { get; private set; }
        public Camera Camera { get; private set; }
        public RenderTexture Texture { get; private set; }
        private RenderTexture maskTexture;
        private CommandBuffer commands;
        private CubismRenderer[] renderers;
        private readonly Dictionary<CubismDrawable, int> drawableIndices = new Dictionary<CubismDrawable, int>();
        private readonly HashSet<CubismDrawable> hidden = new HashSet<CubismDrawable>();
        private Texture2D[] textures;
        private CubismMoc moc;
        private Texture2D backgroundTexture;
        private Mesh backgroundMesh;
        private Material backgroundMaterial;
        private readonly GameplaySkinInfo skin;
        private readonly GameplaySkinStyle style;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<AnimationClip> clips = new List<AnimationClip>();
        private Material maskMaterial;
        private Animation animation;
        private string idleMotion;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        /// <summary>Loads only assets already validated by the skin manager.</summary>
        public GameplaySkinModelSurface(GameplaySkinInfo skin, string modelPath, float aspect, GameplaySkinStyle style = null)
        {
            this.skin = skin;
            this.style = style ?? GameplaySkinManager.Style;
            try
            {
                Shader shader = Resources.Load<Shader>("SkinLive2D/Model");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Live2D built-in shader is unavailable.");
                modelPath = modelPath.Replace('\\', '/');
                // SDK reference resolution prepends "/" when the model is at the
                // package root. An explicit relative directory avoids that artificial
                // absolute path without weakening resource path validation.
                if (!modelPath.Contains("/")) modelPath = "./" + modelPath;
                var source = CubismModel3Json.LoadAtPath(modelPath,
                    (type, path) => GameplaySkinManager.LoadLive2DAsset(skin, type, path));
                if (source == null || source.Moc3 == null)
                    throw new InvalidOperationException("Live2D model3.json or moc3 is missing.");
                if (source.FileReferences.Textures == null || source.FileReferences.Textures.Length > 16)
                    throw new InvalidOperationException("Live2D supports at most 16 texture atlases.");
                textures = source.Textures;
                long pixels = 0;
                foreach (var texture in textures)
                {
                    if (texture == null) throw new InvalidOperationException("A Live2D texture is missing.");
                    pixels += (long)texture.width * texture.height;
                }
                if (pixels > 16777216) throw new InvalidOperationException("Live2D texture budget exceeds 64 MiB.");
                // Keep ownership before renderer setup can fail; ToModel() creates
                // hidden temporary resources that callers cannot clean up on error.
                moc = CubismMoc.CreateFrom(source.Moc3);
                Model = CubismModel.InstantiateFrom(moc);
                if (Model == null) throw new InvalidOperationException("Cubism model creation returned null.");
                if (Model.Offscreens != null && Model.Offscreens.Length > 0)
                    throw new NotSupportedException("Live2D offscreen blend groups require the URP renderer.");
                if (Model.Drawables.Length > 2048) throw new InvalidOperationException("Too many Live2D drawables.");
                var controller = Model.gameObject.AddComponent<CubismRenderController>();
                renderers = controller.DrawableRenderers;
                foreach (var renderer in renderers)
                {
                    var drawable = renderer.Drawable;
                    if (drawable.TextureIndex < 0 || drawable.TextureIndex >= textures.Length)
                        throw new InvalidOperationException("Live2D drawable references a missing texture.");
                    if (drawable.ColorBlend != BlendTypes.ColorBlend.Normal && drawable.ColorBlend != BlendTypes.ColorBlend.Add &&
                        drawable.ColorBlend != BlendTypes.ColorBlend.Multiply || drawable.AlphaBlend != BlendTypes.AlphaBlend.Over)
                        throw new NotSupportedException("Unsupported Live2D blend: " + drawable.ColorBlend + "/" + drawable.AlphaBlend);
                    renderer.Material = CreateMaterial(shader, drawable.ColorBlend);
                    renderer.MainTexture = textures[drawable.TextureIndex];
                }
                Model.ForceUpdateNow();
                for (int i = 0; i < Model.Drawables.Length; i++) drawableIndices.Add(Model.Drawables[i], i);
                var hiddenParts = new HashSet<string>(this.style.Live2DHiddenParts, StringComparer.Ordinal);
                foreach (var drawable in Model.Drawables)
                    if (drawable.ParentPartIndex >= 0 && hiddenParts.Contains(Model.Parts[drawable.ParentPartIndex].Id))
                        hidden.Add(drawable);
                // SDK MeshRenderers are for editor picking. Only our command buffer
                // draws them, so gameplay cameras must never see their picking mesh.
                foreach (var renderer in renderers) renderer.MeshRenderer.forceRenderingOff = true;
                maskMaterial = new Material(shader);
                materials.Add(maskMaterial);
                ConfigureCamera(Mathf.Clamp(aspect, .2f, 5f));
                ConfigureBackground(shader);
                ConfigureMotions(source);
                UpdateFrame();
            }
            catch { Dispose(); throw; }
        }

        private Material CreateMaterial(Shader shader, BlendTypes.ColorBlend blend)
        {
            var material = new Material(shader);
            material.SetInt("_DstBlend", (int)(blend == BlendTypes.ColorBlend.Add ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetInt("_SrcBlend", (int)(blend == BlendTypes.ColorBlend.Multiply ? BlendMode.DstColor : BlendMode.One));
            materials.Add(material);
            return material;
        }

        private void ConfigureCamera(float aspect)
        {
            Camera = new GameObject("Gameplay skin model camera").AddComponent<Camera>();
            Camera.cullingMask = 0;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.clear;
            Camera.orthographic = true;
            Camera.nearClipPlane = .01f;
            Camera.farClipPlane = 100;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;
            var canvas = Model.CanvasInformation;
            float units = Mathf.Max(1f, canvas.PixelsPerUnit);
            float width = canvas.CanvasWidth / units;
            float height = canvas.CanvasHeight / units;
            // Use the authored canvas, not the current pose's bounds: moving paws
            // must not change camera framing or make the whole cat jump.
            Camera.orthographicSize = Mathf.Max(height, width / aspect) * .52f;
            Camera.transform.position = new Vector3(
                (canvas.CanvasWidth * .5f - canvas.CanvasOriginX) / units,
                (canvas.CanvasHeight * .5f - canvas.CanvasOriginY) / units, -10);
            int rtWidth = aspect >= 1 ? 768 : Mathf.RoundToInt(768 * aspect);
            int rtHeight = aspect >= 1 ? Mathf.RoundToInt(768 / aspect) : 768;
            Texture = NewTexture(rtWidth, rtHeight, "Live2D model output");
            maskTexture = NewTexture(rtWidth, rtHeight, "Live2D clipping scratch");
            Camera.targetTexture = Texture;
            commands = new CommandBuffer { name = "Gameplay skin Cubism drawables" };
            Camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
        }

        private static RenderTexture NewTexture(int width, int height, string name)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.Create();
            return texture;
        }

        private void ConfigureBackground(Shader shader)
        {
            if (string.IsNullOrEmpty(style.Live2DBackgroundPath)) return;
            backgroundTexture = GameplaySkinManager.LoadLive2DAsset(skin, typeof(Texture2D), style.Live2DBackgroundPath) as Texture2D;
            if (backgroundTexture == null) throw new InvalidOperationException("Live2D background image is missing.");
            Rect rect = style.Live2DBackgroundRect;
            backgroundMesh = new Mesh { name = "Live2D skin model-space background" };
            backgroundMesh.vertices = new[] { new Vector3(rect.xMin, rect.yMin, 0), new Vector3(rect.xMax, rect.yMin, 0),
                new Vector3(rect.xMax, rect.yMax, 0), new Vector3(rect.xMin, rect.yMax, 0) };
            backgroundMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            backgroundMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            backgroundMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            backgroundMaterial = CreateMaterial(shader, BlendTypes.ColorBlend.Normal);
        }

        /// <summary>Builds draw commands from the latest SDK geometry and clipping masks.</summary>
        public void UpdateFrame()
        {
            if (Model == null || commands == null) return;
            // Force evaluation only for explicit offscreen previews/tests. During play
            // Cubism's player-loop update already evaluates all native model parameters.
            if (!Application.isPlaying) Model.ForceUpdateNow();
            commands.Clear();
            commands.SetRenderTarget(Texture);
            commands.ClearRenderTarget(false, true, Color.clear);
            if (backgroundMesh != null)
            {
                properties.Clear();
                properties.SetTexture("_MainTex", backgroundTexture);
                properties.SetFloat("_Opacity", 1);
                properties.SetColor("_Multiply", Color.white);
                properties.SetColor("_Screen", Color.clear);
                commands.DrawMesh(backgroundMesh, Matrix4x4.identity, backgroundMaterial, 0, 0, properties);
            }
            Array.Sort(renderers, (a, b) => Model.AllDrawObjectsRenderOrder[drawableIndices[a.Drawable]]
                .CompareTo(Model.AllDrawObjectsRenderOrder[drawableIndices[b.Drawable]]));
            foreach (var renderer in renderers)
            {
                var drawable = renderer.Drawable;
                if (hidden.Contains(drawable)) continue;
                if (drawable.IsMasked)
                {
                    commands.SetRenderTarget(maskTexture);
                    commands.ClearRenderTarget(false, true, Color.clear);
                    foreach (var mask in drawable.Masks)
                    {
                        var maskRenderer = mask.GetComponent<CubismRenderer>();
                        properties.Clear();
                        properties.SetTexture("_MainTex", maskRenderer.MainTexture);
                        commands.DrawMesh(maskRenderer.Mesh, Matrix4x4.identity, maskMaterial, 0, 1, properties);
                    }
                    commands.SetRenderTarget(Texture);
                }
                properties.Clear();
                properties.SetTexture("_MainTex", renderer.MainTexture);
                properties.SetTexture("_ClipTex", maskTexture);
                properties.SetFloat("_UseMask", drawable.IsMasked ? 1 : 0);
                properties.SetFloat("_InvertMask", drawable.IsInverted ? 1 : 0);
                properties.SetColor("_Multiply", drawable.MultiplyColor);
                properties.SetColor("_Screen", drawable.ScreenColor);
                properties.SetFloat("_Opacity", Model.GetComponent<CubismRenderController>().Opacity);
                commands.DrawMesh(renderer.Mesh, Matrix4x4.identity, renderer.MeshRenderer.sharedMaterial, 0, 0, properties);
            }
        }

        private void ConfigureMotions(CubismModel3Json source)
        {
            // Motions are data-generated legacy clips. No scripts, sounds or callbacks
            // from the package are executed; only parameter/part curves are accepted.
            var groups = source.FileReferences.Motions;
            if (groups.GroupNames == null) return;
            animation = Model.gameObject.AddComponent<Animation>();
            for (int i = 0; i < groups.GroupNames.Length; i++)
            {
                string name = groups.GroupNames[i];
                bool ruleMotion = style.CustomDisplays.Exists(rule =>
                    rule.Action == "live2dMotion" && rule.Motion == name);
                if (name != style.Live2DIdleMotion && name != style.Live2DLeftPressMotion &&
                    name != style.Live2DRightPressMotion && !ruleMotion) continue;
                if (groups.Motions[i].Length == 0) continue;
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source.AssetPath), groups.Motions[i][0].File);
                var json = GameplaySkinManager.LoadLive2DAsset(skin, typeof(string), path) as string;
                if (json == null) continue;
                var motion = CubismMotion3Json.LoadFrom(json);
                if (motion == null || motion.Curves == null || motion.Curves.Length > 256 ||
                    float.IsNaN(motion.Meta.Duration) || float.IsInfinity(motion.Meta.Duration) ||
                    motion.Meta.Duration < 0 || motion.Meta.Duration > 600)
                    throw new InvalidOperationException("Invalid or oversized Live2D motion.");
                foreach (var curve in motion.Curves)
                {
                    bool validTarget = curve.Target == "Parameter" && Model.Parameters.FindById(curve.Id) != null ||
                        curve.Target == "PartOpacity" && Model.Parts.FindById(curve.Id) != null ||
                        curve.Target == "Model" && curve.Id == "Opacity";
                    if (!validTarget || curve.Segments == null || curve.Segments.Length > 8192)
                        throw new InvalidOperationException("Unsupported Live2D motion binding: " + curve.Id);
                    foreach (var segment in curve.Segments)
                        if (float.IsNaN(segment) || float.IsInfinity(segment))
                            throw new InvalidOperationException("Invalid Live2D motion segment.");
                }
                motion.UserData = null;
                var clip = new AnimationClip { legacy = true };
                motion.ToAnimationClip(clip);
                clip.wrapMode = name == style.Live2DIdleMotion ? WrapMode.Loop : WrapMode.Once;
                animation.AddClip(clip, name);
                clips.Add(clip);
            }
            idleMotion = style.Live2DIdleMotion;
            PlayMotion(idleMotion);
        }

        public void PlayMotion(string name)
        {
            if (animation != null && !string.IsNullOrEmpty(name) && animation.GetClip(name) != null)
                animation.Play(name);
        }

        public void ResumeIdle()
        {
            if (animation != null && !animation.isPlaying) PlayMotion(idleMotion);
        }

        /// <summary>Releases model-specific textures; they are not shared preview-cache entries.</summary>
        public void Dispose()
        {
            if (Camera != null && commands != null) Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            commands?.Release();
            commands = null;
            if (Model != null) Model.gameObject.SetActive(false);
            Release(Model != null ? Model.gameObject : null);
            Release(moc);
            moc = null;
            Release(backgroundTexture);
            Release(backgroundMesh);
            backgroundTexture = null;
            backgroundMesh = null;
            Release(Camera != null ? Camera.gameObject : null);
            if (Texture != null) { Texture.Release(); Release(Texture); }
            if (maskTexture != null) { maskTexture.Release(); Release(maskTexture); }
            foreach (var material in materials) Release(material);
            foreach (var clip in clips) Release(clip);
            if (textures != null) foreach (var texture in textures) Release(texture);
            materials.Clear();
            clips.Clear();
            Model = null;
            Camera = null;
            Texture = null;
            maskTexture = null;
        }

        private static void Release(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
