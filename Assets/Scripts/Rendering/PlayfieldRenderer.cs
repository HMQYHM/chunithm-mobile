using UnityEngine;
using ChunithmMobile.Runtime;

namespace ChunithmMobile.Rendering
{
    // 轨道底板渲染器：绘制轨道、判定线、触控板和边界层。
    // 视觉开关只改变显示，不应关闭 ArcadeInputRig 的采样逻辑。
    /// <summary>
    /// Builds the gameplay surface from small, predictable quads.
    /// Keeping the field in ChartSpace avoids the skew and oversized strips
    /// caused by importing the arcade SVO meshes directly.
    /// </summary>
    public sealed class PlayfieldRenderer : MonoBehaviour
    {
        [Range(0, 16)] public int LaneLineCount = 16;
        [Range(.01f, .12f)] public float LaneLineWidth = .035f;
        [Range(.02f, .25f)] public float GoldenLineThickness = .08f;
        [Range(-1f, 2f)] public float GoldenLineZ = ChartSpace.JudgementZ;

        private Material trackMaterial;
        private Material laneMaterial;
        private Material guideMaterial;
        private Material edgeMaterial;
        private Material goldMaterial;
        private bool initialized;
        private Transform trackRoot;

        private void Awake()
        {
            GameplaySkinStyle skin = GameplaySkinManager.Style;
            trackMaterial = MeshFactory.Material(GameplaySkinManager.LoadSceneTexture(skin.TrackTexturePath) ?? GameplaySkinManager.LoadTrackTexture("field_blue_16"), skin.TrackColor, true);
            laneMaterial = MeshFactory.Material(GameplaySkinManager.LoadSceneTexture(skin.LaneTexturePath) ?? GameplaySkinManager.LoadTrackTexture("barline_blue"), skin.LaneColor);
            guideMaterial = MeshFactory.Material(skin.GuideColor);
            // goal_line contains transparent glow pixels. Use the transparent
            // material path so its edge does not depth-fight the track when
            // notes cross the far/start portion of the field.
            edgeMaterial = MeshFactory.Material(GameplaySkinManager.LoadTrackTexture("goal_line"), skin.EdgeColor, true);
            goldMaterial = MeshFactory.Material(GameplaySkinManager.LoadTrackTexture("goal_line"), skin.JudgementLineColor);

            // The field is opaque and should be behind all note materials.
            trackMaterial.renderQueue = 2000;
            guideMaterial.renderQueue = 2420;
            laneMaterial.renderQueue = 2430;
            edgeMaterial.renderQueue = 2440;
            goldMaterial.renderQueue = 3020;

            initialized = true;
            Build();
        }

        public void RefreshSkin()
        {
            if (!initialized) return;
            GameplaySkinStyle skin = GameplaySkinManager.Style;
            trackMaterial.mainTexture = GameplaySkinManager.LoadSceneTexture(skin.TrackTexturePath) ?? GameplaySkinManager.LoadTrackTexture("field_blue_16");
            trackMaterial.color = skin.TrackColor;
            laneMaterial.mainTexture = GameplaySkinManager.LoadSceneTexture(skin.LaneTexturePath) ?? GameplaySkinManager.LoadTrackTexture("barline_blue");
            laneMaterial.color = skin.LaneColor;
            guideMaterial.mainTexture = null;
            guideMaterial.color = skin.GuideColor;
            edgeMaterial.mainTexture = GameplaySkinManager.LoadTrackTexture("goal_line");
            edgeMaterial.color = skin.EdgeColor;
            goldMaterial.mainTexture = GameplaySkinManager.LoadTrackTexture("goal_line");
            goldMaterial.color = skin.JudgementLineColor;
            Build();
        }

        public void ConfigureTopWidth(int percent)
        {
            bool changed = ChartSpace.SetFarWidthPercent(percent);
            if (changed && initialized) Build();
        }

        public void Build()
        {
            // The component shares its host with note playback. Only rebuild
            // geometry owned by the field, never notes or their pool.
            if (trackRoot == null) {
                trackRoot = new GameObject("Track geometry").transform;
                trackRoot.SetParent(transform, false);
            }
            for (int i = trackRoot.childCount - 1; i >= 0; i--) {
                GameObject child = trackRoot.GetChild(i).gameObject;
                child.SetActive(false);
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Release(filter.sharedMesh);
                Release(child);
            }

            CreateQuad(
                "Track",
                -ChartSpace.NearHalfWidth, ChartSpace.NearHalfWidth, ChartSpace.NearZ,
                -ChartSpace.FarHalfWidth, ChartSpace.FarHalfWidth, ChartSpace.FarZ,
                -.035f,
                trackMaterial);

            CreateDepthGuides();
            CreateLaneLines();
            CreateGoldenLine();
        }

        private static void Release(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void CreateDepthGuides()
        {
            // These are intentionally quiet: they establish depth without
            // competing with the notes or turning into a second judgement bar.
            float[] depths = { 8f, 16f, 24f, 32f, 40f };
            for (int i = 0; i < depths.Length; i++)
            {
                float z = depths[i];
                float half = ChartSpace.HalfWidthAt(z);
                CreateQuad(
                    "Depth guide " + (i + 1),
                    -half, half, z - .018f,
                    -half, half, z + .018f,
                    -.005f,
                    guideMaterial);
            }
        }

        private void CreateLaneLines()
        {
            if (LaneLineCount <= 0) return;

            CreateLongitudinalLine(
                "Left edge",
                0f,
                .09f,
                edgeMaterial,
                .012f);
            CreateLongitudinalLine(
                "Right edge",
                ChartSpace.Lanes,
                .09f,
                edgeMaterial,
                .006f);

            for (int i = 1; i < LaneLineCount; i++)
            {
                float lane = i * (float)ChartSpace.Lanes / LaneLineCount;
                CreateLongitudinalLine(
                    "Lane " + i,
                    lane,
                    LaneLineWidth,
                    laneMaterial,
                    .008f);
            }
        }

        private void CreateGoldenLine()
        {
            float z = Mathf.Clamp(GoldenLineZ, 0f, ChartSpace.FarZ);
            float half = ChartSpace.HalfWidthAt(z) + .12f;
            float thickness = Mathf.Max(.02f, GoldenLineThickness);

            CreateQuad(
                "Golden judgement line",
                -half, half, z - thickness * .5f,
                -half, half, z + thickness * .5f,
                ChartSpace.GroundY,
                goldMaterial);
        }

        private void CreateLongitudinalLine(
            string name,
            float lane,
            float width,
            Material material,
            float y)
        {
            float nearX = ChartSpace.LaneX(lane, ChartSpace.NearZ);
            float farX = ChartSpace.LaneX(lane, ChartSpace.FarZ);
            // Lane lines belong to the same tapered surface as the track.
            // Keeping one world-space thickness at both ends made the far
            // lanes visibly wider than the field on small/top-width layouts.
            float nearScale = ChartSpace.HalfWidthAt(ChartSpace.NearZ) / ChartSpace.NearHalfWidth;
            float farScale = ChartSpace.HalfWidthAt(ChartSpace.FarZ) / ChartSpace.NearHalfWidth;
            float nearHalfWidth = Mathf.Max(.004f, width * .5f * nearScale);
            float farHalfWidth = Mathf.Max(.002f, width * .5f * farScale);

            CreateQuad(
                name,
                nearX - nearHalfWidth, nearX + nearHalfWidth, ChartSpace.NearZ,
                farX - farHalfWidth, farX + farHalfWidth, ChartSpace.FarZ,
                y,
                material);
        }

        private void CreateQuad(
            string name,
            float nearLeftX,
            float nearRightX,
            float nearZ,
            float farLeftX,
            float farRightX,
            float farZ,
            float y,
            Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(trackRoot, false);

            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = MeshFactory.Quad(
                new Vector3(nearLeftX, y, nearZ),
                new Vector3(nearRightX, y, nearZ),
                new Vector3(farRightX, y, farZ),
                new Vector3(farLeftX, y, farZ));

            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
