using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // ALTER EGO combines explicit directional AIR records with ASC/ASD
    // AIR-ACTION geometry. This fixture keeps real arrows independent from
    // dense same-plane records without inventing arrows from curve opcodes.
    public static class AlterEgoAirVisualSelfTest
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Run()
        {
            string path = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH") ??
                Path.GetFullPath("../提取的歌曲/2978/chart/2978_03.c2s");
            if (!File.Exists(path)) path = Path.GetFullPath("Build/TestFixtures/songs/2978/chart/2978_03.c2s");
            ParsedChart chart = C2sParser.Parse(path);
            List<ScheduledJudgePoint> points = JudgePointScheduler.Build(chart)
                .Where(point => point.IsScoring)
                .OrderBy(point => point.TimeMs)
                .ThenBy(point => point.NoteIndex)
                .ThenBy(point => point.PointIndex)
                .ToList();
            Require(points.Count >= 532, "ALTER EGO fixture has fewer than 532 scoring points");

            // This regression targets ASC at measure 27 tick 0, lane 13/3.
            // Combo indices move when missing AIR checkpoints are restored;
            // locate the authored fixture rather than changing the target.
            const double focus = 37121.58317059117;
            var nearbyAir = chart.Notes.Select((note, index) => (note, index))
                .Where(item => item.note.Kind == NoteKind.Air &&
                    Math.Abs(item.note.TimeMs - focus) <= 1800)
                .ToList();
            Require(nearbyAir.Count > 0, "ALTER EGO combo 532 neighborhood has no AIR fixture");
            Require(nearbyAir.All(item => item.note.HasVisualHead),
                "standalone ALTER EGO AIR lost its visual head during parsing");
            Require(nearbyAir.All(item => item.note.AirDirection == DirectionForOpcode(item.note.OriginalType)),
                "ALTER EGO explicit AIR opcode was assigned the wrong visual direction");

            var samePlaneGround = new Dictionary<int, ChartNote>();
            foreach (var item in nearbyAir) {
                ChartNote ground = chart.Notes.FirstOrDefault(candidate =>
                    candidate.Kind is not (NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction) &&
                    Math.Abs(candidate.TimeMs - item.note.TimeMs) <= .1 &&
                    candidate.Lane < item.note.Lane + Math.Max(1, item.note.Width) &&
                    item.note.Lane < candidate.Lane + Math.Max(1, candidate.Width));
                if (ground != null) samePlaneGround[item.index] = ground;
                Debug.Log($"ALTER EGO AIR near combo532: index={item.index}, time={item.note.TimeMs:F3}, " +
                    $"type={item.note.OriginalType}/{item.note.SourceType}, lane={item.note.Lane}/{item.note.Width}, " +
                    $"direction={item.note.AirDirection}, visualHead={item.note.HasVisualHead}, " +
                    $"judgeHead={item.note.VisibleHead}, pairedGround={(ground == null ? "none" : ground.OriginalType)}");
            }
            Require(samePlaneGround.Count > 0,
                "ALTER EGO combo 532 neighborhood has no same-plane ground/AIR composition");
            int focusActionIndex = chart.Notes.FindIndex(note =>
                note.Kind == NoteKind.AirAction && note.OriginalType == "ASC" &&
                Math.Abs(note.TimeMs - focus) <= .1 && !note.IsContinuation);
            Require(focusActionIndex >= 0,
                "ALTER EGO combo 532 fixture has no starting ASC/TAP composition");
            Require(chart.Notes.Where(note => note.OriginalType is "ASC" or "ASD")
                    .All(note => note.AirDirection == AirDirection.None),
                "ASC/ASD curve geometry was incorrectly converted into a directional AIR arrow");

            var root = new GameObject("ALTER EGO AIR visual regression");
            var cameraRoot = new GameObject("ALTER EGO AIR camera");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                renderer.Bind(chart, new DspChartClock());
                MethodInfo process = typeof(ChartPlaybackRenderer).GetMethod("ProcessNote", Private);
                MethodInfo renderFrame = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private);
                MethodInfo isTrailEntry = typeof(ChartPlaybackRenderer).GetMethod(
                    "IsAirActionTrailEntry", BindingFlags.NonPublic | BindingFlags.Static);
                var active = (IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("active", Private).GetValue(renderer);

                ValidateContinuousLongChain(chart, renderer);

                foreach (var item in nearbyAir.Where(item => samePlaneGround.ContainsKey(item.index))) {
                    double now = item.note.TimeMs - 40;
                    process.Invoke(renderer, new object[] { item.index, now });
                    Require(active.Contains(item.index),
                        $"ALTER EGO AIR {item.index} was culled before reaching the judgement plane");
                    object visual = active[item.index];
                    var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
                    Require(head.activeSelf,
                        $"ALTER EGO AIR {item.index} exists but its directional arrow is hidden");
                    var mesh = head.GetComponent<MeshFilter>().sharedMesh;
                    Require(mesh != null && mesh.vertexCount > 0,
                        $"ALTER EGO AIR {item.index} has no visible arrow mesh");
                }


                // The measure-27 ASC/TAP AIR-trail entry. The original presentation combines three
                // layers at one plane: an upward AIR arrow from the ground,
                // the green AHTAP connector and the green authored trail.
                double focusNow = focus - 40;
                renderer.Clear();
                renderer.Bind(chart, new DspChartClock());
                renderFrame.Invoke(renderer, new object[] { focusNow });
                Require(active.Contains(focusActionIndex),
                    "ALTER EGO starting ASC was culled before the judgement plane");
                object focusVisual = active[focusActionIndex];
                var focusHead = (GameObject)focusVisual.GetType().GetField("Head").GetValue(focusVisual);
                var focusLine = (GameObject)focusVisual.GetType().GetField("Line").GetValue(focusVisual);
                var focusArrow = (GameObject)focusVisual.GetType().GetField("Accent").GetValue(focusVisual);
                Require(focusHead.activeSelf,
                    "ALTER EGO starting ASC lost its green AIR-trail connector");
                Require(isTrailEntry != null &&
                        (bool)isTrailEntry.Invoke(null, new object[] { chart.Notes[focusActionIndex] }),
                    "ALTER EGO starting ASC was not classified as an AHTAP trail entry");
                Mesh focusHeadMesh = focusHead.GetComponent<MeshFilter>().sharedMesh;
                Require(focusHeadMesh != null && focusHeadMesh.vertexCount > 0,
                    "ALTER EGO starting ASC has no visible AHTAP connector mesh");
                Require(focusLine.activeSelf && focusLine.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                    "ALTER EGO green AIR trail did not start from its connector");
                Bounds headBounds = focusHead.GetComponent<MeshRenderer>().bounds;
                Bounds lineBounds = focusLine.GetComponent<MeshRenderer>().bounds;
                Require(lineBounds.min.z <= headBounds.center.z + .02f &&
                        lineBounds.max.z >= headBounds.center.z - .02f,
                    "ALTER EGO green AIR trail is spatially detached from its connector");
                Require(focusArrow.activeSelf,
                    "ALTER EGO starting ASC lost its synthesized upward AIR arrow");
                Mesh focusArrowMesh = focusArrow.GetComponent<MeshFilter>().sharedMesh;
                Require(focusArrowMesh != null && focusArrowMesh.vertexCount > 0,
                    "ALTER EGO starting ASC upward AIR arrow has no visible mesh");
                Bounds arrowBounds = focusArrow.GetComponent<MeshRenderer>().bounds;
                Require(arrowBounds.max.y >= headBounds.center.y - .02f,
                    "ALTER EGO starting ASC upward AIR arrow does not reach its green connector");

                Camera camera = cameraRoot.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 11.45f, -9.2f);
                camera.transform.rotation = Quaternion.Euler(35f, 0, 0);
                camera.fieldOfView = 48f;
                camera.farClipPlane = 100f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                var captureTimes = nearbyAir.Select(item => item.note.TimeMs)
                    .Append(focus)
                    .Distinct()
                    .OrderBy(time => time);
                foreach (double eventTime in captureTimes) {
                    double now = eventTime - 40;
                    renderer.Clear();
                    renderer.Bind(chart, new DspChartClock());
                    renderFrame.Invoke(renderer, new object[] { now });
                    Capture(camera, $"Build/alter-ego-air-{eventTime:F0}.png");
                    LogVisibleHeads(chart, renderer, camera, now, eventTime);
                }
            } finally {
                UnityEngine.Object.DestroyImmediate(cameraRoot);
                UnityEngine.Object.DestroyImmediate(root);
            }

            Debug.Log($"ALTER EGO AIR regression passed: combo532 time={focus:F3}ms, " +
                $"nearbyAir={nearbyAir.Count}, paired={samePlaneGround.Count}.");
        }

        private static AirDirection DirectionForOpcode(string opcode) => opcode switch
        {
            "AIR" => AirDirection.Up,
            "AUL" => AirDirection.UpLeft,
            "AUR" => AirDirection.UpRight,
            "ADW" => AirDirection.Down,
            "ADL" => AirDirection.DownLeft,
            "ADR" => AirDirection.DownRight,
            _ => AirDirection.None
        };

        private static void ValidateContinuousLongChain(ParsedChart chart, ChartPlaybackRenderer renderer)
        {
            var geometries = (IDictionary)typeof(ChartPlaybackRenderer)
                .GetField("longChainGeometry", Private).GetValue(renderer);
            var group = chart.Notes.Select((note, index) => (note, index))
                .Where(item => item.note.LongChainId >= 0 &&
                    item.note.Kind is NoteKind.Slide or NoteKind.AirAction or NoteKind.AirHold)
                .GroupBy(item => item.note.LongChainId)
                .Where(items => items.Count() >= 2 && geometries.Contains(items.Key))
                .OrderByDescending(items => items.Count())
                .FirstOrDefault();
            Require(group != null, "ALTER EGO fixture has no unbranched multi-record long-note chain");

            int[] indices = group.OrderBy(item => item.note.TimeMs).Select(item => item.index).ToArray();
            ChartNote[] members = indices.Select(index => chart.Notes[index]).ToArray();
            var judgementSnapshot = members.Select(note =>
                (note.HasVisualHead, note.VisibleHead, note.JudgePointCount)).ToArray();
            int owner = indices[0];
            var owners = (IDictionary)typeof(ChartPlaybackRenderer)
                .GetField("longChainOwnerByNote", Private).GetValue(renderer);
            Require(indices.All(index => (int)owners[index] == owner),
                "continuous chain records do not resolve to one visual owner");
            Require(geometries.Contains(group.Key), "continuous chain has no combined ribbon geometry");

            object chain = geometries[group.Key];
            object ribbon = chain.GetType().GetField("Ribbon").GetValue(chain);
            Type ribbonType = ribbon.GetType();
            var times = (double[])ribbonType.GetField("AuthoredTimes").GetValue(ribbon);
            var lanes = (float[])ribbonType.GetField("Lanes").GetValue(ribbon);
            var widths = (float[])ribbonType.GetField("Widths").GetValue(ribbon);
            Require(times.Length > members.Length * 2,
                "combined ribbon was not sampled densely enough for smooth curves");
            Require(members.All(note => note.HasVisualHead || note.IsContinuation),
                "combination removed authored visual nodes");
            for (int i = 1; i < times.Length; i++) {
                Require(times[i] > times[i - 1], "combined ribbon sample times are not strictly increasing");
                float previousCenter = lanes[i - 1] + widths[i - 1] * .5f;
                float center = lanes[i] + widths[i] * .5f;
                Require(Math.Abs(center - previousCenter) < 1.25f,
                    $"combined curve has a visible lane discontinuity at sample {i}: " +
                    $"{previousCenter:F3}->{center:F3}, t={times[i - 1]:F2}->{times[i]:F2}");
            }

            double now = (members[0].TimeMs + members[members.Length - 1].EndTimeMs) * .5;
            MethodInfo renderFrame = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private);
            renderFrame.Invoke(renderer, new object[] { now });
            var active = (IDictionary)typeof(ChartPlaybackRenderer)
                .GetField("active", Private).GetValue(renderer);
            if (active.Contains(owner)) {
                object ownerVisual = active[owner];
                var body = (GameObject)ownerVisual.GetType().GetField("Body").GetValue(ownerVisual);
                Require(body.activeSelf && body.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                    "combined long-chain owner has no rendered body mesh");
            }
            foreach (int index in indices.Skip(1)) {
                if (!active.Contains(index)) continue;
                object nodeVisual = active[index];
                var body = (GameObject)nodeVisual.GetType().GetField("Body").GetValue(nodeVisual);
                Require(!body.activeSelf, $"long-chain segment {index} renders a duplicate body mesh");
            }
            for (int i = 0; i < members.Length; i++)
                Require(members[i].HasVisualHead == judgementSnapshot[i].HasVisualHead &&
                        members[i].VisibleHead == judgementSnapshot[i].VisibleHead &&
                        members[i].JudgePointCount == judgementSnapshot[i].JudgePointCount,
                    $"long-chain display merge changed judgement ownership at member {i}");
            Debug.Log($"Continuous long-chain regression passed: id={group.Key}, records={members.Length}, " +
                $"samples={times.Length}, scoringHeads={members.Count(note => note.VisibleHead)}; " +
                "visual/judgement ownership unchanged.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void LogVisibleHeads(ParsedChart chart, ChartPlaybackRenderer renderer,
            Camera camera, double now, double eventTime)
        {
            var active = (IDictionary)typeof(ChartPlaybackRenderer)
                .GetField("active", Private).GetValue(renderer);
            foreach (DictionaryEntry entry in active) {
                int noteIndex = (int)entry.Key;
                ChartNote note = chart.Notes[noteIndex];
                if (Math.Abs(note.TimeMs - eventTime) > 360.0 ||
                    note.Kind is not (NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction or
                        NoteKind.Tap or NoteKind.ExTap)) continue;
                object visual = entry.Value;
                var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
                if (!head.activeSelf) continue;
                var meshRenderer = head.GetComponent<MeshRenderer>();
                Vector3 screen = camera.WorldToScreenPoint(meshRenderer.bounds.center);
                Debug.Log($"ALTER EGO visible head: capture={eventTime:F3}, now={now:F3}, " +
                    $"index={noteIndex}, type={note.OriginalType}/{note.SourceType}, " +
                    $"time={note.TimeMs:F3}, screen=({screen.x:F1},{screen.y:F1},{screen.z:F2}), " +
                    $"queue={meshRenderer.sharedMaterial?.renderQueue ?? -1}");
            }
        }

        private static void Capture(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try {
                camera.targetTexture = target;
                camera.aspect = (float)width / height;
                RenderTexture.active = target;
                camera.Render();
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            } finally {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
