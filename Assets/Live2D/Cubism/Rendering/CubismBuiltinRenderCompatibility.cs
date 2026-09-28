/**
 * Runtime compatibility layer for the built-in render pipeline.
 *
 * Cubism 5.3 splits several renderer members into a RenderGraph-specific
 * partial class. This project uses Unity's built-in pipeline, so the small
 * subset below keeps model creation, mesh updates and normal MeshRenderer
 * drawing available without compiling the incompatible URP extension.
 */
using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Rendering.Util;
using UnityEngine;

namespace Live2D.Cubism.Rendering
{
    public sealed partial class CubismRenderer
    {
        /// <summary>Vertices for the offscreen fallback quad.</summary>
        internal static readonly Vector3[] OffscreenVertices =
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
        };

        /// <summary>UV coordinates for the offscreen fallback quad.</summary>
        internal static readonly Vector2[] OffscreenUVs =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };

        /// <summary>Triangle indices for the offscreen fallback quad.</summary>
        internal static readonly int[] OffscreenTriangle = { 0, 1, 2, 0, 2, 3 };

        [SerializeField, HideInInspector]
        private CubismRenderer[] _masks;
        private Mesh _offscreenMesh;
        private int _previousOffscreenUnmanagedIndex;
        private MaterialPropertyBlock _propertyBlock;

        /// <summary>Drawable or offscreen object represented by this renderer.</summary>
        public CubismOffscreen Offscreen { get; set; }

        /// <summary>Runtime draw-object kind selected by the model loader.</summary>
        [SerializeField] public CubismModelTypes.DrawObjectType DrawObjectType;

        /// <summary>Blend values are kept for skin/material compatibility.</summary>
        [SerializeField] public BlendTypes.ColorBlend ColorBlendType;
        [SerializeField] public BlendTypes.AlphaBlend AlphaBlendType;

        internal int DrawObjectUnmanagedIndex { get; set; }
        internal bool IsLastDrawObjectInModel { get; set; }
        public bool SkipRendering { get; set; }

        internal Vector3 LastDirection;
        internal float DistanceToCamera;

        private MaterialPropertyBlock PropertyBlock =>
            _propertyBlock ?? (_propertyBlock = new MaterialPropertyBlock());

        /// <summary>Preserves the original renderer's order for sorting updates.</summary>
        internal void SetDrawObjectRenderOrder(int newRenderOrder)
        {
            RenderOrder = newRenderOrder;
            ApplySorting();
        }

        /// <summary>Reports whether depth sorting needs to be recalculated.</summary>
        internal bool DidUpdateDirectionFromLastSorted(Vector3 cameraPosition)
        {
            return LastDirection != transform.position - cameraPosition;
        }

        /// <summary>Stores the current camera-relative depth for sorting.</summary>
        internal void CalculateDistanceToCamera(Vector3 cameraPosition, Vector3 cameraForward)
        {
            LastDirection = transform.position - cameraPosition;
            DistanceToCamera = Vector3.Project(LastDirection, cameraForward).magnitude;
        }

        /// <summary>Initializes masks as ordinary child renderers in built-in mode.</summary>
        private void TryInitializeMasks()
        {
            _masks = Array.Empty<CubismRenderer>();
        }

        /// <summary>Initializes per-drawable state used by the base renderer.</summary>
        private void InitializeDrawObject()
        {
            if (DrawObjectType == CubismModelTypes.DrawObjectType.Drawable && Drawable != null)
                DrawObjectUnmanagedIndex = Drawable.UnmanagedIndex;
            else if (Offscreen != null)
                DrawObjectUnmanagedIndex = Offscreen.UnmanagedIndex;
        }

        /// <summary>Completes built-in renderer setup after mesh/material creation.</summary>
        public void OnAfterAllRendererInitialize()
        {
            TryInitializeMasks();
            UpdateVisibility();
        }

        /// <summary>Placeholder for the URP offscreen resource property.</summary>
        public RenderTexture OffscreenFrameBuffer { get; set; }
    }

    public sealed partial class CubismRenderController
    {
        internal bool HasRootPartOffscreen = true;

        [SerializeField, HideInInspector]
        private bool _hasMask;

        /// <summary>Whether the model contains masked drawables.</summary>
        public bool HasMask
        {
            get => _hasMask;
            set => _hasMask = value;
        }

        public RenderTexture CurrentFrameBuffer { get; set; }
        public int CurrentOffscreenUnmanagedIndex { get; set; }

        [SerializeField, HideInInspector]
        private int _groupedSortingIndex;

        /// <summary>Group index used by the existing controller grouping code.</summary>
        public int GroupedSortingIndex
        {
            get => _groupedSortingIndex;
            set => _groupedSortingIndex = value;
        }

        internal bool DidChangeDrawableRenderOrder;
        internal bool DidChangeSorting;
        private CubismRenderer[] _sortedRenderers;
        private CubismRenderer[] _drawableRenderers;
        private CubismRenderer[] _offscreenRenderers;

        /// <summary>Renderers ordered by the model's draw order.</summary>
        public CubismRenderer[] SortedRenderers
        {
            get
            {
                if (_sortedRenderers == null) SortRenderers();
                return _sortedRenderers;
            }
        }

        /// <summary>Renderers created for model drawables.</summary>
        public CubismRenderer[] DrawableRenderers
        {
            get
            {
                if (_drawableRenderers == null && Model != null && Model.Drawables != null)
                    _drawableRenderers = Model.Drawables.GetComponentsMany<CubismRenderer>();
                return _drawableRenderers;
            }
        }

        /// <summary>Renderers created for model offscreen objects.</summary>
        public CubismRenderer[] OffscreenRenderers
        {
            get
            {
                if (_offscreenRenderers == null && Model?.Offscreens != null)
                    _offscreenRenderers = Model.Offscreens.GetComponentsMany<CubismRenderer>();
                return _offscreenRenderers;
            }
        }

        /// <summary>Creates the ordinary MeshRenderer set used by built-in mode.</summary>
        private void TryInitializeRenderers(CubismRenderer[] existing)
        {
            if (existing != null && existing.Length > 0) return;
            if (Model == null || Model.Drawables == null) return;

            var renderers = new List<CubismRenderer>();
            var drawableRenderers = Model.Drawables.AddComponentEach<CubismRenderer>();
            _drawableRenderers = drawableRenderers;
            for (var i = 0; i < drawableRenderers.Length; i++)
            {
                drawableRenderers[i].DrawObjectType = CubismModelTypes.DrawObjectType.Drawable;
                drawableRenderers[i].Drawable = Model.Drawables[i];
                renderers.Add(drawableRenderers[i]);
            }

            if (Model.Offscreens != null)
            {
                var offscreenRenderers = Model.Offscreens.AddComponentEach<CubismRenderer>();
                _offscreenRenderers = offscreenRenderers;
                for (var i = 0; i < offscreenRenderers.Length; i++)
                {
                    offscreenRenderers[i].DrawObjectType = CubismModelTypes.DrawObjectType.Offscreen;
                    offscreenRenderers[i].Offscreen = Model.Offscreens[i];
                    renderers.Add(offscreenRenderers[i]);
                }
            }

            Renderers = renderers.ToArray();
            _sortedRenderers = null;
        }

        /// <summary>Applies model draw order to Unity sorting order.</summary>
        private void OnAfterRenderersInitialize(CubismRenderer[] renderers)
        {
            if (renderers == null) return;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var order = renderer.DrawObjectType == CubismModelTypes.DrawObjectType.Drawable
                    ? renderer.Drawable.UnmanagedIndex
                    : DrawableRenderers.Length + renderer.Offscreen.UnmanagedIndex;
                if (Model.AllDrawObjectsRenderOrder != null && order < Model.AllDrawObjectsRenderOrder.Length)
                    renderer.SetDrawObjectRenderOrder(Model.AllDrawObjectsRenderOrder[order]);
                renderer.OnAfterAllRendererInitialize();
            }
            SortRenderers();
        }

        /// <summary>Built-in mode does not use nested URP offscreen passes.</summary>
        private bool CheckHasRootPartOffscreen(CubismRenderer targetRenderer) => false;

        private void SortRenderers()
        {
            if (Renderers == null) return;
            var list = new List<CubismRenderer>(Renderers);
            list.Sort((a, b) => a.MeshRenderer.sortingOrder.CompareTo(b.MeshRenderer.sortingOrder));
            _sortedRenderers = list.ToArray();
        }
    }
}
