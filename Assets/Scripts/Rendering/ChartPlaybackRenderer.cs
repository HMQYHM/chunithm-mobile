using System;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Rendering
{
    /// <summary>Original-resource renderer with independent head/body/line/tail lifetimes.</summary>
    public sealed class ChartPlaybackRenderer : MonoBehaviour
    {
        private const double VisibilityLeadRealMilliseconds = 120.0;
        private const double PostHeadRenderGraceMilliseconds = 900.0;
        private const float MinimumVisibilityGuardZ = 2f;
        private const float MaximumVisibilityGuardZ = 96f;
        private const double AirDecorationHoldMilliseconds = 900.0;
        // Keep every airborne surface above the complete ground-note pass.
        // Equal-queue renderers use Unity's camera-distance transparent sort.
        private const int GroundHeadQueue = 3070;
        private const int AirWallQueue = 3100;
        private const int AirBodyQueue = 3110;
        private const int AirDecorationQueue = 3120;
        private const int AirLineQueue = 3130;
        private const int AirHeadQueue = 3140;
        private const int AirArrowQueue = 3150;
        private const float AirTrailOuterOpacity = .28f;

        [Range(.002f, .04f)] public float UnitsPerScrollMillisecond = .018f;
        [Min(.1f)] public float VisualSpeed = 1f;
        [Range(12, 48)] public int RibbonSamples = 28;

        private readonly Dictionary<int, NoteVisual> active = new Dictionary<int, NoteVisual>();
        private readonly Stack<NoteVisual> pool = new Stack<NoteVisual>();
        private readonly HashSet<int> keep = new HashSet<int>();
        private readonly HashSet<int> updated = new HashSet<int>();
        private readonly HashSet<int> judgedHeads = new HashSet<int>();
        private readonly List<int> remove = new List<int>();
        private readonly Dictionary<int, ChartNote> pairedGroundByAir = new Dictionary<int, ChartNote>();
        private readonly Dictionary<string, Material> headMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Mesh> originalMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Mesh> mirroredAirMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Material> airLineMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Material> airLineBodyMaterials = new Dictionary<string, Material>();
        private ParsedChart chart;
        private OriginalPlaybackMap playback;
        private RibbonGeometry[] geometry;
        private DspChartClock clock;
        private double lastRenderTime = double.NegativeInfinity;
        private Material holdBody, slideBody, airHoldBody;
        private Material heavenRoof, heavenRoofPink, heavenWall, heavenShadow;
        private Material airActionTrailRainbowBody, airActionTrailGreenBody, airActionBarBody;
        private Material airLadderTop, airLadderPost, airLadderEdge;
        private Material airLadderStem, airLadderRim;
        private Material holdLine, slideLine, airActionTrailRainbowLine, airActionTrailGreenLine, defaultHead;
        private Material defaultAirHead, defaultAirArrow;
        private bool airTrailRainbow;
        private bool firstFrameHidden;
        private Transform notesRoot;
        private readonly NoteSkinUvCache noteSkinUvs = new NoteSkinUvCache();

        /// <summary>
        /// Changes only AIR ACTION's authored trail. AIR heads and ALD
        /// decorative lines deliberately use separate material paths.
        /// </summary>
        public void ConfigureAirTrail(bool rainbow)
        {
            airTrailRainbow = rainbow;
        }

        public void Bind(ParsedChart parsedChart, DspChartClock chartClock)
        {
            firstFrameHidden = false;
            ClearActive();
            judgedHeads.Clear();
            chart = parsedChart;
            playback = new OriginalPlaybackMap(parsedChart);
            lastRenderTime = double.NegativeInfinity;
            geometry = new RibbonGeometry[parsedChart.Notes.Count];
            pairedGroundByAir.Clear();
            for (int i = 0; i < parsedChart.Notes.Count; i++) geometry[i] = BuildGeometry(parsedChart.Notes[i]);
            BuildAirPairs(parsedChart.Notes);
            clock = chartClock;
        }

        public void Clear()
        {
            firstFrameHidden = false;
            ClearActive();
            judgedHeads.Clear();
            chart = null;
            playback = null;
            geometry = null;
            clock = null;
            lastRenderTime = double.NegativeInfinity;
        }

        private void Awake()
        {
            string[] kinds = {
                "tap", "extap", "hold", "slide", "flick", "dmg", "air_up", "air_down",
                "ahtap", "ahtapend", "ahact", "ahsdw", "ahroof"
            };
            foreach (string kind in kinds) {
                for (int width = 1; width <= 16; width++) {
                    string key = kind + "_" + width.ToString("00");
                    Texture2D texture = GameplaySkinManager.LoadNoteTexture(key);
                    if (texture == null) continue;
                    Material material = MeshFactory.Material(texture, Color.white, true);
                    material.renderQueue = kind.StartsWith("air_") ? AirArrowQueue
                        : kind.StartsWith("ah", StringComparison.Ordinal) ? AirHeadQueue : GroundHeadQueue;
                    headMaterials[key] = material;
                }
            }
            string[] meshKinds = {
                "tap", "extap", "hold", "holdend", "slide", "slideend", "flick", "dmg",
                "airup", "airdw", "airuptilt", "airdwtilt",
                "ahtap", "ahtapend", "ahact", "ahsdw", "ahroof"
            };
            foreach (string kind in meshKinds) {
                for (int width = 1; width <= 16; width++) {
                    string key = kind + "_" + width.ToString("00");
                    Mesh external = GameplaySkinManager.LoadMesh("ntt_" + key);
                    if (external != null) {
                        originalMeshes[key] = external;
                        continue;
                    }
                    GameObject resource = Resources.Load<GameObject>("OriginalMeshes/ntt_" + key);
                    MeshFilter filter = resource != null ? resource.GetComponentInChildren<MeshFilter>() : null;
                    if (filter != null && filter.sharedMesh != null) originalMeshes[key] = filter.sharedMesh;
                }
            }
            holdBody = TextureMaterial("field/hold_joint", new Color(1f, 1f, 1f, .92f), 2980);
            slideBody = TextureMaterial("field/slide_joint", new Color(1f, 1f, 1f, .90f), 2990);
            // Unlit/Transparent ignores material tint/alpha. This layer needs
            // an actual translucent surface, independent of the bright centre line.
            airHoldBody = TintedAirMaterial(GameplaySkinManager.LoadFieldTexture("field/air_hold_joint"),
                new Color(1f, 1f, 1f, AirTrailOuterOpacity), AirBodyQueue);
            heavenRoof = TextureMaterial("field/heaven_roof", Color.white, AirBodyQueue);
            heavenRoofPink = TextureMaterial("field/heaven_roof_pink", Color.white, AirBodyQueue);
            heavenWall = TextureMaterial("field/heaven_wall", new Color(1f, 1f, 1f, .72f), AirWallQueue);
            heavenShadow = TextureMaterial("field/heaven_shadow", new Color(1f, 1f, 1f, .74f), 2975);
            airActionTrailRainbowBody = TintedAirMaterial(GameplaySkinManager.LoadFieldTexture("field/air_action_line"),
                new Color(1, 1, 1, AirTrailOuterOpacity), AirBodyQueue);
            airActionBarBody = TintedAirMaterial(GameplaySkinManager.LoadFieldTexture("field/air_action_bar"),
                new Color(1, 1, 1, AirTrailOuterOpacity), AirBodyQueue);
            airActionTrailGreenBody = TintedAirMaterial(null, new Color(.12f, 1f, .34f, AirTrailOuterOpacity), AirBodyQueue);
            airLadderTop = TextureMaterial("field/air_ladder_top", Color.white, AirDecorationQueue);
            airLadderPost = TextureMaterial("field/air_train", Color.white, AirDecorationQueue);
            airLadderEdge = MeshFactory.Material(new Color(1f, .88f, 1f, .96f), true);
            airLadderEdge.renderQueue = AirLineQueue;
            airLadderStem = TintedAirMaterial(null, new Color(1f, .35f, .85f, .75f), AirDecorationQueue);
            airLadderRim = TintedAirMaterial(null, new Color(.95f, .85f, 1f, .92f), AirBodyQueue);
            holdLine = MeshFactory.Material(new Color(1f, .94f, .44f, .98f), true); holdLine.renderQueue = 3040;
            slideLine = MeshFactory.Material(new Color(.68f, .96f, 1f, .98f), true); slideLine.renderQueue = 3040;
            airActionTrailGreenLine = SolidAirTrailMaterial(null, new Color(.12f, 1f, .34f, 1f));
            airActionTrailRainbowLine = SolidAirTrailMaterial(GameplaySkinManager.LoadFieldTexture("field/air_action_line"), Color.white);
            BuildAirLineMaterials();
            defaultHead = MeshFactory.Material(Color.white, true);
            defaultHead.renderQueue = GroundHeadQueue;
            defaultAirHead = new Material(defaultHead) { renderQueue = AirHeadQueue };
            defaultAirArrow = new Material(defaultHead) { renderQueue = AirArrowQueue };
            PrepareNoteSkinUvs();
        }

        private Material TextureMaterial(string resource, Color tint, int queue)
        {
            Texture2D texture = GameplaySkinManager.LoadFieldTexture(resource);
            Material material = texture != null ? MeshFactory.Material(texture, tint, true) : MeshFactory.Material(tint, true);
            material.renderQueue = queue;
            return material;
        }

        private static Material TintedAirMaterial(Texture texture, Color tint, int queue)
        {
            return new Material(Shader.Find("Sprites/Default")) {
                mainTexture = texture != null ? texture : Texture2D.whiteTexture,
                color = tint, renderQueue = queue, enableInstancing = true
            };
        }

        private static Material SolidAirTrailMaterial(Texture texture, Color tint)
        {
            return new Material(Resources.Load<Shader>("AirTrailCore")) {
                mainTexture = texture != null ? texture : Texture2D.whiteTexture,
                color = tint, renderQueue = AirLineQueue, enableInstancing = true
            };
        }

        public void RefreshSkin()
        {
            noteSkinUvs.Clear();
            foreach (Mesh mesh in mirroredAirMeshes.Values)
                if (mesh != null) Destroy(mesh);
            mirroredAirMeshes.Clear();
            string[] meshKinds = {
                "tap", "extap", "hold", "holdend", "slide", "slideend", "flick", "dmg",
                "airup", "airdw", "airuptilt", "airdwtilt", "ahtap", "ahtapend", "ahact", "ahsdw", "ahroof"
            };
            foreach (string kind in meshKinds) {
                for (int width = 1; width <= 16; width++) {
                    string key = kind + "_" + width.ToString("00");
                    Mesh external = GameplaySkinManager.LoadMesh("ntt_" + key);
                    if (external != null) {
                        originalMeshes[key] = external;
                        continue;
                    }
                    GameObject resource = Resources.Load<GameObject>("OriginalMeshes/ntt_" + key);
                    MeshFilter filter = resource != null ? resource.GetComponentInChildren<MeshFilter>() : null;
                    if (filter != null && filter.sharedMesh != null) originalMeshes[key] = filter.sharedMesh;
                }
            }
            foreach (var pair in headMaterials)
                pair.Value.mainTexture = GameplaySkinManager.LoadNoteTexture(pair.Key);
            if (holdBody != null) holdBody.mainTexture = GameplaySkinManager.LoadFieldTexture("field/hold_joint");
            if (slideBody != null) slideBody.mainTexture = GameplaySkinManager.LoadFieldTexture("field/slide_joint");
            if (airHoldBody != null) airHoldBody.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_hold_joint");
            if (heavenRoof != null) heavenRoof.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_roof");
            if (heavenRoofPink != null) heavenRoofPink.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_roof_pink");
            if (heavenWall != null) heavenWall.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_wall");
            if (heavenShadow != null) heavenShadow.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_shadow");
            if (airActionTrailRainbowBody != null)
                airActionTrailRainbowBody.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_action_line");
            if (airActionBarBody != null)
                airActionBarBody.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_action_bar");
            if (airActionTrailRainbowLine != null)
                airActionTrailRainbowLine.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_action_line");

            GameplaySkinStyle skin = GameplaySkinManager.Style;
            if (holdLine != null) holdLine.color = skin.HoldLineColor;
            if (slideLine != null) slideLine.color = skin.SlideLineColor;
            Color trail = skin.AirLineColor;
            trail.a = AirTrailOuterOpacity;
            if (airActionTrailGreenBody != null) airActionTrailGreenBody.color = trail;
            trail.a = 1f;
            if (airActionTrailGreenLine != null) airActionTrailGreenLine.color = trail;
            if (airLadderEdge != null) airLadderEdge.color = skin.EdgeColor;
            PrepareNoteSkinUvs();
        }

        private void PrepareNoteSkinUvs()
        {
            foreach (string kind in new[] { "tap", "extap", "hold", "holdend", "slide", "slideend", "flick", "dmg" })
                for (int width = 1; width <= 16; width++) {
                    string suffix = "_" + width.ToString("00");
                    string geometryKey = (kind == "extap" ? "tap" : kind) + suffix;
                    string textureKey = (kind == "holdend" ? "hold" : kind == "slideend" ? "slide" : kind) + suffix;
                    if (originalMeshes.TryGetValue(geometryKey, out Mesh mesh) &&
                        headMaterials.TryGetValue(textureKey, out Material material) &&
                        GameplaySkinManager.IsExternalNoteTexture(material.mainTexture))
                        noteSkinUvs.Get(mesh, (Texture2D)material.mainTexture);
                }
        }

        private void LateUpdate()
        {
            if (chart == null || playback == null || clock == null || clock.Preparing) return;
            if (firstFrameHidden) {
                foreach (var visual in active.Values) if (visual.Valid) visual.Root.SetActive(true);
                firstFrameHidden = false;
                Debug.Log($"Gameplay first visible frame: chart={clock.TimeMs:F2}ms, warmedNotes={active.Count}");
            }
            RenderFrame(clock.TimeMs);
        }

        public void PrepareFirstFrame(double chartTimeMs, Camera warmupCamera = null)
        {
            if (chart == null || playback == null || clock == null) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            RenderFrame(chartTimeMs);
            if (warmupCamera != null) DrawPreparedFrame(warmupCamera);
            foreach (var visual in active.Values) visual.Root.SetActive(false);
            firstFrameHidden = true;
            Debug.Log($"Gameplay first frame prepared: notes={active.Count}, gpuDraw={warmupCamera != null}, elapsed={watch.ElapsedMilliseconds}ms, clockPlaying={clock.Playing}");
        }

        private static void DrawPreparedFrame(Camera camera)
        {
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(Math.Max(1, camera.pixelWidth),
                Math.Max(1, camera.pixelHeight), 24, RenderTextureFormat.ARGB32);
            var fencePixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try {
                // Submit the real meshes/materials with the gameplay camera
                // while loading still covers the screen. Readback waits for
                // this draw on the GPU, rather than merely toggling objects.
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                fencePixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            } finally {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                ReleaseOwnedObject(fencePixel);
            }
        }

        private void RenderFrame(double now)
        {
            remove.Clear();
            foreach (var item in active) if (!item.Value.Valid) remove.Add(item.Key);
            foreach (int index in remove) {
                active[index].Dispose();
                active.Remove(index);
            }
            keep.Clear();
            updated.Clear();

            bool fullScan = double.IsNegativeInfinity(lastRenderTime) || !playback.SupportsForwardTimeWindow ||
                (!double.IsNegativeInfinity(lastRenderTime) && now + .001 < lastRenderTime);
            if (fullScan) {
                for (int i = 0; i < chart.Notes.Count; i++) ProcessNote(i, now);
            } else {
                // For positive scroll profiles, a note can only enter the
                // visible range within this conservative authored-time band.
                // Already active objects are updated below so long bodies keep
                // their exact lifetime even after their head leaves the band.
                double lead = RenderLookAheadMilliseconds();
                int first = LowerBoundNoteTime(now - PostHeadRenderGraceMilliseconds);
                int end = UpperBoundNoteTime(now + lead);
                for (int i = first; i < end; i++) ProcessNote(i, now);

                // The dictionary is intentionally the source of truth for
                // active long notes. This also covers a slow section whose
                // head was created several seconds ago.
                foreach (var item in active)
                    if (!updated.Contains(item.Key)) ProcessNote(item.Key, now);
            }
            lastRenderTime = now;

            remove.Clear();
            foreach (var item in active) if (!keep.Contains(item.Key)) remove.Add(item.Key);
            foreach (int index in remove) { Return(active[index]); active.Remove(index); }
        }

        private void ProcessNote(int i, double now)
        {
            ChartNote note = chart.Notes[i];
                bool instantDecoration = note.Kind is NoteKind.AirLadder or NoteKind.AirLine;
                bool decorationActive = instantDecoration &&
                    now >= note.TimeMs && now <= note.TimeMs + AirDecorationHoldMilliseconds;
                bool longBody = note.EndTimeMs > note.TimeMs &&
                    note.Kind is (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or
                        NoteKind.AirHold or NoteKind.AirAction or NoteKind.AirLadder or NoteKind.AirLine);
                // Active long objects are already inside the visibility band.
                // Recomputing every ribbon endpoint just to rediscover that
                // fact is costly on dense charts, especially on low-end CPUs.
                // Keep the normal bounds path for first activation and short
                // objects, where it is still needed for correct culling.
                if (longBody && active.TryGetValue(i, out NoteVisual activeVisual) &&
                    (decorationActive || now <= note.EndTimeMs + PostHeadRenderGraceMilliseconds)) {
                    keep.Add(i);
                    updated.Add(i);
                    float activeHeadZ = Distance(note.ScrollProfileId, note.TimeMs, now);
                    UpdateVisual(activeVisual, i, note, now, activeHeadZ,
                        VisibilityGuardZ(note.ScrollProfileId, note.TimeMs, now, activeHeadZ));
                    return;
                }
                int headProfile = note.ScrollProfileId;
                if (note.Kind == NoteKind.Air &&
                    pairedGroundByAir.TryGetValue(i, out ChartNote pairedGround))
                    headProfile = pairedGround.ScrollProfileId;
                float headZ = Distance(headProfile, note.TimeMs, now);
                GetVisualDistanceBounds(i, now, headZ, out double min, out double max);
                bool unresolvedHead = IsUnresolvedHead(note, i, now);
                if (!decorationActive && (max < ChartSpace.NearZ - .25f && !unresolvedHead) ||
                    min > VisualFarZ + MaximumVisibilityGuardZ ||
                    (!decorationActive && now > Math.Max(note.TimeMs, note.EndTimeMs) + 260)) return;
                float visibilityGuardZ = VisibilityGuardZ(headProfile, note.TimeMs, now, headZ);
                if (!decorationActive && min > VisualFarZ + visibilityGuardZ) return;
                if (!active.TryGetValue(i, out NoteVisual visual)) { visual = Rent(); active[i] = visual; }
                keep.Add(i);
                updated.Add(i);
                UpdateVisual(visual, i, note, now, headZ, visibilityGuardZ);
        }

        private double RenderLookAheadMilliseconds()
        {
            double speed = Math.Max(.001, UnitsPerScrollMillisecond * Math.Max(.1f, VisualSpeed) *
                Math.Max(.001, playback.MinimumPositiveSpeed));
            double distance = VisualFarZ + MaximumVisibilityGuardZ + 2f;
            double lead = distance / speed + VisibilityLeadRealMilliseconds * Math.Max(.1, clock.Rate) + 250.0;
            // Distance calculation already provides the needed lead time. A
            // smaller floor prevents dense charts from keeping too many
            // visuals active on slower processors.
            return Math.Min(Math.Max(lead, 1200.0), chart.DurationMs + PostHeadRenderGraceMilliseconds);
        }

        private int LowerBoundNoteTime(double timeMs)
        {
            int low = 0, high = chart.Notes.Count;
            while (low < high) {
                int mid = (low + high) >> 1;
                if (chart.Notes[mid].TimeMs < timeMs) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private int UpperBoundNoteTime(double timeMs)
        {
            int low = 0, high = chart.Notes.Count;
            while (low < high) {
                int mid = (low + high) >> 1;
                if (chart.Notes[mid].TimeMs <= timeMs) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private float Distance(int profileId, double authoredTime, double now)
        {
            return ChartSpace.JudgementZ +
                (float)(playback.ProfileDistance(profileId, authoredTime, now) * UnitsPerScrollMillisecond * VisualSpeed);
        }

        private static float VisualFarZ => ChartSpace.FarZ + ChartSpace.JudgementZ;

        private float VisibilityGuardZ(int profileId, double authoredTime, double now, float currentZ)
        {
            // At high note speed (and especially 2x playback), a note can
            // cross the whole top-edge culling band between rendered frames.
            // Project its actual scroll profile ahead by a short real-time
            // interval so it is created and held at the far edge first.
            double projectedNow = now + VisibilityLeadRealMilliseconds * Math.Max(.1, clock.Rate);
            float projectedZ = Distance(profileId, authoredTime, projectedNow);
            return Mathf.Clamp(Mathf.Abs(currentZ - projectedZ) + 1f,
                MinimumVisibilityGuardZ, MaximumVisibilityGuardZ);
        }

        private void GetVisualDistanceBounds(int noteIndex, double now, double headDistance, out double min, out double max)
        {
            min = max = headDistance;
            RibbonGeometry ribbon = geometry[noteIndex];
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                int profile = ribbon.SegmentProfileIds[i];
                double start = Distance(profile, ribbon.AuthoredTimes[i], now);
                double end = Distance(profile, ribbon.AuthoredTimes[i + 1], now);
                min = Math.Min(min, Math.Min(start, end));
                max = Math.Max(max, Math.Max(start, end));
            }
        }

        private void UpdateVisual(NoteVisual visual, int noteIndex, ChartNote note, double now,
            float headZ, float visibilityGuardZ)
        {
            bool longBody = note.EndTimeMs > note.TimeMs && note.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or NoteKind.AirHold or NoteKind.AirAction or NoteKind.AirLadder or NoteKind.AirLine;
            visual.Accent.SetActive(false);
            visual.Body.SetActive(longBody); visual.Line.SetActive(longBody);
            if (longBody) UpdateLongVisual(visual, geometry[noteIndex], note, now);

            bool judged = judgedHeads.Contains(noteIndex);
            bool hasReachedJudgementLine = headZ <= ChartSpace.JudgementZ;
            bool insideApproach = headZ >= ChartSpace.NearZ &&
                                  headZ <= VisualFarZ + visibilityGuardZ;

            // The original renderer has two different head lifetimes. A
            // normal ground head is held on the gold line until its head
            // judgement resolves; this is why an untouched note does not
            // slide below the line before becoming MISS. Long-note heads and
            // discrete AIR heads finish at their arrival plane, while their
            // remaining body is rendered independently below.
            bool groundHead = note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Mine or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide;
            bool airHead = note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction;
            bool airPending = airHead && now <= note.TimeMs +
                JudgementEngine.MissWindowMs(note.Kind,
                    note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight,
                    note.AirFlick) * clock.Rate;
            bool showHead = note.HasVisualHead && !judged &&
                            (airHead
                                ? (insideApproach || hasReachedJudgementLine) && airPending
                                : groundHead
                                    ? (insideApproach || hasReachedJudgementLine) &&
                                      now <= note.TimeMs + JudgementEngine.MissWindowMs(note) * clock.Rate
                                    : insideApproach);
            if (note.Kind == NoteKind.Slide && !note.VisibleHead && note.HasVisualHead)
                showHead = insideApproach && now < note.TimeMs;
            visual.Head.SetActive(showHead && note.Kind is not (NoteKind.AirLadder or NoteKind.AirLine));
            if (visual.Head.activeSelf) {
                // Ground heads that are waiting for the late MISS boundary are
                // pinned to the same plane as the gold line, matching arcade
                // behavior instead of drifting into the touch area.
                float renderHeadZ = (groundHead || airHead) && hasReachedJudgementLine
                    ? ChartSpace.JudgementZ : Mathf.Min(headZ, VisualFarZ);
                UpdateHead(visual.Head, visual.HeadFilter, visual.HeadMesh, visual.HeadRenderer,
                    noteIndex, note, renderHeadZ, false);
            }

            // The assignment interval is end-exclusive. Querying exactly at
            // EndTimeMs can therefore select the next profile while the final
            // body interval still belongs to the previous one. Reuse that
            // final interval profile so the tail cannot detach from its body.
            bool showTail = false;
            float tailZ = 0f;
            if (longBody && note.VisibleTail && now < note.EndTimeMs) {
                int endProfile = geometry[noteIndex].EndProfileId;
                tailZ = Distance(endProfile, note.EndTimeMs, now);
                float tailGuardZ = VisibilityGuardZ(endProfile, note.EndTimeMs, now, tailZ);
                showTail = tailZ >= ChartSpace.NearZ && tailZ <= VisualFarZ + tailGuardZ;
            }
            visual.Tail.SetActive(showTail && note.Kind is not (NoteKind.AirLadder or NoteKind.AirLine));
            if (visual.Tail.activeSelf) UpdateHead(visual.Tail, visual.TailFilter, visual.TailMesh,
                visual.TailRenderer, noteIndex, note, Mathf.Min(tailZ, VisualFarZ), true);
        }

        private void UpdateLongVisual(NoteVisual visual, RibbonGeometry ribbon, ChartNote note, double now)
        {
            float start = now > note.TimeMs && note.EndTimeMs > note.TimeMs ? Mathf.Clamp01((float)((now - note.TimeMs) / (note.EndTimeMs - note.TimeMs))) : 0f;
            bool visible = now < note.EndTimeMs;
            if (note.Kind == NoteKind.HeavenHold) {
                UpdateHeavenHoldVisual(visual, ribbon, note, now, start, visible);
                return;
            }
            if (note.Kind == NoteKind.AirLadder) {
                UpdateAirLadderVisual(visual, ribbon, note, now, start, visible);
                return;
            }
            bool hasCentreLine = note.Kind != NoteKind.Hold;
            visual.Body.SetActive(visible); visual.Line.SetActive(visible && hasCentreLine);
            if (!visible) return;
            Material body, line; bool air = false;
            switch (note.Kind) {
                case NoteKind.Hold: body = holdBody; line = holdLine; break;
                case NoteKind.Slide: body = slideBody; line = slideLine; break;
                case NoteKind.AirAction:
                    body = airTrailRainbow ? airActionBarBody : airActionTrailGreenBody;
                    line = airTrailRainbow ? airActionTrailRainbowLine : airActionTrailGreenLine;
                    air = true;
                    break;
                case NoteKind.AirHold: body = airHoldBody; line = airActionTrailGreenLine; air = true; break;
                case NoteKind.AirLine:
                    // ALD is a chart-authored decorative line. It must never
                    // inherit AIR ACTION's rainbow/green trail preference.
                    body = AirLineBodyMaterial(note.AirLineColor);
                    line = AirLineMaterial(note.AirLineColor);
                    air = true;
                    break;
                default: body = holdBody; line = holdLine; break;
            }
            visual.BodyRenderer.sharedMaterial = body; visual.LineRenderer.sharedMaterial = line;
            UpdateRibbon(visual.BodyMesh, visual.BodyBuffers, ribbon, note, now, start, air, false);
            if (hasCentreLine)
                UpdateRibbon(visual.LineMesh, visual.LineBuffers, ribbon, note, now, start, air, true);
        }

        private void UpdateAirLadderVisual(NoteVisual visual, RibbonGeometry ribbon, ChartNote note,
            double now, float start, bool visible)
        {
            visual.Body.SetActive(visible);
            visual.Line.SetActive(visible);
            visual.Accent.SetActive(false);
            if (!visible) return;

            // One-tick ALD posts are composite objects: a horizontal cap at
            // the authored starting height and a narrow connection below it.
            // Stretching air_train across Width turns stacked caps into walls.
            if (note.EndTimeMs - note.TimeMs <= 24.0) {
                UpdateShortAirLadderVisual(visual, ribbon, note, now, start);
                return;
            }

            bool horizontal = Mathf.Abs(note.EndAirHeight - note.AirHeight) < .01f;
            bool defaultColor = IsDefaultAirLadderColor(note.AirLineColor);
            visual.BodyRenderer.sharedMaterial = defaultColor
                ? (horizontal ? airLadderTop : airLadderPost)
                : AirLineBodyMaterial(note.AirLineColor);
            visual.LineRenderer.sharedMaterial = defaultColor
                ? airLadderEdge
                : AirLineMaterial(note.AirLineColor);

            UpdateAirLadderBody(visual.BodyMesh, visual.BodyBuffers, ribbon, note, now, start);
            UpdateAirLadderAccent(visual.LineMesh, visual.LineBuffers, ribbon, note, now, start, horizontal);
        }

        private void UpdateShortAirLadderVisual(NoteVisual visual, RibbonGeometry ribbon,
            ChartNote note, double now, float start)
        {
            bool defaultColor = IsDefaultAirLadderColor(note.AirLineColor);
            // A short ALD is an instant field-decoration event. The arcade
            // renderer reveals the complete rail at once; it is not a tiny
            // airborne cap that scrolls in from the far edge.
            visual.Body.SetActive(false);
            visual.Accent.SetActive(false);
            bool active = now >= note.TimeMs &&
                now <= note.TimeMs + AirDecorationHoldMilliseconds;
            visual.Line.SetActive(active);
            if (!active) return;
            visual.LineRenderer.sharedMaterial = defaultColor
                ? airLadderEdge : AirLineMaterial(note.AirLineColor);
            SetFullDepthAirDecoration(visual.LineMesh, visual.LineBuffers, note);
        }

        private static void SetFullDepthAirDecoration(Mesh mesh, RibbonBuffers buffers, ChartNote note)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(1);
            int vertices = 0;
            int triangles = 0;
            float height = ChartSpace.AirY(note.AirHeight);
            float nearX = ChartSpace.NoteCenterX(note.Lane, note.Width, ChartSpace.NearZ);
            float farX = ChartSpace.NoteCenterX(note.EndLane, note.EndWidth, ChartSpace.FarZ);
            float nearHalf = FixedLineHalfWidth(.115f);
            float farHalf = FixedLineHalfWidth(.115f);
            buffers.AppendRibbonQuad(ref vertices, ref triangles,
                new Vector3(nearX - nearHalf, height, ChartSpace.NearZ),
                new Vector3(nearX + nearHalf, height, ChartSpace.NearZ),
                0f,
                new Vector3(farX - farHalf, height, ChartSpace.FarZ),
                new Vector3(farX + farHalf, height, ChartSpace.FarZ),
                1f);
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void UpdateAirLadderBody(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(ribbon.SegmentCount);
            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            double cutoff = note.TimeMs + duration * Mathf.Clamp01(start);
            int vertices = 0;
            int triangles = 0;
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                double segmentStart = Math.Max(cutoff, ribbon.AuthoredTimes[i]);
                double segmentEnd = ribbon.AuthoredTimes[i + 1];
                if (segmentEnd <= segmentStart + .001) continue;
                int profile = ribbon.SegmentProfileIds[i];
                CalculateAirLadderPoint(note, segmentStart, now, profile,
                    out Vector3 leftA, out Vector3 rightA, out float uvA);
                CalculateAirLadderPoint(note, segmentEnd, now, profile,
                    out Vector3 leftB, out Vector3 rightB, out float uvB);
                PadShortAirLadder(note, ribbon, i, ref leftA, ref rightA, ref leftB, ref rightB);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        ChartSpace.NearZ, ChartSpace.FarZ + ChartSpace.JudgementZ + 8f))
                    continue;
                buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void UpdateAirLadderAccent(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start, bool horizontal)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(ribbon.SegmentCount * 2);
            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            double cutoff = note.TimeMs + duration * Mathf.Clamp01(start);
            int vertices = 0;
            int triangles = 0;
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                double segmentStart = Math.Max(cutoff, ribbon.AuthoredTimes[i]);
                double segmentEnd = ribbon.AuthoredTimes[i + 1];
                if (segmentEnd <= segmentStart + .001) continue;
                int profile = ribbon.SegmentProfileIds[i];
                CalculateAirLadderPoint(note, segmentStart, now, profile,
                    out Vector3 leftA, out Vector3 rightA, out float uvA);
                CalculateAirLadderPoint(note, segmentEnd, now, profile,
                    out Vector3 leftB, out Vector3 rightB, out float uvB);
                PadShortAirLadder(note, ribbon, i, ref leftA, ref rightA, ref leftB, ref rightB);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        ChartSpace.NearZ, ChartSpace.FarZ + ChartSpace.JudgementZ + 8f))
                    continue;

                if (horizontal) {
                    const float edge = .045f;
                    buffers.AppendRibbonQuad(ref vertices, ref triangles,
                        leftA - Vector3.right * edge, leftA + Vector3.right * edge, uvA,
                        leftB - Vector3.right * edge, leftB + Vector3.right * edge, uvB);
                    buffers.AppendRibbonQuad(ref vertices, ref triangles,
                        rightA - Vector3.right * edge, rightA + Vector3.right * edge, uvA,
                        rightB - Vector3.right * edge, rightB + Vector3.right * edge, uvB);
                } else {
                    Vector3 centerA = (leftA + rightA) * .5f;
                    Vector3 centerB = (leftB + rightB) * .5f;
                    float halfA = Mathf.Min(.055f, Vector3.Distance(leftA, rightA) * .22f);
                    float halfB = Mathf.Min(.055f, Vector3.Distance(leftB, rightB) * .22f);
                    buffers.AppendRibbonQuad(ref vertices, ref triangles,
                        centerA - Vector3.right * halfA, centerA + Vector3.right * halfA, uvA,
                        centerB - Vector3.right * halfB, centerB + Vector3.right * halfB, uvB);
                }
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void CalculateAirLadderPoint(ChartNote note, double authored, double now, int profile,
            out Vector3 left, out Vector3 right, out float uv)
        {
            float p = note.EndTimeMs > note.TimeMs
                ? Mathf.Clamp01((float)((authored - note.TimeMs) / (note.EndTimeMs - note.TimeMs)))
                : 0f;
            float lane = Mathf.Lerp(note.Lane, note.EndLane, p);
            float width = Mathf.Lerp(note.Width, note.EndWidth, p);
            float z = Distance(profile, authored, now);
            float x = ChartSpace.NoteCenterX(lane, width, z);
            float half = Mathf.Max(.035f, ChartSpace.NoteHalfWidth(width, z) * .96f);
            float y = ChartSpace.AirY(Mathf.Lerp(note.AirHeight, note.EndAirHeight, p));
            left = new Vector3(x - half, y, z);
            right = new Vector3(x + half, y, z);
            uv = p;
        }

        private static void PadShortAirLadder(ChartNote note, RibbonGeometry ribbon, int segmentIndex,
            ref Vector3 leftA, ref Vector3 rightA, ref Vector3 leftB, ref Vector3 rightB)
        {
            if (note.EndTimeMs - note.TimeMs > 24.0) return;
            const float halfDepth = .24f;
            if (segmentIndex == 0) {
                leftA.z -= halfDepth;
                rightA.z -= halfDepth;
            }
            if (segmentIndex == ribbon.SegmentCount - 1) {
                leftB.z += halfDepth;
                rightB.z += halfDepth;
            }
        }

        private static bool IsDefaultAirLadderColor(string color)
        {
            string key = (color ?? "").Trim().ToUpperInvariant();
            return key.Length == 0 || key is "DEF" or "NON" or "12";
        }

        private void UpdateHeavenHoldVisual(NoteVisual visual, RibbonGeometry ribbon, ChartNote note,
            double now, float start, bool visible)
        {
            visual.Body.SetActive(visible);
            visual.Line.SetActive(visible);
            // The extracted shadow is a dark stage-projection texture. On the
            // mobile ground plane it becomes an opaque curved black ribbon,
            // so keep the Heaven roof and walls without projecting that band.
            visual.Accent.SetActive(false);
            if (!visible) return;

            // The executable keeps separate normal/pink Heaven roof tables.
            // Keep the verified normal table as the default until the actual
            // Heaven record exposes its runtime pink flag.
            visual.BodyRenderer.sharedMaterial = heavenRoof != null ? heavenRoof : heavenRoofPink;
            visual.LineRenderer.sharedMaterial = heavenWall;

            float roofY = ChartSpace.AirY(5f);
            UpdateHeavenSurface(visual.BodyMesh, visual.BodyBuffers, ribbon, note, now, start,
                roofY, .70f);
            UpdateHeavenWalls(visual.LineMesh, visual.LineBuffers, ribbon, note, now, start,
                roofY, .98f);
        }

        private void UpdateHeavenSurface(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start, float y, float widthScale)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(ribbon.SegmentCount);
            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            double cutoff = note.TimeMs + duration * Mathf.Clamp01(start);
            int vertices = 0;
            int triangles = 0;
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                double segmentStart = Math.Max(cutoff, ribbon.AuthoredTimes[i]);
                double segmentEnd = ribbon.AuthoredTimes[i + 1];
                if (segmentEnd <= segmentStart + .001) continue;
                int profile = ribbon.SegmentProfileIds[i];
                CalculateHeavenPoint(note, segmentStart, now, profile, y, widthScale,
                    out Vector3 leftA, out Vector3 rightA, out float uvA);
                CalculateHeavenPoint(note, segmentEnd, now, profile, y, widthScale,
                    out Vector3 leftB, out Vector3 rightB, out float uvB);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        ChartSpace.NearZ, ChartSpace.FarZ + ChartSpace.JudgementZ + 8f))
                    continue;
                buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void UpdateHeavenWalls(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start, float y, float widthScale)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(ribbon.SegmentCount * 2);
            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            double cutoff = note.TimeMs + duration * Mathf.Clamp01(start);
            int vertices = 0;
            int triangles = 0;
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                double segmentStart = Math.Max(cutoff, ribbon.AuthoredTimes[i]);
                double segmentEnd = ribbon.AuthoredTimes[i + 1];
                if (segmentEnd <= segmentStart + .001) continue;
                int profile = ribbon.SegmentProfileIds[i];
                CalculateHeavenPoint(note, segmentStart, now, profile, y, widthScale,
                    out Vector3 leftA, out Vector3 rightA, out float uvA);
                CalculateHeavenPoint(note, segmentEnd, now, profile, y, widthScale,
                    out Vector3 leftB, out Vector3 rightB, out float uvB);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        ChartSpace.NearZ, ChartSpace.FarZ + ChartSpace.JudgementZ + 8f))
                    continue;

                buffers.AppendWallQuad(ref vertices, ref triangles,
                    new Vector3(leftA.x, ChartSpace.GroundY, leftA.z), leftA,
                    new Vector3(leftB.x, ChartSpace.GroundY, leftB.z), leftB,
                    uvA, uvB, false);
                buffers.AppendWallQuad(ref vertices, ref triangles,
                    new Vector3(rightA.x, ChartSpace.GroundY, rightA.z), rightA,
                    new Vector3(rightB.x, ChartSpace.GroundY, rightB.z), rightB,
                    uvA, uvB, true);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void CalculateHeavenPoint(ChartNote note, double authored, double now, int profile,
            float y, float widthScale, out Vector3 left, out Vector3 right, out float uv)
        {
            float p = note.EndTimeMs > note.TimeMs
                ? Mathf.Clamp01((float)((authored - note.TimeMs) / (note.EndTimeMs - note.TimeMs)))
                : 0f;
            float lane = Mathf.Lerp(note.Lane, note.EndLane, p);
            float width = Mathf.Lerp(note.Width, note.EndWidth, p);
            float z = Distance(profile, authored, now);
            float x = ChartSpace.NoteCenterX(lane, width, z);
            float half = ChartSpace.NoteHalfWidth(width, z) * widthScale;
            left = new Vector3(x - half, y, z);
            right = new Vector3(x + half, y, z);
            uv = p;
        }

        private Material AirLineMaterial(string color)
        {
            string key = NormalizeAirLineColor(color);
            return airLineMaterials.TryGetValue(key, out Material material)
                ? material : airLineMaterials["GRN"];
        }

        private Material AirLineBodyMaterial(string color)
        {
            string key = NormalizeAirLineColor(color);
            return airLineBodyMaterials.TryGetValue(key, out Material material)
                ? material : airLineBodyMaterials["GRN"];
        }

        private void BuildAirLineMaterials()
        {
            var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) {
                { "RED", new Color(1f, .12f, .12f, 1f) },
                { "ORN", new Color(1f, .42f, .06f, 1f) },
                { "YEL", new Color(1f, .90f, .08f, 1f) },
                { "LIM", new Color(.55f, 1f, .08f, 1f) },
                { "GRN", new Color(.08f, 1f, .38f, 1f) },
                { "DGR", new Color(.02f, .58f, .20f, 1f) },
                { "AQA", new Color(.06f, .95f, 1f, 1f) },
                { "CYN", new Color(.02f, .62f, .72f, 1f) },
                { "BLU", new Color(.12f, .40f, 1f, 1f) },
                { "PPL", new Color(.42f, .12f, .92f, 1f) },
                { "VLT", new Color(.68f, .18f, 1f, 1f) },
                { "PNK", new Color(1f, .22f, .72f, 1f) },
                { "GRY", new Color(.68f, .72f, .78f, 1f) },
                { "BLK", new Color(.15f, .16f, .20f, 1f) },
                { "NON", new Color(.34f, .95f, .76f, 1f) }
            };
            foreach (var pair in colors) {
                Color lineColor = pair.Value;
                lineColor.a = .94f;
                Material line = MeshFactory.Material(lineColor, true);
                line.renderQueue = AirLineQueue;
                airLineMaterials[pair.Key] = line;

                Color bodyColor = pair.Value;
                bodyColor.a = .34f;
                Material body = MeshFactory.Material(bodyColor, true);
                body.renderQueue = AirDecorationQueue;
                airLineBodyMaterials[pair.Key] = body;
            }
        }

        private static string NormalizeAirLineColor(string color)
        {
            string key = (color ?? "").Trim().ToUpperInvariant();
            return key switch {
                "1" => "RED", "2" => "ORN", "3" => "YEL", "4" => "LIM",
                "5" or "DEF" => "GRN", "6" => "AQA",
                "7" => "BLU", "8" => "VLT", "9" => "PNK",
                "10" => "GRY", "11" => "BLK", "12" => "NON", _ => key
            };
        }

        private void UpdateRibbon(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon, ChartNote note, double now, float start, bool air, bool line)
        {
            buffers.BeginFrame();
            buffers.EnsureCapacity(ribbon.SegmentCount);
            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            double cutoff = note.TimeMs + duration * Mathf.Clamp01(start);
            int vertices = 0;
            int triangles = 0;
            for (int i = 0; i < ribbon.SegmentCount; i++) {
                double segmentStart = Math.Max(cutoff, ribbon.AuthoredTimes[i]);
                double segmentEnd = ribbon.AuthoredTimes[i + 1];
                if (segmentEnd <= segmentStart + .001) continue;

                // Both ends of one quad must use the same local scroll map.
                // Otherwise a profile switch joins unrelated Z positions and
                // creates the large diagonal triangles seen on complex charts.
                int profile = ribbon.SegmentProfileIds[i];
                CalculateRibbonPoint(note, segmentStart, now, profile, air, line,
                    out Vector3 leftA, out Vector3 rightA, out float uvA);
                CalculateRibbonPoint(note, segmentEnd, now, profile, air, line,
                    out Vector3 leftB, out Vector3 rightB, out float uvB);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        ChartSpace.NearZ, ChartSpace.FarZ + ChartSpace.JudgementZ + 8f))
                    continue;
                buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void CalculateRibbonPoint(ChartNote note, double authored, double now, int profile,
            bool air, bool line, out Vector3 left, out Vector3 right, out float uv)
        {
            float p = note.EndTimeMs > note.TimeMs
                ? Mathf.Clamp01((float)((authored - note.TimeMs) / (note.EndTimeMs - note.TimeMs)))
                : 0f;
            float lane = Mathf.Lerp(note.Lane, note.EndLane, p);
            float width = Mathf.Lerp(note.Width, note.EndWidth, p);
            float z = Distance(profile, authored, now);
            float x = ChartSpace.NoteCenterX(lane, width, z);
            float half = line ? FixedLineHalfWidth(air ? .115f : .032f) : ChartSpace.NoteHalfWidth(width, z) * .93f;
            if (air && !line) {
                // AIR-HOLD has a full-width translucent sheet and a separate
                // narrow centre line. Decorative lines remain thin.
                half = note.Kind == NoteKind.AirLine
                    ? FixedLineHalfWidth(.115f)
                    : note.Kind == NoteKind.AirHold ? half
                    : Mathf.Max(FixedLineHalfWidth(.115f), half * .34f);
            }
            float y = air ? ChartSpace.AirY(Mathf.Lerp(note.AirHeight, note.EndAirHeight, p)) : (line ? .085f : .055f);
            left = new Vector3(x - half, y, z);
            right = new Vector3(x + half, y, z);
            uv = p;
        }

        private static void CommitRibbon(Mesh mesh, RibbonBuffers buffers,
            int vertices, int triangles)
        {
            if (vertices < 4) { mesh.Clear(false); return; }
            mesh.Clear(false);
            mesh.SetVertices(buffers.Vertices, 0, vertices);
            mesh.SetUVs(0, buffers.Uv, 0, vertices);
            mesh.SetTriangles(buffers.Triangles, 0, triangles, 0, false);
            mesh.bounds = buffers.CurrentBounds;
        }

        private static bool ClipRibbonSegment(ref Vector3 leftA, ref Vector3 rightA, ref float uvA,
            ref Vector3 leftB, ref Vector3 rightB, ref float uvB, float near, float far)
        {
            if ((leftA.z < near && leftB.z < near) || (leftA.z > far && leftB.z > far)) return false;
            if (leftA.z < near) ClipStart(ref leftA, ref rightA, ref uvA, leftB, rightB, uvB, near);
            else if (leftA.z > far) ClipStart(ref leftA, ref rightA, ref uvA, leftB, rightB, uvB, far);
            if (leftB.z < near) ClipEnd(leftA, rightA, uvA, ref leftB, ref rightB, ref uvB, near);
            else if (leftB.z > far) ClipEnd(leftA, rightA, uvA, ref leftB, ref rightB, ref uvB, far);
            return true;
        }

        private static void ClipStart(ref Vector3 leftA, ref Vector3 rightA, ref float uvA,
            Vector3 leftB, Vector3 rightB, float uvB, float z)
        {
            float t = Mathf.Approximately(leftA.z, leftB.z) ? 0f : Mathf.Clamp01((z - leftA.z) / (leftB.z - leftA.z));
            leftA = Vector3.Lerp(leftA, leftB, t);
            rightA = Vector3.Lerp(rightA, rightB, t);
            uvA = Mathf.Lerp(uvA, uvB, t);
        }

        private static void ClipEnd(Vector3 leftA, Vector3 rightA, float uvA,
            ref Vector3 leftB, ref Vector3 rightB, ref float uvB, float z)
        {
            float t = Mathf.Approximately(leftA.z, leftB.z) ? 1f : Mathf.Clamp01((z - leftA.z) / (leftB.z - leftA.z));
            leftB = Vector3.Lerp(leftA, leftB, t);
            rightB = Vector3.Lerp(rightA, rightB, t);
            uvB = Mathf.Lerp(uvA, uvB, t);
        }

        // Keep the original mesh-space thickness. The gameplay camera already
        // makes distant geometry narrower; tapering here caused double perspective.
        private static float FixedLineHalfWidth(float width) => width;

        private void UpdateHead(GameObject target, MeshFilter filter, Mesh fallbackMesh, MeshRenderer renderer, int index, ChartNote note, float z, bool tail)
        {
            int lane = tail ? note.EndLane : note.Lane;
            int width = Mathf.Clamp(tail ? note.EndWidth : note.Width, 1, 16);
            float x = ChartSpace.NoteCenterX(lane, width, z), half = ChartSpace.NoteHalfWidth(width, z);
            if (note.Kind == NoteKind.Air) {
                if (!tail && pairedGroundByAir.TryGetValue(index, out ChartNote ground)) { lane = ground.Lane; width = Mathf.Clamp(ground.Width, 1, 16); x = ChartSpace.NoteCenterX(lane, width, z); half = ChartSpace.NoteHalfWidth(width, z); }
                bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
                bool tilted = note.AirDirection is AirDirection.UpLeft or AirDirection.UpRight or AirDirection.DownLeft or AirDirection.DownRight;
                // The extracted tilted meshes are authored from the right
                // facing base pose. Left-facing chart opcodes therefore use
                // the mirrored mesh; this keeps AUL/ADL visually left of the
                // lane just like the original playfield.
                bool mirror = note.AirDirection is AirDirection.UpRight or AirDirection.DownRight;
                string meshKind = down ? (tilted ? "airdwtilt" : "airdw") : (tilted ? "airuptilt" : "airup");
                if (!SetOriginalAirMesh(target, filter, meshKind, width, x, z, note.AirHeight, mirror)) {
                    filter.sharedMesh = fallbackMesh;
                    SetAirTriangle(fallbackMesh, x, half, z, note.AirHeight, note.AirDirection);
                    target.transform.localPosition = Vector3.zero;
                    target.transform.localRotation = Quaternion.identity;
                    target.transform.localScale = Vector3.one;
                }
                renderer.sharedMaterial = HeadMaterial(down ? "air_down" : "air_up", width);
                return;
            }
            if (note.Kind is NoteKind.AirHold or NoteKind.AirAction) {
                float height = tail ? note.EndAirHeight : note.AirHeight;
                bool action = note.Kind == NoteKind.AirAction;
                string meshKind = action ? "ahact" : (tail ? "ahtapend" : "ahtap");
                bool placed = action
                    ? SetOriginalAirActionMesh(target, filter, meshKind, width, x, z, height)
                    : SetOriginalAirHoldMesh(target, filter, meshKind, width, x, z, height, tail);
                if (!placed) {
                    filter.sharedMesh = fallbackMesh;
                    SetAirCapQuad(fallbackMesh, x, half * (action ? .72f : .56f), z,
                        ChartSpace.AirY(height), tail ? .38f : .52f);
                    target.transform.localPosition = Vector3.zero;
                    target.transform.localRotation = Quaternion.identity;
                    target.transform.localScale = Vector3.one;
                }
                renderer.sharedMaterial = HeadMaterial(meshKind, width);
                return;
            }
            bool exHoldHead = !tail && note.Kind == NoteKind.Hold &&
                string.Equals(note.OriginalType, "HXD", StringComparison.OrdinalIgnoreCase);
            string key = note.Kind switch {
                NoteKind.ExTap => "extap",
                NoteKind.Flick => "flick",
                NoteKind.Hold when exHoldHead => "extap",
                NoteKind.Hold or NoteKind.HeavenHold => tail ? "holdend" : "hold",
                NoteKind.Slide => tail ? "slideend" : "slide",
                NoteKind.Mine => "dmg",
                _ => "tap"
            };
            if (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Hold or
                 NoteKind.HeavenHold or NoteKind.Slide or NoteKind.Mine) {
                // These meshes are the extracted per-width original planes.
                // Their authored aspect ratio includes the original depth;
                // scaling the mesh from its X bounds preserves that ratio and
                // avoids the old one-size quad that made heads look too short.
                // The extracted EXTAP model includes the arcade cabinet's
                // raised side shell. On a perspective mobile playfield that
                // shell looks like a bucket around the note. The playable
                // surface is the same flat per-width plane as TAP; the gold
                // EXTAP artwork remains an independent skin material.
                string geometryKey = key == "extap" ? "tap" : key;
                if (!SetOriginalMesh(target, filter, geometryKey, width, x, ChartSpace.GroundY, z, false)) {
                    filter.sharedMesh = fallbackMesh;
                    SetGroundQuad(fallbackMesh, x, half, z, note.Kind == NoteKind.Mine ? .52f : .66f);
                    target.transform.localPosition = Vector3.zero;
                    target.transform.localRotation = Quaternion.identity;
                    target.transform.localScale = Vector3.one;
                }
                renderer.sharedMaterial = HeadMaterial(key == "holdend" ? "hold" : key == "slideend" ? "slide" : key, width);
                if (filter.sharedMesh != fallbackMesh &&
                    GameplaySkinManager.IsExternalNoteTexture(renderer.sharedMaterial.mainTexture))
                    filter.sharedMesh = noteSkinUvs.Get(filter.sharedMesh, (Texture2D)renderer.sharedMaterial.mainTexture);
                return;
            }
            if (!SetOriginalMesh(target, filter, key, width, x, .105f, z, false)) {
                filter.sharedMesh = fallbackMesh;
                SetGroundQuad(fallbackMesh, x, half, z, .64f);
                target.transform.localPosition = Vector3.zero;
                target.transform.localScale = Vector3.one;
            }
            string textureKey = key == "holdend" ? "hold" : key == "slideend" ? "slide" : key;
            renderer.sharedMaterial = HeadMaterial(textureKey, width);
        }

        private bool SetOriginalAirHoldMesh(GameObject target, MeshFilter filter, string kind, int width,
            float x, float z, float airHeight, bool tail)
        {
            // AHTAP/AHTAPEND are authored as a shallow horizontal model. Keep
            // the original UVs and footprint, but normalize its depth to the
            // same compact head size used by the arcade renderer.
            return SetOriginalModel(target, filter, kind, width, x, ChartSpace.AirY(airHeight), z,
                tail ? .46f : .54f, .01f);
        }

        private bool SetOriginalAirActionMesh(GameObject target, MeshFilter filter, string kind, int width,
            float x, float z, float airHeight)
        {
            // AHACT is a small raised purple action block. Non-uniform scaling
            // preserves its authored silhouette without importing the model's
            // eight-unit Z span into the mobile track.
            return SetOriginalModel(target, filter, kind, width, x, ChartSpace.AirY(airHeight), z,
                .40f, .42f);
        }

        private bool SetOriginalModel(GameObject target, MeshFilter filter, string kind, int width,
            float x, float centerY, float z, float targetDepth, float targetHeight)
        {
            string key = kind + "_" + Mathf.Clamp(width, 1, 16).ToString("00");
            if (!originalMeshes.TryGetValue(key, out Mesh mesh)) return false;
            Bounds bounds = mesh.bounds;
            if (bounds.size.x <= .0001f || bounds.size.z <= .0001f) return false;

            float targetWidth = ChartSpace.NoteHalfWidth(width, z) * 2f;
            float scaleX = targetWidth / bounds.size.x;
            float scaleZ = Mathf.Max(.001f, targetDepth) / bounds.size.z;
            float scaleY = bounds.size.y > .0001f && targetHeight > .0001f
                ? targetHeight / bounds.size.y : 1f;
            filter.sharedMesh = mesh;
            target.transform.localPosition = new Vector3(
                x - bounds.center.x * scaleX,
                centerY - bounds.center.y * scaleY,
                z - bounds.center.z * scaleZ);
            target.transform.localRotation = Quaternion.identity;
            target.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
            return true;
        }

        private bool SetOriginalMesh(GameObject target, MeshFilter filter, string kind, int width, float x, float y, float z, bool mirror)
        {
            string key = kind + "_" + Mathf.Clamp(width, 1, 16).ToString("00");
            if (!originalMeshes.TryGetValue(key, out Mesh mesh)) return false;
            // Original meshes are authored in a fixed model space. Their X
            // extent must follow the track at this depth, otherwise a narrow
            // far end still renders a full-size note over the lane borders.
            float sourceWidth = mesh.bounds.size.x;
            if (sourceWidth <= .0001f) return false;
            float targetWidth = ChartSpace.NoteHalfWidth(width, z) * 2f;
            float scaleX = targetWidth / sourceWidth;
            Bounds bounds = mesh.bounds;
            // The extracted ground resources share the lane-width table, but
            // their authored Z spans are not a gameplay unit. Applying the X
            // scale to Z made the head depth depend on the asset and caused
            // notes to overlap the judgement line by several visual lanes.
            float targetDepth = kind == "dmg" ? .54f : .66f;
            float scaleZ = targetDepth / Mathf.Max(.0001f, bounds.size.z);
            // EXTap and mine resources contain a small vertical silhouette;
            // normalize it while keeping its bottom on the ground plane.
            float scaleY = bounds.size.y > .0001f ? .66f / bounds.size.y : 1f;
            filter.sharedMesh = mesh;
            // Imported arcade planes are not centered on their transform
            // origin (their authored Z range is about -4.5..5.6). Center the
            // actual resource on the chart-space point so its visible center
            // sits on the lane and the gold judgement plane.
            target.transform.localPosition = new Vector3(
                x - bounds.center.x * scaleX,
                y - bounds.min.y * scaleY,
                z - bounds.center.z * scaleZ);
            target.transform.localRotation = Quaternion.identity;
            target.transform.localScale = new Vector3(mirror ? -scaleX : scaleX, scaleY, scaleZ);
            return true;
        }

        private bool SetOriginalAirMesh(GameObject target, MeshFilter filter, string kind, int width, float x, float z, float airHeight, bool mirror)
        {
            string key = kind + "_" + Mathf.Clamp(width, 1, 16).ToString("00");
            if (!originalMeshes.TryGetValue(key, out Mesh mesh)) return false;
            if (mirror) mesh = MirroredAirMesh(key, mesh);
            Bounds bounds = mesh.bounds;
            if (bounds.size.x <= 0f || bounds.max.y <= 0f) return false;

            float targetWidth = ChartSpace.NoteHalfWidth(width, z) * 2f;
            float scaleX = targetWidth / bounds.size.x;
            float airBaseY = .18f;
            float authoredTopY = Mathf.Max(airBaseY + .8f, ChartSpace.AirY(airHeight));
            float targetTopY = airBaseY + (authoredTopY - airBaseY) * .72f;
            float scaleY = (targetTopY - airBaseY) / bounds.max.y;
            // Diagonal models carry real authored Z depth. It is independent
            // from lane width; scaling it with X made wide AIR arrows become
            // thick slabs and moved their tips away from the timing plane.
            float scaleZ = bounds.size.z > .0001f ? .62f / bounds.size.z : 1f;

            filter.sharedMesh = mesh;
            // Tilted AIR meshes are authored several units behind their local
            // origin. Without centering this depth, diagonal AIR heads render
            // early/late relative to the ground note and can be hidden by the
            // track surface near the judgement line.
            target.transform.localPosition = new Vector3(
                x - bounds.center.x * scaleX,
                airBaseY - bounds.min.y * scaleY,
                z - bounds.center.z * scaleZ);
            target.transform.localRotation = Quaternion.identity;
            // The mirrored copy already has reversed triangle winding. Keep a
            // positive transform scale so back-face culling cannot erase it.
            target.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
            return true;
        }

        private Mesh MirroredAirMesh(string key, Mesh source)
        {
            if (mirroredAirMeshes.TryGetValue(key, out Mesh mirrored)) return mirrored;
            mirrored = UnityEngine.Object.Instantiate(source);
            mirrored.name = source.name + " (mirrored)";
            Vector3[] vertices = mirrored.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].x = -vertices[i].x;
            mirrored.vertices = vertices;
            int[] triangles = mirrored.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3) {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            mirrored.triangles = triangles;
            mirrored.RecalculateBounds();
            mirroredAirMeshes[key] = mirrored;
            return mirrored;
        }

        private Material HeadMaterial(string kind, int width)
        {
            string key = kind + "_" + Mathf.Clamp(width, 1, 16).ToString("00");
            if (headMaterials.TryGetValue(key, out Material material)) return material;
            if (kind.StartsWith("air_", StringComparison.Ordinal)) return defaultAirArrow;
            if (kind.StartsWith("ah", StringComparison.Ordinal)) return defaultAirHead;
            return headMaterials.TryGetValue("tap_" + Mathf.Clamp(width, 1, 16).ToString("00"), out material)
                ? material : defaultHead;
        }

        private void BuildAirPairs(IReadOnlyList<ChartNote> notes)
        {
            // AIR is authored on the same timing plane as its ground partner.
            // Looking only at nearby sorted records is not sufficient: a dense
            // chord can put more than eight objects between the two records,
            // and a nearby-but-different tick can then steal the AIR position.
            var groundsByTime = new Dictionary<long, List<ChartNote>>();
            for (int i = 0; i < notes.Count; i++) {
                ChartNote ground = notes[i];
                if (ground.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction or NoteKind.AirLadder or NoteKind.AirLine)
                    continue;
                long key = AirPairTimeKey(ground.TimeMs);
                if (!groundsByTime.TryGetValue(key, out List<ChartNote> grounds)) {
                    grounds = new List<ChartNote>();
                    groundsByTime.Add(key, grounds);
                }
                grounds.Add(ground);
            }

            for (int i = 0; i < notes.Count; i++) {
                ChartNote air = notes[i];
                if (air.Kind != NoteKind.Air) continue;
                long key = AirPairTimeKey(air.TimeMs);
                ChartNote best = null;
                bool bestSourceMatch = false;
                int bestOverlap = 0;
                float bestCenterDistance = float.PositiveInfinity;
                // Query adjacent buckets because the same authored timestamp
                // can land on either side of a rounded millisecond boundary.
                for (long bucket = key - 1; bucket <= key + 1; bucket++) {
                    if (!groundsByTime.TryGetValue(bucket, out List<ChartNote> grounds)) continue;
                    foreach (ChartNote ground in grounds) {
                        double timeDelta = Math.Abs(ground.TimeMs - air.TimeMs);
                        if (timeDelta > .1) continue;
                        int overlap = Math.Max(0, Math.Min(ground.Lane + Math.Max(1, ground.Width),
                            air.Lane + Math.Max(1, air.Width)) - Math.Max(ground.Lane, air.Lane));
                        if (overlap <= 0) continue;
                        float groundCenter = ground.Lane + Math.Max(1, ground.Width) * .5f;
                        float airCenter = air.Lane + Math.Max(1, air.Width) * .5f;
                        float centerDistance = Mathf.Abs(groundCenter - airCenter);
                        bool sourceMatch = !string.IsNullOrWhiteSpace(air.SourceType) &&
                            string.Equals(air.SourceType, ground.OriginalType, StringComparison.OrdinalIgnoreCase);
                        if (best == null || sourceMatch && !bestSourceMatch ||
                            (sourceMatch == bestSourceMatch && (overlap > bestOverlap ||
                            (overlap == bestOverlap && centerDistance < bestCenterDistance)))) {
                            best = ground;
                            bestSourceMatch = sourceMatch;
                            bestOverlap = overlap;
                            bestCenterDistance = centerDistance;
                        }
                    }
                }
                if (best != null) pairedGroundByAir[i] = best;
            }
        }

        private static long AirPairTimeKey(double timeMs) =>
            (long)Math.Round(timeMs * 10.0, MidpointRounding.AwayFromZero);

        public void MarkJudged(int noteIndex, int pointIndex)
        {
            if (chart == null || noteIndex < 0 || noteIndex >= chart.Notes.Count) return;
            ChartNote note = chart.Notes[noteIndex];
            if (note.HasVisualHead && note.VisibleHead && pointIndex == 0) judgedHeads.Add(noteIndex);
        }

        private bool IsUnresolvedHead(ChartNote note, int noteIndex, double now)
        {
            if (note == null || !note.HasVisualHead || judgedHeads.Contains(noteIndex)) return false;
            if (note.Kind == NoteKind.Slide && !note.VisibleHead) return false;
            if (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Mine or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide)
                return true;
            if (note.Kind is not (NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction)) return false;
            bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            return now <= note.TimeMs + JudgementEngine.MissWindowMs(note.Kind, down, note.AirFlick) * clock.Rate;
        }

        private RibbonGeometry BuildGeometry(ChartNote note)
        {
            // Keep uniform authored samples for ordinary curves, but always add
            // SFL/SLP/STP/SLA boundaries. A long object can cross a local speed
            // boundary while its endpoints remain outside the screen; without
            // these knots the renderer draws a straight shortcut or loses the
            // visible middle section.
            int minimumSamples = note.Kind is NoteKind.AirLadder or NoteKind.AirLine ? 48 : 12;
            if (note.EndTimeMs <= note.TimeMs) {
                var point = new RibbonGeometry(2);
                point.AuthoredTimes[0] = point.AuthoredTimes[1] = note.TimeMs;
                point.SegmentProfileIds[0] = playback.FindProfile(
                    note.TimeMs, note.Lane, Math.Max(1, note.Width));
                return point;
            }

            int uniformCount = Mathf.Max(minimumSamples, RibbonSamples);
            var times = new List<double>(uniformCount + 24);
            void AddBoundary(double time)
            {
                if (time <= note.TimeMs + .001 || time >= note.EndTimeMs - .001) return;
                times.Add(time);
            }
            void AddRoundedTransitions(float from, float to)
            {
                if (Mathf.Approximately(from, to)) return;
                float low = Mathf.Min(from, to);
                float high = Mathf.Max(from, to);
                int first = Mathf.CeilToInt(low - .5f);
                int last = Mathf.FloorToInt(high - .5f);
                for (int integer = first; integer <= last; integer++) {
                    float threshold = integer + .5f;
                    float progress = (threshold - from) / (to - from);
                    if (progress > 0f && progress < 1f)
                        AddBoundary(note.TimeMs + (note.EndTimeMs - note.TimeMs) * progress);
                }
            }
            for (int i = 0; i < uniformCount; i++)
                times.Add(note.TimeMs + (note.EndTimeMs - note.TimeMs) * i / (double)(uniformCount - 1));
            foreach (ScrollEvent scroll in chart.ScrollEvents) {
                AddBoundary(scroll.StartMs);
                AddBoundary(scroll.EndMs);
            }
            foreach (var profile in chart.FieldScrollProfiles.Values)
                foreach (ScrollEvent scroll in profile) {
                    AddBoundary(scroll.StartMs);
                    AddBoundary(scroll.EndMs);
                }
            foreach (ScrollAssignment assignment in chart.ScrollAssignments) {
                float minimumLeft = Mathf.Min(note.Lane, note.EndLane);
                float maximumRight = Mathf.Max(note.Lane + Math.Max(1, note.Width),
                    note.EndLane + Math.Max(1, note.EndWidth));
                if (minimumLeft >= assignment.Lane + assignment.Width ||
                    assignment.Lane >= maximumRight) continue;
                AddBoundary(assignment.StartMs);
                AddBoundary(assignment.EndMs);
            }
            // FindProfile receives rounded lane and width values. Add every
            // half-lane crossing so a lateral slide cannot change profile in
            // the middle of a rendered quad.
            AddRoundedTransitions(note.Lane, note.EndLane);
            AddRoundedTransitions(Math.Max(1, note.Width), Math.Max(1, note.EndWidth));
            times.Sort();
            var unique = new List<double>(times.Count);
            foreach (double time in times)
                if (unique.Count == 0 || Math.Abs(time - unique[unique.Count - 1]) > .001)
                    unique.Add(time);

            var result = new RibbonGeometry(unique.Count);
            for (int i = 0; i < unique.Count; i++) {
                result.AuthoredTimes[i] = unique[i];
            }
            for (int i = 0; i < result.SegmentCount; i++) {
                double authored = (result.AuthoredTimes[i] + result.AuthoredTimes[i + 1]) * .5;
                float p = Mathf.Clamp01((float)((authored - note.TimeMs) /
                    (note.EndTimeMs - note.TimeMs)));
                int lane = Mathf.RoundToInt(Mathf.Lerp(note.Lane, note.EndLane, p));
                int width = Mathf.Max(1,
                    Mathf.RoundToInt(Mathf.Lerp(note.Width, note.EndWidth, p)));
                result.SegmentProfileIds[i] = playback.FindProfile(authored, lane, width);
            }
            return result;
        }

        private void OnDestroy()
        {
            foreach (var visual in active.Values) visual.Dispose();
            foreach (var visual in pool) visual.Dispose();
            active.Clear();
            pool.Clear();
            noteSkinUvs.Clear();
        }

        private NoteVisual Rent()
        {
            while (pool.Count > 0) {
                NoteVisual cached = pool.Pop();
                if (cached.Valid) { cached.Root.SetActive(true); return cached; }
                cached.Dispose();
            }
            if (notesRoot == null) {
                notesRoot = new GameObject("Note geometry").transform;
                notesRoot.SetParent(transform, false);
            }
            return new NoteVisual(notesRoot);
        }
        private void Return(NoteVisual v)
        {
            if (!v.Valid) { v.Dispose(); return; }
            v.Root.SetActive(false);
            pool.Push(v);
        }
        private static void ReleaseOwnedObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
        private void ClearActive() { foreach (var item in active) Return(item.Value); active.Clear(); }

        private static readonly Vector3[] QuadVertices = new Vector3[4];
        private static readonly Vector2[] QuadUvs = new Vector2[4];
        private static readonly int[] QuadTriangles = { 0, 2, 1, 0, 3, 2 };
        private static readonly Vector3[] TriangleVertices = new Vector3[3];
        private static readonly Vector2[] TriangleUvs = new Vector2[3];
        private static readonly int[] TriangleTriangles = { 0, 2, 1 };

        private static void SetGroundQuad(Mesh mesh, float x, float half, float z, float depth)
        {
            float halfDepth = depth * .5f;
            QuadVertices[0] = new Vector3(x-half, ChartSpace.GroundY, z-halfDepth);
            QuadVertices[1] = new Vector3(x+half, ChartSpace.GroundY, z-halfDepth);
            QuadVertices[2] = new Vector3(x+half, ChartSpace.GroundY, z+halfDepth);
            QuadVertices[3] = new Vector3(x-half, ChartSpace.GroundY, z+halfDepth);
            QuadUvs[0] = Vector2.zero; QuadUvs[1] = Vector2.right;
            QuadUvs[2] = Vector2.one; QuadUvs[3] = Vector2.up;
            mesh.Clear(false); mesh.vertices = QuadVertices; mesh.uv = QuadUvs;
            mesh.triangles = QuadTriangles;
            mesh.bounds = new Bounds(new Vector3(x, ChartSpace.GroundY, z),
                new Vector3(half * 2f, .01f, depth));
        }

        private static void SetAirTriangle(Mesh mesh, float x, float half, float z, float height, AirDirection direction)
        {
            bool down = direction is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            float centerY = ChartSpace.AirY(height);
            float low = Mathf.Max(.45f, centerY - .70f);
            float high = centerY + .70f;
            float shift = direction is AirDirection.UpLeft or AirDirection.DownRight ? -half*.50f : direction is AirDirection.UpRight or AirDirection.DownLeft ? half*.50f : 0f;
            TriangleVertices[0] = new Vector3(x-half, down ? high : low, z);
            TriangleVertices[1] = new Vector3(x+half, down ? high : low, z);
            TriangleVertices[2] = new Vector3(x+shift, down ? low : high, z-.015f);
            if (down) {
                TriangleUvs[0] = Vector2.up; TriangleUvs[1] = Vector2.one; TriangleUvs[2] = new Vector2(.5f, 0);
            } else {
                TriangleUvs[0] = Vector2.zero; TriangleUvs[1] = Vector2.right; TriangleUvs[2] = new Vector2(.5f, 1);
            }
            mesh.Clear(false); mesh.vertices = TriangleVertices; mesh.uv = TriangleUvs;
            mesh.triangles = TriangleTriangles;
            mesh.bounds = new Bounds(new Vector3(x, (low + high) * .5f, z),
                new Vector3(half * 2f, high - low, .03f));
        }

        private static void SetAirCapQuad(Mesh mesh, float x, float half, float z, float y, float depth)
        {
            float d = depth * .5f;
            QuadVertices[0] = new Vector3(x-half, y, z-d);
            QuadVertices[1] = new Vector3(x+half, y, z-d);
            QuadVertices[2] = new Vector3(x+half, y, z+d);
            QuadVertices[3] = new Vector3(x-half, y, z+d);
            QuadUvs[0] = Vector2.zero; QuadUvs[1] = Vector2.right;
            QuadUvs[2] = Vector2.one; QuadUvs[3] = Vector2.up;
            mesh.Clear(false); mesh.vertices = QuadVertices; mesh.uv = QuadUvs;
            mesh.triangles = QuadTriangles;
            mesh.bounds = new Bounds(new Vector3(x, y, z), new Vector3(half * 2f, .01f, depth));
        }

        private sealed class NoteVisual
        {
            public bool Valid => Root != null && Head != null && Body != null && Line != null && Tail != null && Accent != null;
            public void Dispose()
            {
                ReleaseOwnedObject(HeadMesh); ReleaseOwnedObject(BodyMesh); ReleaseOwnedObject(LineMesh);
                ReleaseOwnedObject(TailMesh); ReleaseOwnedObject(AccentMesh); ReleaseOwnedObject(Root);
            }
            public readonly GameObject Root,Head,Body,Line,Tail,Accent; public readonly Mesh HeadMesh,BodyMesh,LineMesh,TailMesh,AccentMesh; public readonly MeshFilter HeadFilter,BodyFilter,LineFilter,TailFilter,AccentFilter; public readonly MeshRenderer HeadRenderer,BodyRenderer,LineRenderer,TailRenderer,AccentRenderer;
            public readonly RibbonBuffers BodyBuffers = new RibbonBuffers();
            public readonly RibbonBuffers LineBuffers = new RibbonBuffers();
            public readonly RibbonBuffers AccentBuffers = new RibbonBuffers();
            public NoteVisual(Transform parent) { Root=new GameObject("Pooled original note"); Root.transform.SetParent(parent,false); Create(Root.transform,"Accent",out Accent,out AccentMesh,out AccentFilter,out AccentRenderer); Create(Root.transform,"Body",out Body,out BodyMesh,out BodyFilter,out BodyRenderer); Create(Root.transform,"Centre line",out Line,out LineMesh,out LineFilter,out LineRenderer); Create(Root.transform,"Tail",out Tail,out TailMesh,out TailFilter,out TailRenderer); Create(Root.transform,"Head",out Head,out HeadMesh,out HeadFilter,out HeadRenderer); }
            private static void Create(Transform p,string n,out GameObject go,out Mesh mesh,out MeshFilter filter,out MeshRenderer renderer) { go=new GameObject(n); go.transform.SetParent(p,false); mesh=new Mesh{name="Dynamic "+n}; mesh.MarkDynamic(); filter=go.AddComponent<MeshFilter>(); filter.sharedMesh=mesh; renderer=go.AddComponent<MeshRenderer>(); renderer.sortingOrder=0; }
        }

        private sealed class RibbonBuffers
        {
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public Vector2[] Uv = Array.Empty<Vector2>();
            public int[] Triangles = Array.Empty<int>();
            private Vector3 boundsMin;
            private Vector3 boundsMax;
            private bool hasBounds;

            public Bounds CurrentBounds => hasBounds
                ? new Bounds((boundsMin + boundsMax) * .5f, boundsMax - boundsMin)
                : new Bounds(Vector3.zero, Vector3.zero);

            public void BeginFrame()
            {
                hasBounds = false;
            }

            public void EnsureCapacity(int quadCount)
            {
                int requiredVertices = Math.Max(0, quadCount) * 4;
                int requiredTriangles = Math.Max(0, quadCount) * 6;
                if (Vertices.Length < requiredVertices) {
                    int capacity = Math.Max(requiredVertices, Math.Max(8, Vertices.Length * 2));
                    Array.Resize(ref Vertices, capacity);
                    Array.Resize(ref Uv, capacity);
                }
                if (Triangles.Length < requiredTriangles)
                    Array.Resize(ref Triangles,
                        Math.Max(requiredTriangles, Math.Max(12, Triangles.Length * 2)));
            }

            private void Include(Vector3 value)
            {
                if (!hasBounds) {
                    boundsMin = boundsMax = value;
                    hasBounds = true;
                    return;
                }
                boundsMin = Vector3.Min(boundsMin, value);
                boundsMax = Vector3.Max(boundsMax, value);
            }

            public void AppendRibbonQuad(ref int vertexCount, ref int triangleCount,
                Vector3 leftA, Vector3 rightA, float uvA,
                Vector3 leftB, Vector3 rightB, float uvB)
            {
                int start = vertexCount;
                int requiredVertices = start + 4;
                if (Vertices.Length < requiredVertices) {
                    int capacity = Math.Max(requiredVertices, Math.Max(8, Vertices.Length * 2));
                    Array.Resize(ref Vertices, capacity);
                    Array.Resize(ref Uv, capacity);
                }
                int requiredTriangles = triangleCount + 6;
                if (Triangles.Length < requiredTriangles)
                    Array.Resize(ref Triangles, Math.Max(requiredTriangles, Math.Max(12, Triangles.Length * 2)));

                Vertices[start] = leftA;
                Vertices[start + 1] = rightA;
                Vertices[start + 2] = leftB;
                Vertices[start + 3] = rightB;
                Include(leftA); Include(rightA); Include(leftB); Include(rightB);
                Uv[start] = new Vector2(0, uvA);
                Uv[start + 1] = new Vector2(1, uvA);
                Uv[start + 2] = new Vector2(0, uvB);
                Uv[start + 3] = new Vector2(1, uvB);
                Triangles[triangleCount] = start;
                Triangles[triangleCount + 1] = start + 2;
                Triangles[triangleCount + 2] = start + 1;
                Triangles[triangleCount + 3] = start + 1;
                Triangles[triangleCount + 4] = start + 2;
                Triangles[triangleCount + 5] = start + 3;
                vertexCount += 4;
                triangleCount += 6;
            }

            public void AppendWallQuad(ref int vertexCount, ref int triangleCount,
                Vector3 bottomA, Vector3 topA, Vector3 bottomB, Vector3 topB,
                float uvA, float uvB, bool reverse)
            {
                int start = vertexCount;
                int requiredVertices = start + 4;
                if (Vertices.Length < requiredVertices) {
                    int capacity = Math.Max(requiredVertices, Math.Max(8, Vertices.Length * 2));
                    Array.Resize(ref Vertices, capacity);
                    Array.Resize(ref Uv, capacity);
                }
                int requiredTriangles = triangleCount + 6;
                if (Triangles.Length < requiredTriangles)
                    Array.Resize(ref Triangles, Math.Max(requiredTriangles, Math.Max(12, Triangles.Length * 2)));

                Vertices[start] = bottomA;
                Vertices[start + 1] = topA;
                Vertices[start + 2] = bottomB;
                Vertices[start + 3] = topB;
                Include(bottomA); Include(topA); Include(bottomB); Include(topB);
                Uv[start] = new Vector2(uvA, 0f);
                Uv[start + 1] = new Vector2(uvA, 1f);
                Uv[start + 2] = new Vector2(uvB, 0f);
                Uv[start + 3] = new Vector2(uvB, 1f);
                if (reverse) {
                    Triangles[triangleCount] = start;
                    Triangles[triangleCount + 1] = start + 1;
                    Triangles[triangleCount + 2] = start + 2;
                    Triangles[triangleCount + 3] = start + 2;
                    Triangles[triangleCount + 4] = start + 1;
                    Triangles[triangleCount + 5] = start + 3;
                } else {
                    Triangles[triangleCount] = start;
                    Triangles[triangleCount + 1] = start + 2;
                    Triangles[triangleCount + 2] = start + 1;
                    Triangles[triangleCount + 3] = start + 2;
                    Triangles[triangleCount + 4] = start + 3;
                    Triangles[triangleCount + 5] = start + 1;
                }
                vertexCount += 4;
                triangleCount += 6;
            }

        }

        private sealed class RibbonGeometry
        {
            public readonly double[] AuthoredTimes;
            public readonly int[] SegmentProfileIds;
            public int Count => AuthoredTimes.Length;
            public int SegmentCount => Math.Max(0, AuthoredTimes.Length - 1);
            public int EndProfileId => SegmentProfileIds.Length > 0
                ? SegmentProfileIds[SegmentProfileIds.Length - 1] : 0;

            public RibbonGeometry(int count)
            {
                AuthoredTimes = new double[count];
                SegmentProfileIds = new int[Math.Max(1, count - 1)];
            }
        }
    }
}
