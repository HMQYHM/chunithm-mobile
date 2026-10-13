using System;
using System.Collections.Generic;
using System.Linq;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Rendering
{
    // 游玩谱面渲染器：按 DSP 谱面时间创建、更新、隐藏和回收音符视觉对象。
    // 地面音符、AIR 本体和 AIR 拖尾有独立材质/生命周期；本类不负责判定和成绩。
    /// <summary>Original-resource renderer with independent head/body/line/tail lifetimes.</summary>
    public sealed class ChartPlaybackRenderer : MonoBehaviour
    {
        private const double VisibilityLeadRealMilliseconds = 120.0;
        private const double PostHeadRenderGraceMilliseconds = 900.0;
        // Rendering-only guard for tiny backwards samples from platform audio
        // adapters. Judgement still uses the untouched DSP chart clock.
        private const double RenderTimeBackstepToleranceMilliseconds = 8.0;
        private const float MinimumVisibilityGuardZ = 2f;
        private const float MaximumVisibilityGuardZ = 96f;
        // A moving ribbon must not be created/destroyed because a floating
        // point value crosses the camera clip plane by a few microns. Keep a
        // small spatial hysteresis band; the line is still outside the play
        // field before it is finally recycled by ProcessNote.
        private const float RibbonClipEpsilonZ = .08f;
        // Keep ribbon clipping outside the visible judgement area. Rebuilding
        // a segment exactly as it crosses the near plane causes mesh topology
        // churn and visible shaking on consecutive frames.
        private const float RibbonBodyNearClipZ = ChartSpace.NearZ - 8f;
        private const float RibbonBodyFarClipZ = ChartSpace.FarZ + ChartSpace.JudgementZ + 8f;
        // Guide lines are submitted as complete authored segments. The camera
        // performs the final viewport clipping, so their first visible segment
        // does not change topology while crossing the screen.
        private const float GuidePathNearCullZ = ChartSpace.NearZ - 24f;
        private const float GuidePathFarCullZ = ChartSpace.FarZ + ChartSpace.JudgementZ + 24f;
        // Zero-duration ALD records still need a continuous visual crossing.
        // They use the same guide-path mesh pipeline as normal records and
        // receive only this short authored-time window.
        private const double InstantGuidePathTravelMilliseconds = 48.0;
        // Decoration is visual-only. If a frame stalls while a dense batch of
        // ALD meshes is submitted, the authoritative DSP time can advance a
        // large distance between two renders. Do not let an existing line
        // teleport by that whole distance in one frame; let its visual clock
        // catch up over subsequent frames instead.
        private const double GuideVisualMaxAdvanceMilliseconds = 48.0;
        // Long-note surfaces are clipped at the judgement line. The note
        // object remains alive for KEEP judgement and is recycled only after
        // its complete authored tail leaves the normal visual range. The
        // hysteresis below is used only to decide whether a segment remains
        // alive; the generated intersection itself must stay on the exact
        // judgement plane so it lines up with the head mesh.
        private const float RibbonGameplayNearClipZ = ChartSpace.JudgementZ;
        // Keep note objects alive before they enter the camera's visible far
        // edge.  This is deliberately outside VisualFarZ so a frame hitch or
        // a high scroll profile cannot make a head pop into the screen.
        private const float NoteSpawnFarZ = ChartSpace.FarZ + ChartSpace.JudgementZ + 12f;
        // Keep every airborne surface above the complete ground-note pass.
        // Equal-queue renderers use Unity's camera-distance transparent sort.
        private const int GroundHeadQueue = 3070;
        private const int AirWallQueue = 3100;
        private const int AirBodyQueue = 3110;
        private const int AirTrailQueue = 3130;
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
        private readonly HashSet<int> airActionEntriesWithDirectionalAir = new HashSet<int>();
        private readonly List<int> remove = new List<int>();
        private readonly Dictionary<int, ChartNote> pairedGroundByAir = new Dictionary<int, ChartNote>();
        private readonly Dictionary<int, (int lane, int width)> pairedGroundPositionByAir =
            new Dictionary<int, (int lane, int width)>();
        private readonly Dictionary<int, int> pairedGroundProfileByAir = new Dictionary<int, int>();
        private readonly Dictionary<int, int> visualDuplicateOwnerByNote = new Dictionary<int, int>();
        private readonly Dictionary<int, LongChainGeometry> longChainGeometry = new Dictionary<int, LongChainGeometry>();
        private readonly Dictionary<int, int> longChainOwnerByNote = new Dictionary<int, int>();
        private readonly Dictionary<int, GuidePathVisual> activeGuidePaths = new Dictionary<int, GuidePathVisual>();
        private readonly Dictionary<int, GuidePathGeometry> guidePathGeometry = new Dictionary<int, GuidePathGeometry>();
        private readonly Dictionary<string, Material> guidePathMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Material> headMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Mesh> originalMeshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Mesh> mirroredAirMeshes = new Dictionary<string, Mesh>();
        private ParsedChart chart;
        private AirLadderVisuals airLadderVisuals;
        // Precomputed gameplay tracks mirror the original client's
        // NotesPreCalcManager boundary. The legacy fields and methods remain
        // available for editor tests while all new lifetime/ownership queries
        // are routed through this cache.
        private OriginalNotePrecalculation precomputed;
        private OriginalPlaybackMap playback;
        private RibbonGeometry[] geometry;
        private DspChartClock clock;
        private double lastRenderTime = double.NegativeInfinity;
        private double guideVisualTime = double.NegativeInfinity;
        private Material holdBody, slideBody, airHoldBody, airHoldRoof;
        private Material heavenRoof, heavenRoofPink, heavenWall, heavenShadow;
        private Material airActionTrailGreenBody;
        private Material holdLine, slideLine, airHoldLine, airActionTrailGreenLine, airLadderLine, defaultHead;
        private Material defaultAirHead, defaultAirArrow;
        private bool firstFrameHidden;
        private Transform notesRoot;
        private readonly NoteSkinUvCache noteSkinUvs = new NoteSkinUvCache();

        /// <summary>
        /// 绑定谱面和时钟并预计算每个音符的几何数据。Bind 会清理上一局视觉对象，
        /// 但不启动播放，也不修改判定状态。
        /// </summary>
        public void Bind(ParsedChart parsedChart, DspChartClock chartClock, bool uniformSpeed = false)
        {
            firstFrameHidden = false;
            ClearActive();
            judgedHeads.Clear();
            chart = parsedChart;
            precomputed = OriginalNotePrecalculation.Build(parsedChart);
            playback = new OriginalPlaybackMap(parsedChart, uniformSpeed);
            lastRenderTime = double.NegativeInfinity;
            guideVisualTime = double.NegativeInfinity;
            geometry = new RibbonGeometry[parsedChart.Notes.Count];
            pairedGroundByAir.Clear();
            pairedGroundPositionByAir.Clear();
            pairedGroundProfileByAir.Clear();
            visualDuplicateOwnerByNote.Clear();
            for (int i = 0; i < parsedChart.Notes.Count; i++) geometry[i] = BuildGeometry(parsedChart.Notes[i]);
            // Bind the clock before building independent visual tracks. This
            // keeps the decoration path safe if a future geometry prewarm
            // consults playback timing during construction.
            clock = chartClock;
            // ALD records are exported as adjacent pieces. Normalize only
            // their shared seam before allocating visual objects, so the
            // renderer cannot show a one-frame gap or a doubled start cap.
            WeldGuidePathStarts(parsedChart.GuidePaths ?? new List<VisualGuidePath>());
            BuildGuidePathVisuals(parsedChart.GuidePaths ?? new List<VisualGuidePath>());
            if (airLadderVisuals == null) airLadderVisuals = new AirLadderVisuals(notesRoot != null ? notesRoot : transform);
            airLadderVisuals.Bind(parsedChart.GuidePaths);
            BuildLongChainGeometry(parsedChart.Notes);
            BuildAirPairs(parsedChart.Notes);
            BuildVisualDuplicateOwners(parsedChart.Notes);
            BuildAirActionArrowOwnership(parsedChart.Notes);
        }

        /// <summary>释放当前谱面的活动对象和几何引用，供停止或重新游玩使用。</summary>
        public void Clear()
        {
            firstFrameHidden = false;
            ClearActive();
            ClearGuidePathVisuals();
            airLadderVisuals?.Clear();
            guidePathGeometry.Clear();
            judgedHeads.Clear();
            chart = null;
            precomputed = null;
            playback = null;
            geometry = null;
            longChainGeometry.Clear();
            longChainOwnerByNote.Clear();
            pairedGroundByAir.Clear();
            pairedGroundPositionByAir.Clear();
            pairedGroundProfileByAir.Clear();
            visualDuplicateOwnerByNote.Clear();
            clock = null;
            airActionEntriesWithDirectionalAir.Clear();
            lastRenderTime = double.NegativeInfinity;
            guideVisualTime = double.NegativeInfinity;
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
            holdBody = TextureMaterial("field/hold_joint", new Color(1f, 1f, 1f, .54f), 2980);
            slideBody = TextureMaterial("field/slide_joint", new Color(1f, 1f, 1f, .54f), 2990);
            // Unlit/Transparent ignores material tint/alpha. This layer needs
            // an actual translucent surface, independent of the bright centre line.
            airHoldBody = TintedAirMaterial(GameplaySkinManager.LoadFieldTexture("field/air_hold_joint"),
                new Color(1f, 1f, 1f, AirTrailOuterOpacity), AirBodyQueue);
            // The Windows renderer has a separate thin AIR-HOLD roof strip.
            // Keep it as an independent layer instead of tinting the body;
            // this preserves the original body translucency and roof edge.
            airHoldRoof = TextureMaterial("field/air_hold_roof", Color.white, AirTrailQueue);
            heavenRoof = TextureMaterial("field/heaven_roof", Color.white, AirBodyQueue);
            heavenRoofPink = TextureMaterial("field/heaven_roof_pink", Color.white, AirBodyQueue);
            heavenWall = TextureMaterial("field/heaven_wall", new Color(1f, 1f, 1f, .72f), AirWallQueue);
            heavenShadow = TextureMaterial("field/heaven_shadow", new Color(1f, 1f, 1f, .74f), 2975);
            airActionTrailGreenBody = TintedAirMaterial(null, new Color(.12f, 1f, .34f, AirTrailOuterOpacity), AirBodyQueue);
            holdLine = MeshFactory.Material(new Color(1f, .94f, .44f, .98f), true); holdLine.renderQueue = 3040;
            slideLine = MeshFactory.Material(new Color(.68f, .96f, 1f, .98f), true); slideLine.renderQueue = 3040;
            // The Windows client uses separate textured lines for AIR-HOLD
            // and AIR-LADDER paths. Keep them independent from the solid
            // green AIR-ACTION core so their authored strip pattern survives.
            airHoldLine = TextureMaterial("field/air_hold_line", Color.white, AirTrailQueue);
            airLadderLine = TextureMaterial("field/air_ladder_line", Color.white, AirTrailQueue);
            airActionTrailGreenLine = SolidAirTrailMaterial(null, new Color(.12f, 1f, .34f, 1f));

            BuildGuidePathMaterials();
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
                color = tint, renderQueue = AirTrailQueue, enableInstancing = true
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
            if (airHoldRoof != null) airHoldRoof.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_hold_roof");
            if (airHoldLine != null) airHoldLine.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_hold_line");
            if (airLadderLine != null) airLadderLine.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_ladder_line");
            foreach (Material guideMaterial in guidePathMaterials.Values)
                if (guideMaterial != null)
                    guideMaterial.mainTexture = GameplaySkinManager.LoadFieldTexture("field/air_ladder_line");
            if (heavenRoof != null) heavenRoof.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_roof");
            if (heavenRoofPink != null) heavenRoofPink.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_roof_pink");
            if (heavenWall != null) heavenWall.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_wall");
            if (heavenShadow != null) heavenShadow.mainTexture = GameplaySkinManager.LoadFieldTexture("field/heaven_shadow");

            GameplaySkinStyle skin = GameplaySkinManager.Style;
            if (holdLine != null) holdLine.color = skin.HoldLineColor;
            if (slideLine != null) slideLine.color = skin.SlideLineColor;
            Color trail = skin.AirLineColor;
            trail.a = AirTrailOuterOpacity;
            if (airActionTrailGreenBody != null) airActionTrailGreenBody.color = trail;
            trail.a = 1f;
            if (airActionTrailGreenLine != null) airActionTrailGreenLine.color = trail;
            airLadderVisuals?.RefreshSkin();
            PrepareNoteSkinUvs();
        }

        private void PrepareNoteSkinUvs()
        {
            foreach (string kind in new[] { "tap", "extap", "hold", "holdend", "slide", "slideend", "flick", "dmg" })
                for (int width = 1; width <= 16; width++) {
                    string suffix = "_" + width.ToString("00");
                    // EX TAP has its own arcade mesh. It is not a TAP mesh with
                    // a different texture; keep the UV cache attached to the
                    // authored EX geometry so the raised border is preserved.
                    string geometryKey = kind + suffix;
                    string textureKey = (kind == "holdend" ? "hold" : kind == "slideend" ? "slide" : kind) + suffix;
                    if (originalMeshes.TryGetValue(geometryKey, out Mesh mesh) &&
                        headMaterials.TryGetValue(textureKey, out Material material) &&
                        GameplaySkinManager.IsExternalNoteTexture(material.mainTexture))
                        noteSkinUvs.Get(mesh, (Texture2D)material.mainTexture);
                }
        }

        // 先按时间范围筛选音符，再更新对象池；不为整首谱面每帧创建 GameObject。
        private void LateUpdate()
        {
            if (chart == null || playback == null || clock == null || clock.Preparing) return;
            if (firstFrameHidden) {
                foreach (var visual in active.Values) if (visual.Valid) visual.Root.SetActive(true);
                firstFrameHidden = false;
                Debug.Log($"Gameplay first visible frame: chart={clock.TimeMs:F2}ms, warmedNotes={active.Count}");
            }
            double now = clock.TimeMs;
            // Keep rendering monotonic across tiny platform audio-clock
            // backsteps. This affects visuals only; judgement continues to
            // consume the authoritative DSP clock.
            if (!double.IsNegativeInfinity(lastRenderTime) &&
                now < lastRenderTime &&
                lastRenderTime - now <= RenderTimeBackstepToleranceMilliseconds)
                now = lastRenderTime;
            RenderFrame(now);
        }

        /// <summary>
        /// 在不推进 DSP 时钟的情况下预热首帧。加载流程应在此完成后再启动正式音频和谱面时间轴。
        /// </summary>
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
            UpdateGuidePathVisuals(AdvanceGuideVisualTime(now));
            airLadderVisuals?.Render(now, Distance);
        }

        private double AdvanceGuideVisualTime(double authoritativeNow)
        {
            if (double.IsNegativeInfinity(guideVisualTime)) {
                guideVisualTime = authoritativeNow;
                return guideVisualTime;
            }

            // A real seek or a replay restart must take effect immediately.
            // Tiny backwards samples are already filtered by LateUpdate, but
            // this guard also covers editor preview and explicit seeks.
            if (authoritativeNow + RenderTimeBackstepToleranceMilliseconds < guideVisualTime) {
                guideVisualTime = authoritativeNow;
                return guideVisualTime;
            }

            double delta = authoritativeNow - guideVisualTime;
            if (delta <= 0) return guideVisualTime;

            // Follow normal frame cadence without adding latency. Only cap an
            // abnormal jump, such as a one-frame mesh/GC stall. A minimum
            // step keeps the line responsive on devices that report a very
            // small or zero unscaled frame delta during startup.
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            double allowed = Math.Max(12.0, Math.Min(GuideVisualMaxAdvanceMilliseconds,
                frameMs * Math.Max(.5, clock != null ? clock.Rate : 1.0) * 2.5));
            guideVisualTime += Math.Min(delta, allowed);
            return guideVisualTime;
        }

        private void ProcessNote(int i, double now)
        {
            ChartNote note = chart.Notes[i];
            OriginalNoteTrack track = precomputed != null && i < precomputed.Tracks.Count
                ? precomputed[i] : null;
            bool longBody = track != null ? track.BodyVisibleUntilMs > note.TimeMs && track.BodyResource.Length > 0
                : note.EndTimeMs > note.TimeMs &&
                  note.Kind is (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or
                      NoteKind.AirHold or NoteKind.AirAction);
            double visualEndMs = note.EndTimeMs;
            if (note.LongChainId >= 0 && longChainGeometry.TryGetValue(note.LongChainId, out LongChainGeometry chain))
                visualEndMs = chain.EndTimeMs;
            int headProfile = note.ScrollProfileId;
            if (note.Kind == NoteKind.Air &&
                pairedGroundProfileByAir.TryGetValue(i, out int pairedProfile))
                headProfile = pairedProfile;
            if (IsVisualDuplicate(i, note)) return;
            float headZ = Distance(headProfile, note.TimeMs, now);
            double min, max;
            GetVisualDistanceBounds(i, now, headZ, out min, out max);
            bool unresolvedHead = IsUnresolvedHead(note, i, now);
            bool activeLongBody = longBody && active.ContainsKey(i);
            // Keep an already-rendered long-note object alive through the
            // judgement-plane crossing. Its mesh is cleared by UpdateRibbon
            // once every segment is behind the plane, but recycling the
            // GameObject on that exact frame made a small clock/profile
            // fluctuation restart the body and produced a visible sideways
            // flash at the tail. The short grace period is visual-only and
            // does not affect judgement or scoring.
            bool pastNearPlane = max < ChartSpace.NearZ - RibbonClipEpsilonZ;
            bool longBodyGrace = activeLongBody && now <= visualEndMs + 260.0;
            if ((pastNearPlane && !unresolvedHead && !longBodyGrace) ||
                min > NoteSpawnFarZ + MaximumVisibilityGuardZ ||
                (!longBody && now > Math.Max(note.TimeMs, visualEndMs) + 260)) return;
            float visibilityGuardZ = VisibilityGuardZ(headProfile, note.TimeMs, now, headZ);
            if (min > NoteSpawnFarZ + visibilityGuardZ) return;
            if (!active.TryGetValue(i, out NoteVisual visual)) { visual = Rent(); active[i] = visual; }
            keep.Add(i);
            updated.Add(i);
            UpdateVisual(visual, i, note, now, headZ, visibilityGuardZ);
        }

        private void BuildGuidePathVisuals(IReadOnlyList<VisualGuidePath> paths)
        {
            ClearGuidePathVisuals();
            airLadderVisuals?.Clear();
            guidePathGeometry.Clear();
            // Editor self-tests and a few lightweight preview hosts can bind
            // the renderer before Unity has dispatched Awake. Keep Bind
            // self-contained so a guide path can never make song loading
            // fail merely because its material cache is still empty.
            if (guidePathMaterials.Count == 0) BuildGuidePathMaterials();
            // ALD records are independent authored visual objects. Do not
            // rewrite one record from a neighboring record: a later line can
            // have a different colour, profile, or intended origin, and
            // welding them makes an already visible line shift when the next
            // one enters the viewport.
            for (int i = 0; i < paths.Count; i++) {
                GuidePathVisual visual = new GuidePathVisual(notesRoot != null ? notesRoot : transform);
                string colorKey = NormalizeGuidePathColor(paths[i].Color);
                if (!guidePathMaterials.TryGetValue(colorKey, out Material material))
                    material = guidePathMaterials["GRN"];
                visual.Renderer.sharedMaterial = material;
                visual.Root.SetActive(false);
                activeGuidePaths.Add(i, visual);
                guidePathGeometry.Add(i, BuildGuidePathGeometry(paths[i]));
            }
        }

        // ALD records are often exported as adjacent pieces of one visual
        // path. Keep a hard boundary from inventing a raised "head" when the
        // next record repeats the same instant with slightly different lane,
        // width, or height rounding. This is visual-only; ALD records remain
        // untouched in the parser and never enter judgement.
        private static void WeldGuidePathStarts(IReadOnlyList<VisualGuidePath> paths)
        {
            if (paths == null || paths.Count < 2) return;
            for (int i = 1; i < paths.Count; i++) {
                VisualGuidePath previous = paths[i - 1];
                VisualGuidePath current = paths[i];
                if (Math.Abs(previous.EndTimeMs - current.TimeMs) > .5) continue;
                if (!string.Equals(previous.Color, current.Color, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(previous.SecondaryColor, current.SecondaryColor, StringComparison.OrdinalIgnoreCase))
                    continue;

                float previousCenter = previous.EndLane + previous.EndWidth * .5f;
                float currentCenter = current.Lane + current.Width * .5f;
                if (Mathf.Abs(previousCenter - currentCenter) > .26f) continue;
                if (Mathf.Abs(previous.EndWidth - current.Width) > .26f) continue;
                if (Mathf.Abs(previous.EndAirHeight - current.AirHeight) > .26f) continue;

                current.Lane = previous.EndLane;
                current.Width = Mathf.Max(1, previous.EndWidth);
                current.AirHeight = previous.EndAirHeight;
                // A welded chain must use one spatial time base. Independent
                // field mappers have independent distance origins and create
                // a gap even when the authored timestamps are identical.
                current.ScrollProfileId = previous.ScrollProfileId;
            }
        }

        private void UpdateGuidePathVisuals(double now)
        {
            if (chart == null || chart.GuidePaths == null) return;
            foreach (var pair in activeGuidePaths) {
                VisualGuidePath path = chart.GuidePaths[pair.Key];
                UpdateGuidePathVisual(pair.Value, path, now, pair.Key);
            }
        }

        private void UpdateGuidePathVisual(GuidePathVisual visual, VisualGuidePath path, double now, int index)
        {
            // Spaced ALD records own plates, not a tiny green centre trace.
            if (AirLadderVisuals.HasPlates(path)) { visual.Root.SetActive(false); return; }
            if (!guidePathGeometry.TryGetValue(index, out GuidePathGeometry authored)) return;
            int samples = authored.Times.Length;
            bool visible = false;
            for (int i = 0; i < samples - 1; i++) {
                float z = Distance(authored.Profiles[i], authored.Times[i], now);
                float zNext = Distance(authored.Profiles[i + 1], authored.Times[i + 1], now);
                if ((z >= GuidePathNearCullZ && z <= GuidePathFarCullZ) ||
                    (zNext >= GuidePathNearCullZ && zNext <= GuidePathFarCullZ)) {
                    visible = true;
                    break;
                }
            }
            if (!visible) {
                visual.Root.SetActive(false);
                return;
            }
            visual.Root.SetActive(true);
            visual.Buffers.BeginFrame();
            visual.Buffers.EnsureCapacity(samples - 1);
            int vertices = 0, triangles = 0;
            int pathProfile = authored.Profiles.Length > 0 ? authored.Profiles[0] : 0;
            for (int i = 0; i < samples - 1; i++) {
                float p0 = authored.Progress[i], p1 = authored.Progress[i + 1];
                // Both adjacent quads must use the same profile at a shared
                // sample point. This makes the endpoint geometry identical
                // on both sides of an SLA boundary and prevents visible gaps.
                GuidePathPoint(path, authored.Times[i], p0, now, pathProfile,
                    out Vector3 centerA, out float uvA);
                GuidePathPoint(path, authored.Times[i + 1], p1, now, pathProfile,
                    out Vector3 centerB, out float uvB);
                // Keep the decoration as a single fine line, but make it
                // easier to see on dense charts and high-DPI screens.
                const float halfWidth = .024f;
                Vector3 leftA = centerA - Vector3.right * halfWidth;
                Vector3 rightA = centerA + Vector3.right * halfWidth;
                Vector3 leftB = centerB - Vector3.right * halfWidth;
                Vector3 rightB = centerB + Vector3.right * halfWidth;
                float minZ = Mathf.Min(leftA.z, leftB.z);
                float maxZ = Mathf.Max(leftA.z, leftB.z);
                if (maxZ < GuidePathNearCullZ || minZ > GuidePathFarCullZ) continue;
                visual.Buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(visual.Mesh, visual.Buffers, vertices, triangles);
        }

        private GuidePathGeometry BuildGuidePathGeometry(VisualGuidePath path)
        {
            double duration = path.EndTimeMs - path.TimeMs;
            bool instant = duration <= .001;
            if (instant) {
                // Treat a zero-duration record as a very short authored path.
                // This keeps its motion continuous through the judgement line
                // instead of switching to a separate fixed-length renderer.
                duration = InstantGuidePathTravelMilliseconds;
            }
            int sampleCount = instant ? 2 : 48;
            var times = new double[sampleCount];
            var progress = new float[sampleCount];
            // Keep one spatial time base for a complete ALD record. Switching
            // independent field mappers between samples creates a visible gap
            // or sudden segment, especially at the head of dense charts.
            var profiles = new int[sampleCount];
            for (int i = 0; i < sampleCount; i++) {
                progress[i] = i / (float)(sampleCount - 1);
                times[i] = instant
                    ? path.TimeMs - duration * .5 + duration * progress[i]
                    : path.TimeMs + duration * progress[i];
            }
            int profileId = path.ScrollProfileId;
            if (profileId < 0)
                profileId = playback.FindProfile(path.TimeMs, path.Lane, path.Width);
            for (int i = 0; i < profiles.Length; i++) profiles[i] = profileId;
            return new GuidePathGeometry(times, progress, profiles, false, 0);
        }

        private void GuidePathPoint(VisualGuidePath path, double authored, float p, double now,
            int profile, out Vector3 center, out float uv)
        {
            float z = Distance(profile, authored, now);
            // Project every authored sample independently. The previous
            // implementation computed the current start/end X positions and
            // then interpolated between them, which made the whole old line
            // move whenever either endpoint changed depth. That is a relative
            // shape, not the authored chart path. Each sample must keep its
            // own lane/width and only receive the perspective projection for
            // its own Z position.
            float lane = Mathf.Lerp(path.Lane, path.EndLane, p);
            float width = Mathf.Lerp(path.Width, path.EndWidth, p);
            // Guide lines are authored as a stable overlay over the lane
            // field. Do not project X at each sample's Z: that makes a line
            // on one fixed lane lean as it travels through the trapezoid and
            // makes its angle change whenever the next ALD appears. Keep the
            // lateral basis fixed while retaining each sample's independent
            // forward depth.
            float x = ChartSpace.NoteCenterX(lane, width, ChartSpace.JudgementZ);
            float y = ChartSpace.AirY(Mathf.Lerp(path.AirHeight, path.EndAirHeight, p));
            center = new Vector3(x, y, z);
            uv = p;
        }

        private void ClearGuidePathVisuals()
        {
            foreach (GuidePathVisual visual in activeGuidePaths.Values) visual.Dispose();
            activeGuidePaths.Clear();
            guidePathGeometry.Clear();
        }

        private void BuildGuidePathMaterials()
        {
            var colors = new Dictionary<string, Color> {
                { "RED", new Color(1f, .12f, .12f) }, { "ORN", new Color(1f, .42f, .06f) },
                { "YEL", new Color(1f, .90f, .08f) }, { "LIM", new Color(.55f, 1f, .08f) },
                { "GRN", new Color(.08f, 1f, .38f) }, { "DGR", new Color(.02f, .58f, .20f) },
                { "AQA", new Color(.06f, .95f, 1f) }, { "CYN", new Color(.02f, .88f, .95f) },
                { "BLU", new Color(.12f, .40f, 1f) }, { "VLT", new Color(.68f, .18f, 1f) },
                { "PPL", new Color(.78f, .26f, 1f) }, { "PNK", new Color(1f, .22f, .72f) },
                { "GRY", new Color(.68f, .72f, .78f) }, { "BLK", new Color(.15f, .16f, .20f) },
                { "NON", new Color(.34f, .95f, .76f) }
            };
            foreach (var pair in colors) {
                // ALD/AIR-LADDER uses the original strip texture as the
                // repeating base. Keep the authored colour as a tint so the
                // CYN/PPL/etc. chart colour still comes from the ALD record.
                Texture2D ladderTexture = GameplaySkinManager.LoadFieldTexture("field/air_ladder_line");
                Material line = ladderTexture != null
                    ? MeshFactory.Material(ladderTexture, pair.Value, true)
                    : MeshFactory.Material(pair.Value, false);
                line.renderQueue = AirTrailQueue;
                guidePathMaterials[pair.Key] = line;
            }
        }

        private static string NormalizeGuidePathColor(string color)
        {
            string key = (color ?? "").Trim().ToUpperInvariant();
            return key switch {
                "1" => "RED", "2" => "ORN", "3" => "YEL", "4" => "LIM", "5" or "DEF" => "GRN",
                "6" => "AQA", "7" => "BLU", "8" => "VLT", "9" => "PNK", "10" => "GRY",
                "11" => "BLK", "12" => "NON",
                "RED" or "ORN" or "YEL" or "LIM" or "GRN" or "DGR" or "AQA" or "BLU" or
                "CYN" or "VLT" or "PPL" or "PNK" or "GRY" or "BLK" or "NON" => key,
                _ => "GRN"
            };
        }

        private double RenderLookAheadMilliseconds()
        {
            double speed = Math.Max(.001, UnitsPerScrollMillisecond * Math.Max(.1f, VisualSpeed) *
                Math.Max(.001, playback.MinimumPositiveSpeed));
            double distance = NoteSpawnFarZ + MaximumVisibilityGuardZ + 2f;
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
            if (longChainOwnerByNote.TryGetValue(noteIndex, out int owner) && owner == noteIndex &&
                longChainGeometry.TryGetValue(chart.Notes[noteIndex].LongChainId, out LongChainGeometry chain))
                ribbon = chain.Ribbon;
            // Use the same per-authored-point profile as UpdateRibbon. The
            // segment midpoint profile is only suitable for interpolating a
            // quad; using it for lifetime decisions can disagree with the
            // actual clipped vertices at a scroll/profile boundary. Near the
            // judgement line that mismatch made a long note get recycled and
            // recreated on adjacent frames, which appeared as tail shaking.
            for (int i = 0; i < ribbon.Count; i++) {
                int profile = ribbon.PointProfileIds[i];
                double distance = Distance(profile, ribbon.AuthoredTimes[i], now);
                min = Math.Min(min, distance);
                max = Math.Max(max, distance);
            }
        }

        private void UpdateVisual(NoteVisual visual, int noteIndex, ChartNote note, double now,
            float headZ, float visibilityGuardZ)
        {
            // Unity's transparent sort falls back to creation order when
            // renderQueue and camera distance are equal. Dense chords and
            // long-note seams then reorder from frame to frame as pooled
            // objects are reused. Keep every visual layer deterministic by
            // using the authored note index as its secondary order key.
            int order = Mathf.Clamp(noteIndex, 0, 30000);
            visual.BodyRenderer.sortingOrder = order;
            visual.LineRenderer.sortingOrder = order;
            visual.RoofRenderer.sortingOrder = order;
            visual.AccentRenderer.sortingOrder = order;
            visual.TailRenderer.sortingOrder = order + 1;
            visual.HeadRenderer.sortingOrder = order + 2;
            OriginalNoteTrack track = precomputed != null && noteIndex < precomputed.Tracks.Count
                ? precomputed[noteIndex] : null;
            bool longVisual = (note.EndTimeMs > note.TimeMs && note.Kind is
                (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or
                 NoteKind.AirHold or NoteKind.AirAction));
            bool longBody = track != null
                ? track.IsBodyOwner && track.BodyResource.Length > 0
                : longVisual;
            visual.Accent.SetActive(false);
            visual.Roof.SetActive(false);
            visual.Body.SetActive(longBody); visual.Line.SetActive(longBody);
            if (longBody) UpdateLongVisual(visual, noteIndex, geometry[noteIndex], note, now);

            bool judged = judgedHeads.Contains(noteIndex);
            bool hasReachedJudgementLine = headZ <= ChartSpace.JudgementZ;
            // Signed scroll distance can cross the line before the authored
            // hit time. Such future heads must not be pinned or drawn below it.
            bool insideApproach = headZ >= (now < note.TimeMs ? ChartSpace.JudgementZ : ChartSpace.NearZ) &&
                                  headZ <= NoteSpawnFarZ + visibilityGuardZ;

            // The original renderer has two different head lifetimes. A
            // normal ground head is held on the gold line until its head
            // judgement resolves; this is why an untouched note does not
            // slide below the line before becoming MISS. Long-note heads and
            // discrete AIR heads finish at their arrival plane, while their
            // remaining body is rendered independently below.
            bool groundHead = note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Mine or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide;
            bool airHead = note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction;
            double headVisibleUntil = track != null
                ? track.HeadVisibleUntilMs
                : note.TimeMs + (airHead
                    ? JudgementEngine.MissWindowMs(note.Kind,
                        note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight,
                        note.AirFlick)
                    : JudgementEngine.MissWindowMs(note));
            // The authored window is expressed in chart milliseconds. The
            // clock's rate scales how quickly that window is traversed.
            double scaledHeadVisibleUntil = note.TimeMs +
                (headVisibleUntil - note.TimeMs) * Math.Max(.1, clock.Rate);
            bool airPending = airHead && now <= scaledHeadVisibleUntil;
            bool showHead = note.HasVisualHead && !judged &&
                            (airHead
                                ? (insideApproach || (hasReachedJudgementLine && now >= note.TimeMs)) && airPending
                                : groundHead
                                    ? (insideApproach || (hasReachedJudgementLine && now >= note.TimeMs)) &&
                                      now <= scaledHeadVisibleUntil
                                    : insideApproach);
            // A standalone directional AIR is always an authored visual
            // layer. Keep it alive through its own AIR window even if damaged
            // metadata reports zero points; a same-time TAP or long-note cap
            // must not make the arrow disappear at the judgement plane.
            // Headless long-note control records still remain approach-only.
            if ((!OwnsVisualHeadJudgement(note) || note.JudgePointCount <= 0) && note.Kind != NoteKind.Air)
                showHead = note.HasVisualHead && !judged && insideApproach && now < note.TimeMs;
            visual.Head.SetActive(showHead);
            if (visual.Head.activeSelf) {
                // Ground heads that are waiting for the late MISS boundary are
                // pinned to the same plane as the gold line, matching arcade
                // behavior instead of drifting into the touch area.
                // AIR heads travel with their authored height and depth. Only
                // ground heads stay on the judgement plane while waiting for
                // their late MISS boundary.
                float renderHeadZ = groundHead && hasReachedJudgementLine && now >= note.TimeMs
                    ? ChartSpace.JudgementZ : Mathf.Min(headZ, VisualFarZ);
                UpdateHead(visual.Head, visual.HeadFilter, visual.HeadMesh, visual.HeadRenderer,
                    noteIndex, note, renderHeadZ, false);
            }

            // A ground note that starts an ASC trail owns two simultaneous
            // visuals in the original presentation: the green AHTAP cap/body
            // and an upward AIR arrow rising from the ground plane. Keep the
            // arrow in Accent so neither visual replaces the other. If the C2S
            // already provides an explicit AIR/AUL/AUR at this plane, that
            // authored note remains the sole directional layer.
            bool showTrailEntryArrow = showHead && IsAirActionTrailEntry(note) &&
                !airActionEntriesWithDirectionalAir.Contains(noteIndex);
            visual.Accent.SetActive(showTrailEntryArrow);
            if (showTrailEntryArrow)
                UpdateDirectionalAirHead(visual.Accent, visual.AccentFilter, visual.AccentMesh,
                    visual.AccentRenderer, note.Lane, note.Width, Mathf.Min(headZ, VisualFarZ),
                    note.AirHeight, AirDirection.Up, connectToCap: true);

            // The assignment interval is end-exclusive. Querying exactly at
            // EndTimeMs can therefore select the next profile while the final
            // body interval still belongs to the previous one. Reuse that
            // final interval profile so the tail cannot detach from its body.
            bool showTail = false;
            float tailZ = 0f;
            // This cap is geometry, not a separate judgeable note. Its lifetime
            // ends at the authored tail; attached AIR/TAP records live separately.
            // A merged linear chain has one body owner (the first segment)
            // and a different tail owner (the final segment). Do not require
            // the tail segment to also own the body, otherwise the original
            // hold/slide cap disappears at the end of every welded chain.
            bool tailPending = track == null
                ? longVisual && note.IsLongChainTail && note.VisibleTail
                : longVisual && track.IsTailOwner;
            if (tailPending) {
                int endProfile = geometry[noteIndex].EndProfileId;
                tailZ = Distance(endProfile, note.EndTimeMs, now);
                float tailGuardZ = VisibilityGuardZ(endProfile, note.EndTimeMs, now, tailZ);
                // The tail is geometry, not a judgement checkpoint. Keep a
                // small visual hysteresis band at the judgement line. Without
                // it the cap toggles when the DSP sample lands on either side
                // of the plane, which looks like a left/right tail flash while
                // the body is being clipped. The cap is pinned to the exact
                // plane during this band so it cannot drift into the touch area.
                showTail = tailZ >= ChartSpace.JudgementZ - RibbonClipEpsilonZ &&
                    tailZ <= VisualFarZ + tailGuardZ;
            }
            visual.Tail.SetActive(showTail);
            if (visual.Tail.activeSelf) {
                bool tailIsAir = note.Kind is NoteKind.AirHold or NoteKind.AirAction;
                float renderTailZ = Mathf.Clamp(Mathf.Max(tailZ, ChartSpace.JudgementZ),
                    ChartSpace.JudgementZ, VisualFarZ);
                UpdateHead(visual.Tail, visual.TailFilter, visual.TailMesh,
                    visual.TailRenderer, noteIndex, note, renderTailZ, true);
            }
        }

        private void UpdateLongVisual(NoteVisual visual, int noteIndex, RibbonGeometry ribbon, ChartNote note, double now)
        {
            if (note.LongChainId >= 0 && longChainGeometry.TryGetValue(note.LongChainId, out LongChainGeometry chain)) {
                if (!longChainOwnerByNote.TryGetValue(noteIndex, out int owner) || owner != noteIndex) {
                    visual.Body.SetActive(false);
                    visual.Line.SetActive(false);
                    return;
                }
                ribbon = chain.Ribbon;
            }
            // Keep the complete authored curve. Its segments are clipped only
            // by the shared spatial viewport below, so the endpoint crossing
            // the judgement line retains the authored bend without adding a
            // renderer-side overshoot.
            float start = 0f;
            bool visible = true;
            if (note.Kind == NoteKind.HeavenHold) {
                UpdateHeavenHoldVisual(visual, ribbon, note, now, start, visible);
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
                    body = airActionTrailGreenBody;
                    line = airActionTrailGreenLine;
                    air = true;
                    break;
                case NoteKind.AirHold:
                    body = airHoldBody; line = airHoldLine; air = true;
                    // AIR-HOLD is a three-layer authored visual in the
                    // original client: translucent sheet, centre line and a
                    // narrow roof edge. Accent is otherwise unused by the
                    // body path and is safe to reuse for this decoration.
                    visual.Roof.SetActive(airHoldRoof != null);
                    if (visual.Roof.activeSelf) {
                        visual.RoofRenderer.sharedMaterial = airHoldRoof;
                        UpdateRibbon(visual.RoofMesh, visual.RoofBuffers, ribbon, note, now, start,
                            true, true, RibbonGameplayNearClipZ, .115f, .025f);
                    }
                    break;
                default: body = holdBody; line = holdLine; break;
            }
            visual.BodyRenderer.sharedMaterial = body; visual.LineRenderer.sharedMaterial = line;
            UpdateRibbon(visual.BodyMesh, visual.BodyBuffers, ribbon, note, now, start, air, false,
                RibbonGameplayNearClipZ);
            if (hasCentreLine)
                UpdateRibbon(visual.LineMesh, visual.LineBuffers, ribbon, note, now, start, air, true,
                    RibbonGameplayNearClipZ);
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
                roofY, .70f, RibbonGameplayNearClipZ);
            UpdateHeavenWalls(visual.LineMesh, visual.LineBuffers, ribbon, note, now, start,
                roofY, .98f, RibbonGameplayNearClipZ);
        }

        private void UpdateHeavenSurface(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start, float y, float widthScale, float nearClip)
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
                        nearClip, RibbonBodyFarClipZ))
                    continue;
                buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void UpdateHeavenWalls(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon,
            ChartNote note, double now, float start, float y, float widthScale, float nearClip)
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
                         nearClip, RibbonBodyFarClipZ))
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

        private void UpdateRibbon(Mesh mesh, RibbonBuffers buffers, RibbonGeometry ribbon, ChartNote note,
            double now, float start, bool air, bool line, float nearClip,
            float halfWidthOverride = float.NaN, float yOffset = 0f)
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
                int profileA = ribbon.PointProfileIds[i];
                int profileB = ribbon.PointProfileIds[i + 1];
                CalculateRibbonPoint(note, segmentStart, now, profileA, ribbon.Lanes[i], ribbon.Widths[i],
                    ribbon.AirHeights[i], ribbon.Uv[i], air, line,
                    out Vector3 leftA, out Vector3 rightA, out float uvA, halfWidthOverride, yOffset);
                CalculateRibbonPoint(note, segmentEnd, now, profileB, ribbon.Lanes[i + 1], ribbon.Widths[i + 1],
                    ribbon.AirHeights[i + 1], ribbon.Uv[i + 1], air, line,
                    out Vector3 leftB, out Vector3 rightB, out float uvB, halfWidthOverride, yOffset);
                if (!ClipRibbonSegment(ref leftA, ref rightA, ref uvA,
                        ref leftB, ref rightB, ref uvB,
                        nearClip, RibbonBodyFarClipZ))
                    continue;
                buffers.AppendRibbonQuad(ref vertices, ref triangles,
                    leftA, rightA, uvA, leftB, rightB, uvB);
            }
            CommitRibbon(mesh, buffers, vertices, triangles);
        }

        private void CalculateRibbonPoint(ChartNote note, double authored, double now, int profile,
            float lane, float width, float airHeight, float uvValue, bool air, bool line,
            out Vector3 left, out Vector3 right, out float uv,
            float halfWidthOverride = float.NaN, float yOffset = 0f)
        {
            float z = Distance(profile, authored, now);
            float x = ChartSpace.NoteCenterX(lane, width, z);
            float half = line
                ? FixedLineHalfWidth(air ? .115f : .032f)
                : ChartSpace.NoteHalfWidth(width, z) * .93f;
            if (!float.IsNaN(halfWidthOverride)) half = halfWidthOverride;
            if (air && !line) {
                // AIR-HOLD has a full-width translucent sheet and a separate
                // narrow centre line.
                half = note.Kind == NoteKind.AirHold ? half
                    : Mathf.Max(FixedLineHalfWidth(.115f), half * .34f);
            }
            float y = air ? ChartSpace.AirY(airHeight) : (line ? .085f : .055f);
            y += yOffset;
            left = new Vector3(x - half, y, z);
            right = new Vector3(x + half, y, z);
            uv = uvValue;
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
            float stableNear = near - RibbonClipEpsilonZ;
            float stableFar = far + RibbonClipEpsilonZ;
            if ((leftA.z < stableNear && leftB.z < stableNear) ||
                (leftA.z > stableFar && leftB.z > stableFar)) return false;
            // Once both ends have crossed the gameplay near plane, retain the
            // segment only inside the short hysteresis band and collapse that
            // last strip onto one shared Z plane. Leaving the two original Z
            // values in the mesh makes perspective recompute its X position
            // while the head is being hidden or the tail is being removed;
            // on a curved/ AIR ribbon this reads as a one-frame sideways flash.
            // The segment is still discarded as soon as both ends pass the
            // stable band above, so this does not extend the gameplay body.
            if (leftA.z < near && leftB.z < near) {
                leftA.z = rightA.z = near;
                leftB.z = rightB.z = near;
                return true;
            }
            // Keep the epsilon as a topology hysteresis band, but never use
            // it as the interpolation plane. Using stableNear here places the
            // body edge in front of the judgement line while the head remains
            // pinned on JudgementZ, which reads as a sideways jump under
            // perspective as the head is judged or hidden.
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
            OriginalNoteTrack track = precomputed != null && index < precomputed.Tracks.Count
                ? precomputed[index] : null;
            int lane = tail ? note.EndLane : note.Lane;
            int width = Mathf.Clamp(tail ? note.EndWidth : note.Width, 1, 16);
            float x = ChartSpace.NoteCenterX(lane, width, z), half = ChartSpace.NoteHalfWidth(width, z);
            if (note.Kind == NoteKind.Air) {
                if (!tail && pairedGroundPositionByAir.TryGetValue(index, out var pairPosition)) {
                    lane = pairPosition.lane;
                    width = Mathf.Clamp(pairPosition.width, 1, 16);
                    x = ChartSpace.NoteCenterX(lane, width, z);
                    half = ChartSpace.NoteHalfWidth(width, z);
                }
                UpdateDirectionalAirHead(target, filter, fallbackMesh, renderer, lane, width, z,
                    note.AirHeight, note.AirDirection, connectToCap: false);
                return;
            }
            if (note.Kind is NoteKind.AirHold or NoteKind.AirAction) {
                float height = tail ? note.EndAirHeight : note.AirHeight;
                bool action = note.Kind == NoteKind.AirAction;
                // An ASC that starts from a ground TAP/CHR/HOLD is the entry
                // of the green AIR trail. The original presentation uses the
                // green AHTAP connector there; AHACT is reserved for authored
                // action nodes inside the trail. This is a visual ownership
                // rule, not an AIR direction: ASC still has AirDirection.None.
                bool trailEntry = !tail && IsAirActionTrailEntry(note);
                string meshKind = action && !trailEntry
                    ? "ahact" : (tail ? "ahtapend" : "ahtap");
                bool placed = action && !trailEntry
                    ? SetOriginalAirActionMesh(target, filter, meshKind, width, x, z, height)
                    : SetOriginalAirHoldMesh(target, filter, meshKind, width, x, z, height, tail);
                if (!placed) {
                    filter.sharedMesh = fallbackMesh;
                    SetAirCapQuad(fallbackMesh, x, half * (action && !trailEntry ? .72f : .56f), z,
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
            string key = track != null && !string.IsNullOrEmpty(track.HeadResource)
                ? track.HeadResource
                : note.Kind switch {
                    NoteKind.ExTap => "extap",
                    NoteKind.Flick => "flick",
                    NoteKind.Hold when exHoldHead => "extap",
                    NoteKind.Hold or NoteKind.HeavenHold => tail ? "holdend" : "hold",
                    NoteKind.Slide => tail ? "slideend" : "slide",
                    NoteKind.Mine => "dmg",
                    _ => "tap"
                };
            if (tail && track != null && !string.IsNullOrEmpty(track.TailResource)) key = track.TailResource;
            if (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Hold or
                 NoteKind.HeavenHold or NoteKind.Slide or NoteKind.Mine) {
                // These meshes are the extracted per-width original planes.
                // Their authored aspect ratio includes the original depth;
                // scaling the mesh from its X bounds preserves that ratio and
                // avoids the old one-size quad that made heads look too short.
                // CHR and HXD use the Windows client's independent EXTAP
                // family. Keep that geometry separate from TAP: the two
                // resources have different silhouettes and depth profiles.
                string geometryKey = key;
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

        private static bool IsAirActionTrailEntry(ChartNote note) =>
            note != null && note.Kind == NoteKind.AirAction &&
            note.OriginalType == "ASC" && note.HasVisualHead && !note.IsContinuation;

        private void UpdateDirectionalAirHead(GameObject target, MeshFilter filter, Mesh fallbackMesh,
            MeshRenderer renderer, int lane, int width, float z, float airHeight,
            AirDirection direction, bool connectToCap)
        {
            width = Mathf.Clamp(width, 1, 16);
            float x = ChartSpace.NoteCenterX(lane, width, z);
            float half = ChartSpace.NoteHalfWidth(width, z);
            bool down = direction is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            bool tilted = direction is AirDirection.UpLeft or AirDirection.UpRight or AirDirection.DownLeft or AirDirection.DownRight;
            // The extracted tilted mesh points right in its original model
            // space. AUR/ADR therefore use it directly; AUL/ADL use the
            // mirrored copy. The synthesized ASC entry uses the straight UP
            // mesh and extends it to the AHTAP cap to form one continuous shape.
            bool mirror = ShouldMirrorAirMesh(direction);
            string meshKind = down ? (tilted ? "airdwtilt" : "airdw") : (tilted ? "airuptilt" : "airup");
            if (!SetOriginalAirMesh(target, filter, meshKind, width, x, z, airHeight, mirror,
                    connectToCap ? 1f : .72f)) {
                filter.sharedMesh = fallbackMesh;
                SetAirTriangle(fallbackMesh, x, half, z, airHeight, direction);
                target.transform.localPosition = Vector3.zero;
                target.transform.localRotation = Quaternion.identity;
                target.transform.localScale = Vector3.one;
            }
            renderer.sharedMaterial = HeadMaterial(down ? "air_down" : "air_up", width);
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

        private bool SetOriginalAirMesh(GameObject target, MeshFilter filter, string kind, int width, float x, float z,
            float airHeight, bool mirror, float heightScale)
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
            float targetTopY = airBaseY + (authoredTopY - airBaseY) * Mathf.Clamp01(heightScale);
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

        private static bool ShouldMirrorAirMesh(AirDirection direction) =>
            direction is AirDirection.UpLeft or AirDirection.DownLeft;

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
            // AIR can be attached to a ground head at the same timestamp or
            // to the tail of a HOLD/SLIDE whose EndTime is the AIR timestamp.
            // Resolve all candidates first, then assign them globally so a
            // dense chord cannot let the first arrow steal the only compatible
            // long-note tail from a later arrow.
            var candidates = new List<(int air, ChartNote ground, int source, int endpoint,
                int overlap, double timeDelta, float centerDistance)>();
            for (int i = 0; i < notes.Count; i++) {
                ChartNote air = notes[i];
                if (air.Kind != NoteKind.Air) continue;
                float airCenter = air.Lane + Math.Max(1, air.Width) * .5f;
                for (int g = 0; g < notes.Count; g++) {
                    ChartNote ground = notes[g];
                    if (ground.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction) continue;
                    double headDelta = Math.Abs(ground.TimeMs - air.TimeMs);
                    double tailDelta = Math.Abs(ground.EndTimeMs - air.TimeMs);
                    bool atHead = headDelta <= .1;
                    bool atTail = ground.EndTimeMs > ground.TimeMs && tailDelta <= .1 &&
                        ground.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide;
                    if (!atHead && !atTail) continue;
                    int lane = atTail ? ground.EndLane : ground.Lane;
                    int width = Math.Max(1, atTail ? ground.EndWidth : ground.Width);
                    int overlap = Math.Max(0, Math.Min(lane + width, air.Lane + Math.Max(1, air.Width)) -
                        Math.Max(lane, air.Lane));
                    if (overlap <= 0) continue;
                    bool sourceMatch = !string.IsNullOrWhiteSpace(air.SourceType) &&
                        string.Equals(air.SourceType, ground.OriginalType, StringComparison.OrdinalIgnoreCase);
                    float centerDistance = Mathf.Abs(lane + width * .5f - airCenter);
                    candidates.Add((i, ground, sourceMatch ? 1 : 0, atHead ? 1 : 0,
                        overlap, Math.Min(headDelta, tailDelta), centerDistance));
                }
            }
            var reserved = new HashSet<ChartNote>();
            foreach (var candidate in candidates
                .OrderByDescending(c => c.source).ThenByDescending(c => c.endpoint)
                .ThenByDescending(c => c.overlap).ThenBy(c => c.timeDelta)
                .ThenBy(c => c.centerDistance)) {
                if (pairedGroundByAir.ContainsKey(candidate.air) || reserved.Contains(candidate.ground)) continue;
                pairedGroundByAir[candidate.air] = candidate.ground;
                bool useTail = candidate.endpoint == 0 && candidate.ground.EndTimeMs > candidate.ground.TimeMs &&
                    Math.Abs(candidate.ground.EndTimeMs - notes[candidate.air].TimeMs) <= .1;
                pairedGroundPositionByAir[candidate.air] = useTail
                    ? (candidate.ground.EndLane, Math.Max(1, candidate.ground.EndWidth))
                    : (candidate.ground.Lane, Math.Max(1, candidate.ground.Width));
                var position = pairedGroundPositionByAir[candidate.air];
                pairedGroundProfileByAir[candidate.air] = playback.FindProfile(
                    notes[candidate.air].TimeMs, position.lane, position.width);
                reserved.Add(candidate.ground);
            }
            // Malformed charts can contain more AIR arrows than compatible
            // ground records. Preserve the old renderer's useful fallback in
            // that case, but only after every arrow with a unique candidate
            // has received its stable owner.
            foreach (int airIndex in Enumerable.Range(0, notes.Count)
                .Where(i => notes[i].Kind == NoteKind.Air && !pairedGroundByAir.ContainsKey(i))) {
                var fallback = candidates.Where(c => c.air == airIndex)
                    .OrderByDescending(c => c.source).ThenByDescending(c => c.endpoint)
                    .ThenByDescending(c => c.overlap).ThenBy(c => c.timeDelta)
                    .ThenBy(c => c.centerDistance).FirstOrDefault();
                if (fallback.ground == null) continue;
                pairedGroundByAir[airIndex] = fallback.ground;
                bool useTail = fallback.endpoint == 0 && fallback.ground.EndTimeMs > fallback.ground.TimeMs &&
                    Math.Abs(fallback.ground.EndTimeMs - notes[airIndex].TimeMs) <= .1;
                pairedGroundPositionByAir[airIndex] = useTail
                    ? (fallback.ground.EndLane, Math.Max(1, fallback.ground.EndWidth))
                    : (fallback.ground.Lane, Math.Max(1, fallback.ground.Width));
                var position = pairedGroundPositionByAir[airIndex];
                pairedGroundProfileByAir[airIndex] = playback.FindProfile(
                    notes[airIndex].TimeMs, position.lane, position.width);
            }
        }

        private void BuildVisualDuplicateOwners(IReadOnlyList<ChartNote> notes)
        {
            visualDuplicateOwnerByNote.Clear();
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Count; i++) {
                ChartNote note = notes[i];
                if (!IsVisualControlRecord(note)) continue;
                string key = string.Join("|", note.OriginalType ?? "", note.TimeMs.ToString("R"),
                    note.EndTimeMs.ToString("R"), note.Lane, note.Width, note.EndLane, note.EndWidth,
                    note.AirHeight.ToString("R"), note.EndAirHeight.ToString("R"));
                if (owners.TryGetValue(key, out int owner)) visualDuplicateOwnerByNote[i] = owner;
                else owners[key] = i;
            }
        }

        private static bool IsVisualControlRecord(ChartNote note)
        {
            if (note == null) return false;
            return note.OriginalType is "SLC" or "SXC" or "SLD" or "SXD" or
                "ASC" or "ASD" or "AHD" or "AHX";
        }

        private bool IsVisualDuplicate(int index, ChartNote note) =>
            IsVisualControlRecord(note) && visualDuplicateOwnerByNote.TryGetValue(index, out int owner) && owner != index;

        private void BuildAirActionArrowOwnership(IReadOnlyList<ChartNote> notes)
        {
            airActionEntriesWithDirectionalAir.Clear();
            for (int actionIndex = 0; actionIndex < notes.Count; actionIndex++) {
                ChartNote action = notes[actionIndex];
                if (!IsAirActionTrailEntry(action)) continue;
                int actionEnd = action.Lane + Math.Max(1, action.Width);
                for (int airIndex = 0; airIndex < notes.Count; airIndex++) {
                    ChartNote air = notes[airIndex];
                    if (air.Kind != NoteKind.Air || Math.Abs(air.TimeMs - action.TimeMs) > .1) continue;
                    int airEnd = air.Lane + Math.Max(1, air.Width);
                    if (action.Lane < airEnd && air.Lane < actionEnd) {
                        airActionEntriesWithDirectionalAir.Add(actionIndex);
                        break;
                    }
                }
            }
        }

        private static long AirPairTimeKey(double timeMs) =>
            (long)Math.Round(timeMs * 10.0, MidpointRounding.AwayFromZero);

        /// <summary>标记一个判定点已消费，使头、段、尾视觉可以独立决定回收时机。</summary>
        public void MarkJudged(int noteIndex, int pointIndex)
        {
            if (chart == null || noteIndex < 0 || noteIndex >= chart.Notes.Count) return;
            ChartNote note = chart.Notes[noteIndex];
            if (note.HasVisualHead && OwnsVisualHeadJudgement(note) && pointIndex == 0)
                judgedHeads.Add(noteIndex);
        }

        private bool IsUnresolvedHead(ChartNote note, int noteIndex, double now)
        {
            if (note == null || !note.HasVisualHead || now < note.TimeMs || judgedHeads.Contains(noteIndex)) return false;
            if (!OwnsVisualHeadJudgement(note) || note.JudgePointCount <= 0) return false;
            if (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or NoteKind.Mine or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide)
                return now <= note.TimeMs + JudgementEngine.MissWindowMs(note) * clock.Rate;
            if (note.Kind is not (NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction)) return false;
            bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            return now <= note.TimeMs + JudgementEngine.MissWindowMs(note.Kind, down, note.AirFlick) * clock.Rate;
        }

        private static bool OwnsVisualHeadJudgement(ChartNote note) =>
            note != null && (note.VisibleHead ||
                note.Kind == NoteKind.AirHold && note.HasVisualHead && !note.IsContinuation);

        private RibbonGeometry BuildGeometry(ChartNote note)
        {
            // Keep uniform authored samples for ordinary curves, but always add
            // SFL/SLP/STP/SLA boundaries. A long object can cross a local speed
            // boundary while its endpoints remain outside the screen; without
            // these knots the renderer draws a straight shortcut or loses the
            // visible middle section.
            int minimumSamples = 12;
            if (note.EndTimeMs <= note.TimeMs) {
                var point = new RibbonGeometry(2);
                point.AuthoredTimes[0] = point.AuthoredTimes[1] = note.TimeMs;
                point.Lanes[0] = point.Lanes[1] = note.Lane;
                point.Widths[0] = point.Widths[1] = note.Width;
                point.AirHeights[0] = point.AirHeights[1] = note.AirHeight;
                point.Uv[0] = point.Uv[1] = 0f;
                point.PointProfileIds[0] = point.PointProfileIds[1] = playback.FindProfile(
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
                float p = Mathf.Clamp01((float)((unique[i] - note.TimeMs) /
                    (note.EndTimeMs - note.TimeMs)));
                result.Lanes[i] = Mathf.Lerp(note.Lane, note.EndLane, p);
                result.Widths[i] = Mathf.Lerp(note.Width, note.EndWidth, p);
                result.AirHeights[i] = Mathf.Lerp(note.AirHeight, note.EndAirHeight, p);
                result.Uv[i] = p;
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
            for (int i = 0; i < result.Count; i++) {
                int lane = Mathf.RoundToInt(result.Lanes[i]);
                int width = Mathf.Max(1, Mathf.RoundToInt(result.Widths[i]));
                result.PointProfileIds[i] = playback.FindProfile(result.AuthoredTimes[i], lane, width);
            }
            return result;
        }

        // Only weld a proven one-to-one continuation. Parser chain IDs can
        // contain forks, so the renderer keeps ambiguous branches separate.
        private void BuildLongChainGeometry(IReadOnlyList<ChartNote> notes)
        {
            longChainGeometry.Clear();
            longChainOwnerByNote.Clear();
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < notes.Count; i++) {
                int chainId = notes[i].LongChainId;
                if (chainId < 0) continue;
                if (!groups.TryGetValue(chainId, out List<int> members))
                    groups[chainId] = members = new List<int>();
                members.Add(i);
            }

            foreach (var pair in groups) {
                List<int> members = pair.Value;
                members.Sort((a, b) => notes[a].TimeMs.CompareTo(notes[b].TimeMs));
                foreach (int index in members) longChainOwnerByNote[index] = index;
                if (members.Count < 2 || !OriginalNotePrecalculation.IsLinearLongChain(notes, members)) continue;
                foreach (int index in members) longChainOwnerByNote[index] = members[0];

                var times = new List<double>();
                foreach (int index in members)
                    foreach (double time in geometry[index].AuthoredTimes)
                        if (time >= notes[members[0]].TimeMs - .001 &&
                            time <= notes[members[members.Count - 1]].EndTimeMs + .001)
                            times.Add(time);
                times.Sort();
                var unique = new List<double>(times.Count);
                foreach (double time in times)
                    if (unique.Count == 0 || Math.Abs(time - unique[unique.Count - 1]) > .001)
                        unique.Add(time);
                if (unique.Count < 2) continue;

                var ribbon = new RibbonGeometry(unique.Count);
                double chainStart = notes[members[0]].TimeMs;
                double chainEnd = notes[members[members.Count - 1]].EndTimeMs;
                for (int i = 0; i < unique.Count; i++) {
                    double authored = unique[i];
                    ribbon.AuthoredTimes[i] = authored;
                    ribbon.Uv[i] = chainEnd > chainStart
                        ? Mathf.Clamp01((float)((authored - chainStart) / (chainEnd - chainStart))) : 0f;
                    int segment = FindChainSegment(notes, members, authored);
                    ChartNote current = notes[members[segment]];
                    double duration = Math.Max(.001, current.EndTimeMs - current.TimeMs);
                    float p = Mathf.Clamp01((float)((authored - current.TimeMs) / duration));
                    GetSmoothChainPoint(notes, members, segment, p,
                        out float center, out float width, out float airHeight);
                    ribbon.Widths[i] = Mathf.Max(.25f, width);
                    ribbon.Lanes[i] = center - ribbon.Widths[i] * .5f;
                    ribbon.AirHeights[i] = airHeight;
                }

                for (int i = 0; i < ribbon.SegmentCount; i++) {
                    double midpoint = (ribbon.AuthoredTimes[i] + ribbon.AuthoredTimes[i + 1]) * .5;
                    int segment = FindChainSegment(notes, members, midpoint);
                    int noteIndex = members[segment];
                    RibbonGeometry source = geometry[noteIndex];
                    double local = midpoint;
                    int sourceSegment = 0;
                    while (sourceSegment + 1 < source.SegmentCount &&
                           source.AuthoredTimes[sourceSegment + 1] < local) sourceSegment++;
                    ribbon.SegmentProfileIds[i] = source.SegmentProfileIds[Math.Min(sourceSegment,
                        source.SegmentProfileIds.Length - 1)];
                }
                for (int i = 0; i < ribbon.Count; i++) {
                    // FindProfile expects the left lane of the authored
                    // interval. Passing the centre lane here shifted the
                    // query and could select an unrelated SLA/SLP region on
                    // merged curves.
                    ribbon.PointProfileIds[i] = playback.FindProfile(ribbon.AuthoredTimes[i],
                        Mathf.RoundToInt(ribbon.Lanes[i]),
                        Mathf.Max(1, Mathf.RoundToInt(ribbon.Widths[i])));
                }
                longChainGeometry[pair.Key] = new LongChainGeometry(ribbon, chainStart, chainEnd);
            }
        }

        private static int FindChainSegment(IReadOnlyList<ChartNote> notes, IReadOnlyList<int> members,
            double timeMs)
        {
            for (int i = members.Count - 1; i > 0; i--)
                if (timeMs >= notes[members[i]].TimeMs - .001) return i;
            return 0;
        }

        private static void GetSmoothChainPoint(IReadOnlyList<ChartNote> notes, IReadOnlyList<int> members,
            int segment, float t, out float center, out float width, out float airHeight)
        {
            ChartNote current = notes[members[segment]];
            float c1 = current.Lane + current.Width * .5f;
            float c2 = current.EndLane + current.EndWidth * .5f;
            float w1 = current.Width, w2 = current.EndWidth;
            // Long-note records already contain the authored endpoints. A
            // Catmull-Rom pass here can overshoot when adjacent segments turn
            // sharply, producing the snake-like "tail swing" seen on dense
            // charts. Use bounded interpolation so the renderer never invents
            // a lane or width outside this segment's authored range.
            center = Mathf.Lerp(c1, c2, t);
            width = Mathf.Lerp(w1, w2, t);
            width = Mathf.Clamp(width, .25f, 16f);
            center = Mathf.Clamp(center, width * .5f, 16f - width * .5f);
            airHeight = Mathf.Lerp(current.AirHeight, current.EndAirHeight, t);
        }

        private void OnDestroy()
        {
            airLadderVisuals?.Dispose();
            foreach (var visual in active.Values) visual.Dispose();
            foreach (var visual in pool) visual.Dispose();
            foreach (var visual in activeGuidePaths.Values) visual.Dispose();
            active.Clear();
            pool.Clear();
            activeGuidePaths.Clear();
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
            float shift = direction is AirDirection.UpLeft or AirDirection.DownLeft ? -half*.50f : direction is AirDirection.UpRight or AirDirection.DownRight ? half*.50f : 0f;
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
            public bool Valid => Root != null && Head != null && Body != null && Line != null && Tail != null && Accent != null && Roof != null;
            public void Dispose()
            {
                ReleaseOwnedObject(HeadMesh); ReleaseOwnedObject(BodyMesh); ReleaseOwnedObject(LineMesh);
                ReleaseOwnedObject(TailMesh); ReleaseOwnedObject(AccentMesh); ReleaseOwnedObject(RoofMesh); ReleaseOwnedObject(Root);
            }
            public readonly GameObject Root,Head,Body,Line,Tail,Accent,Roof; public readonly Mesh HeadMesh,BodyMesh,LineMesh,TailMesh,AccentMesh,RoofMesh; public readonly MeshFilter HeadFilter,BodyFilter,LineFilter,TailFilter,AccentFilter,RoofFilter; public readonly MeshRenderer HeadRenderer,BodyRenderer,LineRenderer,TailRenderer,AccentRenderer,RoofRenderer;
            public readonly RibbonBuffers BodyBuffers = new RibbonBuffers();
            public readonly RibbonBuffers LineBuffers = new RibbonBuffers();
            public readonly RibbonBuffers AccentBuffers = new RibbonBuffers();
            public readonly RibbonBuffers RoofBuffers = new RibbonBuffers();
            public NoteVisual(Transform parent) { Root=new GameObject("Pooled original note"); Root.transform.SetParent(parent,false); Create(Root.transform,"Accent",out Accent,out AccentMesh,out AccentFilter,out AccentRenderer); Create(Root.transform,"AIR-HOLD roof",out Roof,out RoofMesh,out RoofFilter,out RoofRenderer); Create(Root.transform,"Body",out Body,out BodyMesh,out BodyFilter,out BodyRenderer); Create(Root.transform,"Centre line",out Line,out LineMesh,out LineFilter,out LineRenderer); Create(Root.transform,"Tail",out Tail,out TailMesh,out TailFilter,out TailRenderer); Create(Root.transform,"Head",out Head,out HeadMesh,out HeadFilter,out HeadRenderer); }
            private static void Create(Transform p,string n,out GameObject go,out Mesh mesh,out MeshFilter filter,out MeshRenderer renderer) { go=new GameObject(n); go.transform.SetParent(p,false); mesh=new Mesh{name="Dynamic "+n}; mesh.MarkDynamic(); filter=go.AddComponent<MeshFilter>(); filter.sharedMesh=mesh; renderer=go.AddComponent<MeshRenderer>(); renderer.sortingOrder=0; }
        }

        private sealed class GuidePathVisual
        {
            public readonly GameObject Root;
            public readonly Mesh Mesh;
            public readonly MeshFilter Filter;
            public readonly MeshRenderer Renderer;
            public readonly RibbonBuffers Buffers = new RibbonBuffers();
            public GuidePathVisual(Transform parent)
            {
                Root = new GameObject("Chart visual guide path");
                Root.transform.SetParent(parent, false);
                Create(Root.transform, "Guide path line", out Mesh, out Filter, out Renderer);
            }

            public void Dispose()
            {
                ReleaseOwnedObject(Mesh);
                ReleaseOwnedObject(Root);
            }

            private static void Create(Transform parent, string name, out Mesh mesh,
                out MeshFilter filter, out MeshRenderer renderer)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                mesh = new Mesh { name = "Dynamic " + name };
                mesh.MarkDynamic();
                filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                renderer = go.AddComponent<MeshRenderer>();
                renderer.sortingOrder = 0;
            }
        }

        private sealed class GuidePathGeometry
        {
            public readonly double[] Times;
            public readonly float[] Progress;
            public readonly int[] Profiles;
            public readonly bool Instant;
            public readonly int ProfileId;

            public GuidePathGeometry(double[] times, float[] progress, int[] profiles,
                bool instant, int profileId)
            {
                Times = times;
                Progress = progress;
                Profiles = profiles;
                Instant = instant;
                ProfileId = profileId;
            }

            public static GuidePathGeometry CreateInstant(int profileId) =>
                new GuidePathGeometry(Array.Empty<double>(), Array.Empty<float>(),
                    Array.Empty<int>(), true, profileId);
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
            public readonly int[] PointProfileIds;
            public readonly float[] Lanes;
            public readonly float[] Widths;
            public readonly float[] AirHeights;
            public readonly float[] Uv;
            public int Count => AuthoredTimes.Length;
            public int SegmentCount => Math.Max(0, AuthoredTimes.Length - 1);
            public int EndProfileId => SegmentProfileIds.Length > 0
                ? (PointProfileIds.Length > 0 ? PointProfileIds[PointProfileIds.Length - 1] : SegmentProfileIds[SegmentProfileIds.Length - 1]) : 0;

            public RibbonGeometry(int count)
            {
                AuthoredTimes = new double[count];
                SegmentProfileIds = new int[Math.Max(1, count - 1)];
                PointProfileIds = new int[Math.Max(1, count)];
                Lanes = new float[count];
                Widths = new float[count];
                AirHeights = new float[count];
                Uv = new float[count];
            }
        }

        private sealed class LongChainGeometry
        {
            public readonly RibbonGeometry Ribbon;
            public readonly double StartTimeMs;
            public readonly double EndTimeMs;

            public LongChainGeometry(RibbonGeometry ribbon, double startTimeMs, double endTimeMs)
            {
                Ribbon = ribbon;
                StartTimeMs = startTimeMs;
                EndTimeMs = endTimeMs;
            }
        }
    }
}
