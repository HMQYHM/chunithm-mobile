using System;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Runtime;
using ChunithmMobile.Judgement;
using UnityEngine;

namespace ChunithmMobile.Rendering
{
    /// <summary>
    /// ALD supplies trace-only paths (zero interval) or spaced AirLadder
    /// plates (positive interval). Parser-generated
    /// nodes share timestamps and note indices with the authoritative AIR
    /// checker; this class only observes judgement, never decides the score.
    /// </summary>
    public sealed class AirLadderVisuals : IDisposable
    {
        private readonly Transform parent;
        private readonly List<Cell> cells = new List<Cell>();
        private readonly HashSet<int> judged = new HashSet<int>();
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private Mesh glowMesh;
        public int VisibleCount { get; private set; }
        public int CellCount => cells.Count;

        public AirLadderVisuals(Transform parent) { this.parent = parent; }

        public static bool HasPlates(VisualGuidePath path) => path.StyleInterval > 0 &&
            !string.Equals(path.Color, "NON", StringComparison.OrdinalIgnoreCase);

        public void Bind(IReadOnlyList<VisualGuidePath> paths)
        {
            Clear();
            foreach (var path in paths) {
                if (!HasPlates(path)) continue;
                foreach (AirLadderNode node in path.Nodes)
                    cells.Add(new Cell(path, node));
            }
            RefreshSkin();
        }

        public void MarkJudged(int noteIndex) => judged.Add(noteIndex);

        public void Render(double now, Func<int, double, double, float> distance, double rate = 1)
        {
            VisibleCount = 0;
            foreach (Cell cell in cells) {
                float z = distance(cell.Profile, cell.Time, now);
                // A plate must live until its authored arrival, even when the
                // underlying ALD ends almost immediately. Its geometry stays
                // intact across frames; the camera clips the far edge.
                bool pending = !judged.Contains(cell.NoteIndex) &&
                    now <= cell.Time + JudgementEngine.MissWindowMs(NoteKind.AirAction) * Math.Max(.1, rate);
                bool visible = pending && (z >= ChartSpace.JudgementZ || now >= cell.Time) &&
                    z <= ChartSpace.FarZ + ChartSpace.JudgementZ + 12f;
                if (now >= cell.Time) z = Mathf.Max(z, ChartSpace.JudgementZ);
                if (!visible) { if (cell.Root != null) cell.Root.SetActive(false); continue; }
                if (cell.Root == null) Create(cell);
                cell.Root.SetActive(true);
                float x = ChartSpace.NoteCenterX(cell.Lane, cell.Width, z);
                float width = ChartSpace.NoteHalfWidth(cell.Width, z) * 2f;
                float height = ChartSpace.AirY(cell.Height);
                SetLayer(cell.Bar, cell.BarMesh, x, height, z, width, .58f);
                SetLayer(cell.Frame, cell.FrameMesh, x, height - .025f, z, width, .68f);
                // This is the original additive glow texture, attached to the
                // authored plate rather than a separate green chart line.
                cell.Glow.transform.localPosition = new Vector3(x, height - .04f, z + .6f);
                cell.Glow.transform.localScale = new Vector3(width, 1f, 1.8f);
                VisibleCount++;
            }
        }

        private static void SetLayer(GameObject go, Mesh mesh, float x, float y, float z,
            float width, float depth)
        {
            Bounds b = mesh.bounds;
            float sx = width / Mathf.Max(.0001f, b.size.x);
            float sz = depth / Mathf.Max(.0001f, b.size.z);
            float sy = b.size.y > .0001f ? .07f / b.size.y : 1f;
            go.transform.localPosition = new Vector3(x - b.center.x * sx,
                y - b.min.y * sy, z - b.center.z * sz);
            go.transform.localScale = new Vector3(sx, sy, sz);
        }

        private void Create(Cell cell)
        {
            cell.Root = new GameObject("ALD purple AirLadder plate");
            cell.Root.transform.SetParent(parent, false);
            int width = Mathf.Clamp(Mathf.RoundToInt(cell.Width), 1, 16);
            string kind = cell.Top ? "airladtop" : "airlad";
            cell.BarMesh = Mesh(kind, "bar", width);
            cell.FrameMesh = Mesh(kind, "frame", width);
            cell.Bar = Layer(cell.Root.transform, "Purple bar", cell.BarMesh, materials[kind + "_bar"]);
            cell.Frame = Layer(cell.Root.transform, "White frame", cell.FrameMesh, materials["airlad_waku"]);
            if (glowMesh == null) glowMesh = MeshFactory.Quad(new Vector3(-.5f, 0, -.5f),
                new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f));
            cell.Glow = Layer(cell.Root.transform, "Purple glow", glowMesh, materials[kind + "_glow"]);
            // Generic notes already carry an authored-index sorting order.
            // Use that same order here: leaving plates at zero makes an
            // overlapping green AHTAP cap paint over the purple top layer,
            // even though the ladder material has a later render queue.
            int order = Mathf.Clamp(cell.NoteIndex, 0, 30000) + 2;
            cell.Bar.GetComponent<MeshRenderer>().sortingOrder = order;
            cell.Frame.GetComponent<MeshRenderer>().sortingOrder = order + 1;
            cell.Glow.GetComponent<MeshRenderer>().sortingOrder = order - 1;
        }

        private Mesh Mesh(string kind, string part, int width)
        {
            string name = "ntt_" + kind + "_" + width.ToString("00") + "_" + part;
            if (meshes.TryGetValue(name, out Mesh mesh)) return mesh;
            mesh = GameplaySkinManager.LoadMesh(name);
            if (mesh == null) {
                var model = Resources.Load<GameObject>("OriginalMeshes/" + name);
                mesh = model != null ? model.GetComponentInChildren<MeshFilter>().sharedMesh : null;
            }
            if (mesh == null) throw new InvalidOperationException("Missing AirLadder geometry: " + name);
            meshes[name] = mesh;
            return mesh;
        }

        private static GameObject Layer(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public void RefreshSkin()
        {
            Material("airlad_bar", "airlad_bar", Color.white, false);
            Material("airladtop_bar", "airladtop_bar", Color.white, false);
            Material("airlad_waku", "airlad_waku", Color.white, false);
            Material("airlad_glow", "airlad_effEx", new Color(.8f, .35f, 1f, .35f), true);
            Material("airladtop_glow", "airladtop_effEx", new Color(.8f, .35f, 1f, .35f), true);
            // SkinManager owns its mesh cache. Release references before a skin
            // change, and lazily reacquire geometry for visible cells.
            meshes.Clear();
            foreach (Cell cell in cells) {
                if (cell.Root != null) Release(cell.Root);
                cell.Root = null;
            }
        }

        private void Material(string key, string texture, Color tint, bool additive)
        {
            if (!materials.TryGetValue(key, out Material material)) {
                material = new Material(Resources.Load<Shader>("AirLadder"));
                material.renderQueue = additive ? 3139 : key.EndsWith("waku") ? 3142 : 3141;
                material.SetFloat("_DstBlend", additive ? 1 : 10);
                materials[key] = material;
            }
            // Missing new skin entries fall back to the extracted built-in
            // art, so legacy skins still display the complete purple note.
            material.mainTexture = GameplaySkinManager.LoadFieldTexture("field/" + texture);
            material.color = tint;
        }

        public void Clear()
        {
            foreach (Cell cell in cells) if (cell.Root != null) Release(cell.Root);
            cells.Clear(); judged.Clear(); VisibleCount = 0;
        }
        public void Dispose()
        {
            Clear(); foreach (var material in materials.Values) Release(material);
            materials.Clear(); meshes.Clear(); if (glowMesh != null) Release(glowMesh); glowMesh = null;
        }
        private static void Release(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
        private sealed class Cell
        {
            public readonly VisualGuidePath Path;
            public readonly double Time;
            public readonly float Lane, Width, Height;
            public readonly bool Top;
            public readonly int NoteIndex, Profile;
            public GameObject Root, Bar, Frame, Glow;
            public Mesh BarMesh, FrameMesh;
            public Cell(VisualGuidePath path, AirLadderNode node)
            {
                Path = path; Time = node.TimeMs; Lane = node.Lane; Width = node.Width;
                Height = node.Height; NoteIndex = node.NoteIndex; Profile = node.ScrollProfileId;
                // Native first-node table 0x01C1E2E8 maps via ModelSet/Model
                // records to ntt_airladtop; remaining nodes use ntt_airlad.
                // This is a first-node distinction, not a last-node cap.
                Top = node.IsFirst;
            }
        }
    }
}
