using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ChunithmMobile.Chart
{
    // C2S 解析入口：把文本格式转换为稳定的 ParsedChart 对象。
    // 解析阶段尽量保留原始记录，后续的滚速映射、判定点展开和视觉配对在专门模块完成。
    /// <summary>
    /// UTF-8 C2S reader. All authored times stay as double precision so short
    /// SFL reverse sections are not rounded to different frames.
    /// </summary>
    public static class C2sParser
    {
        private struct RawBpm { public long Tick; public double Bpm; public RawBpm(long tick, double bpm) { Tick = tick; Bpm = bpm; } }
        private struct RawScroll { public long Tick, Duration; public double Multiplier; public int ProfileId; public RawScroll(long tick, long duration, double multiplier, int profileId = 0) { Tick = tick; Duration = duration; Multiplier = multiplier; ProfileId = profileId; } }
        private struct RawScrollAssignment
        {
            public long Tick, Duration; public int Lane, Width, ProfileId;
            public RawScrollAssignment(long tick, int lane, int width, long duration, int profileId)
            { Tick = tick; Lane = lane; Width = width; Duration = duration; ProfileId = profileId; }
        }
        private sealed class RawNote
        {
            public NoteKind Kind; public long Tick, Duration; public int Lane, Width, EndLane, EndWidth;
            public AirDirection Direction; public float AirHeight, EndAirHeight; public bool VisibleHead;
            public string OriginalType, SourceType;
            public List<long> ExplicitJudgeTicks;
            public RawNote(NoteKind kind, long tick, long duration, int lane, int width, int endLane, int endWidth,
                AirDirection direction, float airHeight, float endAirHeight, bool visibleHead,
                string originalType, string sourceType)
            { Kind = kind; Tick = tick; Duration = duration; Lane = lane; Width = width; EndLane = endLane; EndWidth = endWidth;
              Direction = direction; AirHeight = airHeight; EndAirHeight = endAirHeight; VisibleHead = visibleHead;
              OriginalType = originalType; SourceType = sourceType; }
        }

        private sealed class RawGuidePath
        {
            public long Tick, Duration;
            public int Lane, Width, EndLane, EndWidth, StyleInterval;
            public float AirHeight, EndAirHeight;
            public string Color, SecondaryColor;
            public readonly List<(RawNote note, float lane, float width, float height, bool first)> Nodes = new();
        }

        /// <summary>
        /// 读取并解析 C2S 文件，返回包含音符、BPM、滚速和显式判定时间的 ParsedChart。
        /// 本方法不创建 Unity 对象、不推进时间轴，也不进行判定，因此可由编辑器自测复用。
        /// </summary>
        public static ParsedChart Parse(string path)
        {
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }

        public static ParsedChart Parse(Stream stream)
        {
            long resolution = 384;
            double defaultBpm = 120;
            var bpms = new List<RawBpm>();
            var scrolls = new List<RawScroll>();
            var scrollAssignments = new List<RawScrollAssignment>();
            var rawNotes = new List<RawNote>();
            var rawGuidePaths = new List<RawGuidePath>();
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var noteTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            double explicitDurationMs = 0;

            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536, false);
            while (reader.ReadLine() is { } rawLine)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('\t');
                switch (p[0])
                {
                    case "RESOLUTION": resolution = LongAt(p, 1, resolution); break;
                    case "BPM_DEF": defaultBpm = DoubleAt(p, 1, defaultBpm); break;
                    case "DURATION": explicitDurationMs = Math.Max(0, DoubleAt(p, 1)); break;
                    case "BPM" when p.Length >= 4:
                        bpms.Add(new RawBpm(AbsoluteTick(p[1], p[2], resolution), DoubleAt(p, 3, defaultBpm)));
                        break;
                    case "SFL" when p.Length >= 5:
                        scrolls.Add(new RawScroll(AbsoluteTick(p[1], p[2], resolution),
                            Math.Max(0, LongAt(p, 3)), DoubleAt(p, 4, 1)));
                        break;
                    case "SLP" when p.Length >= 5:
                        // The last value is a scroll-profile id, not a boolean.
                        // Profile 0 moves the global playfield. Profiles 1..16
                        // are assigned to objects in authored time/lane areas by SLA records.
                        scrolls.Add(new RawScroll(AbsoluteTick(p[1], p[2], resolution),
                            Math.Max(0, LongAt(p, 3)), DoubleAt(p, 4, 1), IntAt(p, 5)));
                        break;
                    case "STP" when p.Length >= 4:
                        scrolls.Add(new RawScroll(AbsoluteTick(p[1], p[2], resolution),
                            Math.Max(0, LongAt(p, 3)), 0));
                        break;
                    case "SLA" when p.Length >= 7:
                        scrollAssignments.Add(new RawScrollAssignment(AbsoluteTick(p[1], p[2], resolution),
                            IntAt(p, 3), Math.Max(1, IntAt(p, 4)), Math.Max(0, LongAt(p, 5)), IntAt(p, 6)));
                        break;
                    case "T_JUDGE_TAP": case "T_JUDGE_HLD": case "T_JUDGE_SLD":
                    case "T_JUDGE_AIR": case "T_JUDGE_FLK": case "T_JUDGE_ALL":
                        totals[p[0][8..]] = IntAt(p, 1); break;
                    case "T_NOTE_TAP": case "T_NOTE_CHR": case "T_NOTE_FLK": case "T_NOTE_MNE":
                    case "T_NOTE_HLD": case "T_NOTE_SLD": case "T_NOTE_AIR": case "T_NOTE_AHD":
                    case "T_NOTE_ALL":
                        noteTotals[p[0][7..]] = IntAt(p, 1); break;
                    default:
                        if (ParseGuidePath(p, resolution) is { } guidePath) {
                            rawGuidePaths.Add(guidePath);
                            break;
                        }
                        if (ParseNote(p, resolution) is { } note) rawNotes.Add(note);
                        break;
                }
            }

            // C2S exporters normally write note records chronologically, but
            // that is not part of the file contract. Continuation ownership
            // (HLD/SLD/AIR-HOLD sections) is temporal, so normalize note order
            // before resolving visibility and allocating judge points. Keep
            // command ordering for BPM/scroll records unchanged.
            // ASD marks the exported segment endpoint (native C2S writer
            // 0x011ED363..0x011ED492), not its start. Give that action plane an
            // independent AIR checker; the source segment still owns geometry.
            foreach (RawNote segment in rawNotes.ToArray())
                if (segment.OriginalType == "ASD")
                    rawNotes.Add(new RawNote(NoteKind.AirAction, segment.Tick + segment.Duration, 0,
                        segment.EndLane, segment.EndWidth, segment.EndLane, segment.EndWidth,
                        AirDirection.None, segment.EndAirHeight, segment.EndAirHeight, true,
                        "ASD_NODE", ""));
            ExpandLadderNodes(rawGuidePaths, rawNotes);
            rawNotes = rawNotes.OrderBy(note => note.Tick).ToList();

            if (bpms.All(x => x.Tick != 0)) bpms.Add(new RawBpm(0, defaultBpm));
            bpms.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            var time = new TickTimeMapper(resolution, bpms);
            ResolveSegmentVisibility(rawNotes, noteTotals);
            bool[] slideCheckpoints = ResolveSlideCheckpoints(rawNotes);
            var judgePoints = AllocateJudgePoints(rawNotes, totals, time);
            var notesAt = rawNotes.GroupBy(n => n.Tick).ToDictionary(g => g.Key, g => g.ToList());
            var visualHeads = ResolveSlideVisualHeads(rawNotes, notesAt);
            ResolveAirHoldVisualHeads(rawNotes, visualHeads);
            ResolveAirActionVisualHeads(rawNotes, visualHeads);
            ResolveStandaloneAirVisualHeads(rawNotes, visualHeads);
            var notes = rawNotes.Select((n, i) =>
            {
                bool continuation = n.Duration > 0 && notesAt.TryGetValue(n.Tick + n.Duration, out var next) &&
                    next.Any(x => IsContinuousSegment(n, x));
                bool airHoldContinuation = IsHeadlessAirHoldContinuation(n, i, rawNotes);
                bool isContinuation = (!n.VisibleHead && n.Kind != NoteKind.AirHold) || airHoldContinuation;
                return new ChartNote {
                    Kind = n.Kind, TimeMs = time.ToMs(n.Tick), EndTimeMs = time.ToMs(n.Tick + n.Duration),
                    Lane = n.Lane, Width = Math.Max(1, n.Width), EndLane = n.EndLane,
                    EndWidth = Math.Max(1, n.EndWidth), AirDirection = n.Direction, AirFlick = n.SourceType == "FLK",
                    AirHeight = Math.Clamp(n.AirHeight, -16, 32), EndAirHeight = Math.Clamp(n.EndAirHeight, -16, 32),
                    VisibleHead = n.VisibleHead,
                    HasVisualHead = visualHeads[i],
                    IsContinuation = isContinuation,
                    IsSlideCheckpoint = slideCheckpoints[i],
                    // SLC/SXC are curve/control records. They may be the
                    // final record in an authored slide, but they do not
                    // carry the visible blue/yellow tail cap. The cap belongs
                    // to the terminal SLD/SXD record only. Other long-note
                    // families keep their existing tail behavior.
                    VisibleTail = n.Duration > 0 && !continuation && CanRenderVisualTail(n),
                    OriginalType = n.OriginalType, SourceType = n.SourceType,
                    JudgePointCount = judgePoints[i],
                    ExplicitJudgeTimesMs = n.ExplicitJudgeTicks?.Select(time.ToMs).ToList()
                };
            }).OrderBy(n => n.TimeMs).ToList();
            ResolveLongVisualChains(notes, rawNotes);

            double latestNoteMs = notes.Count == 0 ? 0 : notes.Max(n => Math.Max(n.TimeMs, n.EndTimeMs));
            if (rawGuidePaths.Count > 0)
                latestNoteMs = Math.Max(latestNoteMs, rawGuidePaths.Max(raw => time.ToMs(raw.Tick + raw.Duration)));
            double duration = explicitDurationMs > 0
                ? Math.Max(explicitDurationMs, latestNoteMs)
                : latestNoteMs + 2500;
            var mappedScrolls = scrolls.Where(s => s.ProfileId == 0).Select(s => new ScrollEvent(time.ToMs(s.Tick),
                time.ToMs(s.Tick + s.Duration), s.Multiplier)).OrderBy(s => s.StartMs).ToList();
            var mappedProfiles = scrolls.Where(s => s.ProfileId > 0).GroupBy(s => s.ProfileId).ToDictionary(
                group => group.Key,
                group => group.Select(s => new ScrollEvent(time.ToMs(s.Tick), time.ToMs(s.Tick + s.Duration), s.Multiplier))
                    .OrderBy(s => s.StartMs).ToList());
            var mappedAssignments = scrollAssignments.Select(s => new ScrollAssignment(time.ToMs(s.Tick),
                time.ToMs(s.Tick + s.Duration), s.Lane, s.Width, s.ProfileId)).OrderBy(s => s.StartMs).ToList();
            foreach (var note in notes)
                note.ScrollProfileId = FindScrollProfile(mappedAssignments, note.TimeMs, note.Lane, note.Width);
            var noteIndices = rawNotes.Select((note, index) => (note, index)).ToDictionary(x => x.note, x => x.index);
            var guidePaths = rawGuidePaths.Select(raw => new VisualGuidePath {
                TimeMs = time.ToMs(raw.Tick), EndTimeMs = time.ToMs(raw.Tick + raw.Duration),
                Lane = raw.Lane, Width = Math.Max(1, raw.Width), EndLane = raw.EndLane,
                EndWidth = Math.Max(1, raw.EndWidth), AirHeight = Math.Clamp(raw.AirHeight, -16, 32),
                EndAirHeight = Math.Clamp(raw.EndAirHeight, -16, 32), StyleInterval = raw.StyleInterval,
                StyleIntervalMilliseconds = time.ToMs(raw.Tick + raw.StyleInterval) - time.ToMs(raw.Tick),
                Color = raw.Color, SecondaryColor = raw.SecondaryColor,
                Nodes = raw.Nodes.Select((node, index) => new AirLadderNode {
                    TimeMs = time.ToMs(node.note.Tick), Lane = node.lane, Width = node.width,
                    Height = node.height, NoteIndex = noteIndices[node.note], IsFirst = node.first,
                    ScrollProfileId = notes[noteIndices[node.note]].ScrollProfileId
                }).ToList()
            }).OrderBy(item => item.TimeMs).ToList();
            foreach (VisualGuidePath guidePath in guidePaths)
                guidePath.ScrollProfileId = FindScrollProfile(mappedAssignments, guidePath.TimeMs,
                    guidePath.Lane, guidePath.Width);
            // T_JUDGE_ALL is metadata supplied by the exporter. A damaged or
            // newer chart can declare a value that the supported records cannot
            // actually schedule. Never invent checkpoints on unrelated notes
            // to force that number: scoring and combo use the deterministic
            // schedule that gameplay will really consume.
            int scheduledTotal = notes.Sum(ScheduledPointCount);
            int declaredTotal = totals.TryGetValue("ALL", out var all) ? Math.Max(0, all) : scheduledTotal;
            // A few hand-authored charts omit BPM records entirely. The parser
            // already uses the default BPM for time conversion; keep the same
            // fallback here instead of indexing an empty list during gameplay
            // startup.
            double chartBpm = bpms.Count > 0 ? bpms[0].Bpm : defaultBpm;
            return new ParsedChart(notes, duration, chartBpm, scheduledTotal,
                mappedScrolls, mappedProfiles, mappedAssignments, declaredTotal, guidePaths);
        }

        private static void ExpandLadderNodes(List<RawGuidePath> paths, List<RawNote> notes)
        {
            // ALD has no explicit chain id. Join only an unambiguous one-to-one
            // exact endpoint with the same cadence/style. Interleaved layers
            // and branching paths stay independent instead of stealing nodes.
            var successors = new Dictionary<RawGuidePath, RawGuidePath>();
            var predecessors = new Dictionary<RawGuidePath, List<RawGuidePath>>();
            var starts = paths.Where(p => p.StyleInterval > 0).GroupBy(p => p.Tick)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (RawGuidePath path in paths) {
                if (path.StyleInterval <= 0 || path.Duration <= 0 ||
                    !starts.TryGetValue(path.Tick + path.Duration, out var candidates)) continue;
                var matching = candidates.Where(next => next != path &&
                    next.StyleInterval == path.StyleInterval && next.Lane == path.EndLane &&
                    next.Width == path.EndWidth && Math.Abs(next.AirHeight - path.EndAirHeight) < .00001f &&
                    next.Color == path.Color && next.SecondaryColor == path.SecondaryColor).ToArray();
                if (matching.Length != 1) continue;
                successors[path] = matching[0];
                if (!predecessors.TryGetValue(matching[0], out var owners))
                    predecessors[matching[0]] = owners = new List<RawGuidePath>();
                owners.Add(path);
            }
            foreach (var pair in successors.ToArray())
                if (predecessors[pair.Value].Count != 1) successors.Remove(pair.Key);
            var continued = successors.Values.ToHashSet();
            foreach (RawGuidePath first in paths) {
                if (first.StyleInterval <= 0 || continued.Contains(first)) continue;
                var segments = new List<RawGuidePath> { first };
                while (successors.TryGetValue(segments[segments.Count - 1], out var next)) segments.Add(next);
                long end = segments[segments.Count - 1].Tick + segments[segments.Count - 1].Duration;
                int segmentIndex = 0;
                for (long tick = first.Tick; tick <= end; tick += first.StyleInterval) {
                    // A seam belongs to the next piece; it is sampled once,
                    // and the global cadence never restarts at that seam.
                    while (segmentIndex + 1 < segments.Count &&
                        tick >= segments[segmentIndex].Tick + segments[segmentIndex].Duration) segmentIndex++;
                    RawGuidePath guide = segments[segmentIndex];
                    float progress = guide.Duration == 0 ? 0 : (float)((double)(tick - guide.Tick) / guide.Duration);
                    float lane = guide.Lane + (guide.EndLane - guide.Lane) * progress;
                    float width = guide.Width + (guide.EndWidth - guide.Width) * progress;
                    float height = guide.AirHeight + (guide.EndAirHeight - guide.AirHeight) * progress;
                    var node = new RawNote(NoteKind.AirAction, tick, 0,
                        (int)Math.Round(lane), (int)Math.Round(width), (int)Math.Round(lane), (int)Math.Round(width),
                        AirDirection.None, height, height, true, "ALD", "");
                    guide.Nodes.Add((node, lane, width, height, tick == first.Tick));
                    notes.Add(node);
                    if (end - tick < first.StyleInterval) break;
                }
            }
        }

        private static RawGuidePath ParseGuidePath(string[] p, long resolution)
        {
            if (p.Length < 11 || !string.Equals(p[0], "ALD", StringComparison.OrdinalIgnoreCase)) return null;
            int lane = IntAt(p, 3), width = Math.Max(1, IntAt(p, 4));
            return new RawGuidePath {
                Tick = AbsoluteTick(p[1], p[2], resolution),
                StyleInterval = Math.Max(0, IntAt(p, 5)),
                AirHeight = FloatAt(p, 6, 5), Duration = Math.Max(0, LongAt(p, 7)),
                EndLane = IntAt(p, 8, lane), EndWidth = Math.Max(1, IntAt(p, 9, width)),
                EndAirHeight = FloatAt(p, 10, FloatAt(p, 6, 5)),
                Lane = lane, Width = width, Color = StringAt(p, 11, "DEF"),
                SecondaryColor = StringAt(p, 12, "DEF")
            };
        }

        private static int ScheduledPointCount(ChartNote note)
        {
            if (note.ExplicitJudgeTimesMs is { Count: > 0 }) return note.ExplicitJudgeTimesMs.Count;
            return Math.Max(0, note.JudgePointCount);
        }

        private static void ResolveLongVisualChains(List<ChartNote> notes, IReadOnlyList<RawNote> rawNotes)
        {
            int nextChain = 0;
            var endings = new Dictionary<long, List<int>>();
            for (int i = 0; i < notes.Count; i++) {
                ChartNote note = notes[i];
                if (note.EndTimeMs <= note.TimeMs || !IsLongVisualNote(note)) continue;
                int owner = -1;
                RawNote raw = rawNotes[i];
                if (endings.TryGetValue(raw.Tick, out var candidates))
                    foreach (int j in candidates)
                        if (IsContinuousSegment(rawNotes[j], raw)) { owner = j; break; }
                if (owner >= 0) {
                    note.LongChainId = notes[owner].LongChainId;
                    note.IsLongChainHead = false;
                    notes[owner].IsLongChainTail = false;
                } else {
                    note.LongChainId = nextChain++;
                    note.IsLongChainHead = true;
                }
                note.IsLongChainTail = true;
                long end = raw.Tick + raw.Duration;
                if (!endings.TryGetValue(end, out var list)) endings[end] = list = new List<int>();
                list.Add(i);
            }
        }

        private static bool IsLongVisualNote(ChartNote note) =>
            note.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or
                NoteKind.AirHold or NoteKind.AirAction;

        private static bool CanRenderVisualTail(RawNote note) =>
            note.OriginalType is not ("SLC" or "SXC" or "ASC" or "ASD");

        private static int FindScrollProfile(List<ScrollAssignment> assignments, double timeMs, int lane, int width)
        {
            int endLane = lane + width;
            for (int i = assignments.Count - 1; i >= 0; i--) {
                var area = assignments[i];
                if (timeMs < area.StartMs || timeMs >= area.EndMs) continue;
                if (lane < area.Lane + area.Width && area.Lane < endLane) return area.ScrollProfileId;
            }
            return 0;
        }

        private static void ResolveSegmentVisibility(IReadOnlyList<RawNote> notes,
            IReadOnlyDictionary<string, int> totals)
        {
            // Notes are sorted by start, not by end. A short TAP between a
            // curve and its endpoint must not terminate the ownership search.
            var latestEnd = new long[notes.Count];
            long maximumEnd = long.MinValue;
            for (int i = 0; i < notes.Count; i++) {
                maximumEnd = Math.Max(maximumEnd, notes[i].Tick + notes[i].Duration);
                latestEnd[i] = maximumEnd;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                RawNote current = notes[i];
                if (!CanOwnHead(current))
                {
                    current.VisibleHead = false;
                    continue;
                }

                // ASC/ASD encode their parent opcode in column 5. A source
                // that is itself an AIR long-note opcode is a continuation,
                // even when dense same-time records obscure its owner.
                if (IsAirLongContinuationSource(current))
                {
                    current.VisibleHead = false;
                    continue;
                }

                // A source label describes how the chart exporter produced a
                // record. It is not enough to identify continuation sections:
                // a new chain can also carry the same source label. The
                // runtime links only a segment whose end lands exactly on this
                // segment's start and whose lane ranges still overlap.
                current.VisibleHead = true;
                for (int previousIndex = i - 1; previousIndex >= 0; previousIndex--)
                {
                    RawNote previous = notes[previousIndex];
                    if (latestEnd[previousIndex] < current.Tick) break;
                    if (IsContinuousSegment(previous, current))
                    {
                        current.VisibleHead = false;
                        break;
                    }
                }
            }

            // Some exporters write every geometric SLD fragment as an SLD
            // record, while T_NOTE_SLD counts only the authored slide heads.
            // Do not globally merge touching SLD records: hand-authored charts
            // can intentionally place independent objects at a shared seam.
            // When the file supplies a smaller SLD head count, consume only
            // the exact end-to-start candidates needed to reach that count.
            if (totals.TryGetValue("SLD", out int declaredSlides)) {
                var slideRecords = notes.Where(n => n.OriginalType == "SLD").ToList();
                int excess = slideRecords.Count - Math.Max(0, declaredSlides);
                if (excess > 0) {
                    foreach (RawNote current in slideRecords) {
                        if (excess == 0) break;
                        if (!current.VisibleHead) continue;
                        bool continuation = slideRecords.Any(previous =>
                            previous != current && previous.Duration > 0 &&
                            previous.Tick + previous.Duration == current.Tick &&
                            previous.EndLane < current.Lane + Math.Max(1, current.Width) &&
                            current.Lane < previous.EndLane + Math.Max(1, previous.EndWidth));
                        if (!continuation) continue;
                        current.VisibleHead = false;
                        excess--;
                    }
                }
            }
        }

        private static bool[] ResolveSlideCheckpoints(IReadOnlyList<RawNote> notes)
        {
            var result = new bool[notes.Count];
            var endings = new Dictionary<(long tick, int lane, int width), List<int>>();
            for (int i = 0; i < notes.Count; i++) {
                RawNote current = notes[i];
                if (current.Kind != NoteKind.Slide) continue;
                bool moving = current.Lane != current.EndLane || current.Width != current.EndWidth;
                if (endings.TryGetValue((current.Tick, current.Lane, current.Width), out var owners))
                    foreach (int owner in owners) {
                        RawNote previous = notes[owner];
                        if (moving || previous.Lane != previous.EndLane || previous.Width != previous.EndWidth ||
                            result[owner] || !previous.VisibleHead) {
                            result[i] = true;
                            break;
                        }
                    }
                if (current.Duration <= 0) continue;
                var end = (current.Tick + current.Duration, current.EndLane, current.EndWidth);
                if (!endings.TryGetValue(end, out var list)) endings[end] = list = new List<int>();
                list.Add(i);
            }
            return result;
        }

        private static bool CanOwnHead(RawNote note)
        {
            // AHD/AHX are AIR-HOLD body records. SLD and SXD own a slide head; SLC/SXC are control
            // sections and never create a standalone head. HXD is a standalone
            // EX HOLD and owns its head.
            return note.OriginalType is not
                ("AHD" or "AHX" or "SLC" or "SXC");
        }

        private static bool[] ResolveSlideVisualHeads(IReadOnlyList<RawNote> notes,
            Dictionary<long, List<RawNote>> notesAt)
        {
            // Curves may begin with SLC and only end in SLD. Their visual
            // start is independent of C2S checkpoint/head allocation.
            var ends = new HashSet<(long tick, int lane, int width)>();
            foreach (RawNote note in notes)
                if (note.Kind == NoteKind.Slide && note.Duration > 0)
                    ends.Add((note.Tick + note.Duration, note.EndLane, note.EndWidth));

            var result = new bool[notes.Count];
            for (int i = 0; i < notes.Count; i++) {
                RawNote note = notes[i];
                result[i] = note.VisibleHead;
                if (note.OriginalType != "SLC" ||
                    ends.Contains((note.Tick, note.Lane, note.Width))) continue;
                bool existingHead = notesAt[note.Tick].Any(other => other.VisibleHead &&
                    other.Lane == note.Lane && other.Width == note.Width &&
                    other.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Hold or NoteKind.Slide or NoteKind.Flick);
                result[i] = !existingHead;
            }
            return result;
        }

        private static bool IsContinuousSegment(RawNote previous, RawNote current)
        {
            if (previous == null || current == null || previous.Duration <= 0)
                return false;
            // Only opcodes that explicitly describe a continuation may take
            // ownership from the preceding record. Two independent SLD/HLD
            // notes are allowed to touch at the same tick and lane; treating
            // that geometry alone as a chain swallowed the second head and
            // the first tail.
            // SLC/SXC/SXD curves may terminate in SLD. That SLD is an area
            // checkpoint, not a second TAP head. Independent SLD->SLD and
            // HLD->HLD records still retain their authored head judgements.
            bool curveEndpoint = current.OriginalType == "SLD" &&
                previous.OriginalType is ("SLC" or "SXC" or "SXD") &&
                previous.EndLane == current.Lane && previous.EndWidth == current.Width;
            if (!IsContinuationOpcode(current) && !curveEndpoint) return false;
            if (previous.Kind != current.Kind || previous.Tick + previous.Duration != current.Tick)
                return false;

            int previousEnd = previous.EndLane;
            int previousEndExclusive = previous.EndLane + Math.Max(1, previous.EndWidth);
            int currentStart = current.Lane;
            int currentEndExclusive = current.Lane + Math.Max(1, current.Width);
            return currentStart < previousEndExclusive && previousEnd < currentEndExclusive;
        }

        private static bool IsContinuationOpcode(RawNote note) =>
            note.OriginalType is "SLC" or "SXC" or "SXD" ||
            IsAirLongContinuationSource(note);

        private static bool IsAirLongContinuationSource(RawNote note) =>
            note.Kind is NoteKind.AirHold or NoteKind.AirAction &&
            note.SourceType is "AHD" or "AHX" or "ASC" or "ASD";

        private static bool IsHeadlessAirHoldContinuation(RawNote note, int noteIndex, IReadOnlyList<RawNote> notes)
        {
            if (note == null || note.Kind != NoteKind.AirHold || note.VisibleHead) return false;
            if (IsAirLongContinuationSource(note)) return true;
            for (int previousIndex = noteIndex - 1; previousIndex >= 0; previousIndex--)
            {
                RawNote previous = notes[previousIndex];
                if (previous.Tick + previous.Duration < note.Tick) break;
                if (IsContinuousSegment(previous, note)) return true;
            }
            return false;
        }

        private static void ResolveAirHoldVisualHeads(IReadOnlyList<RawNote> notes, bool[] visualHeads)
        {
            // AHD/AHX records are continuation records for scoring, but the
            // first record of each authored AIR-HOLD still has a visible cap.
            // Keep that visual head separate from the judgement ownership so
            // continuation segments do not gain an extra head judgement.
            for (int i = 0; i < notes.Count; i++) {
                RawNote note = notes[i];
                if (note.Kind != NoteKind.AirHold) continue;
                bool hasPrevious = false;
                for (int j = i - 1; j >= 0; j--) {
                    RawNote previous = notes[j];
                    if (previous.Tick + previous.Duration < note.Tick) continue;
                    // Geometry alone does not make a continuation. A new AHD
                    // may start exactly where another one ends while owning a
                    // fresh TAP/AIR gesture. Only an opcode whose source names
                    // the AIR long family may suppress the new visual cap.
                    if (IsContinuousSegment(previous, note)) {
                        hasPrevious = true;
                        break;
                    }
                }
                if (!hasPrevious) visualHeads[i] = true;
            }
        }

        private static void ResolveAirActionVisualHeads(IReadOnlyList<RawNote> notes, bool[] visualHeads)
        {
            // The segment owns only its entry cap. ASD_NODE owns the discrete
            // endpoint cap/checker; ALD is drawn by AirLadderVisuals using the
            // exact same indexed judge note, never by the generic AHACT layer.
            for (int i = 0; i < notes.Count; i++) {
                RawNote note = notes[i];
                if (note.Kind != NoteKind.AirAction) continue;
                visualHeads[i] = note.OriginalType != "ALD" && note.VisibleHead;
            }
        }

        private static void ResolveStandaloneAirVisualHeads(IReadOnlyList<RawNote> notes, bool[] visualHeads)
        {
            // AIR/AUL/AUR/AD* is a separate arrow even when it occupies the
            // exact timing plane and lane range of a TAP, AIR-HOLD, or AIR
            // ACTION record. Long-note ownership may hide continuation caps,
            // but it must never absorb this directional arrow. Judgement
            // ownership remains in VisibleHead/JudgePointCount; this method
            // changes only the authored visual layer.
            for (int i = 0; i < notes.Count; i++)
                if (notes[i].Kind == NoteKind.Air)
                    visualHeads[i] = true;
        }

        private static RawNote ParseNote(string[] p, long resolution)
        {
            if (p.Length < 5) return null;
            var kind = p[0] switch {
                "TAP" => NoteKind.Tap, "CHR" => NoteKind.ExTap, "MNE" => NoteKind.Mine,
                "FLK" => NoteKind.Flick, "HLD" or "HXD" => NoteKind.Hold,
                "SLD" or "SLC" or "SXC" or "SXD" => NoteKind.Slide,
                "AIR" or "AUL" or "AUR" or "ADR" or "ADL" or "ADW" => NoteKind.Air,
                "AHD" or "AHX" => NoteKind.AirHold, "ASC" or "ASD" => NoteKind.AirAction,
                _ => (NoteKind)(-1)
            };
            if ((int)kind < 0) return null;
            long tick = AbsoluteTick(p[1], p[2], resolution);
            int lane = IntAt(p, 3), width = IntAt(p, 4);
            long duration = p[0] switch {
                "HLD" or "HXD" or "SLD" or "SLC" or "SXC" or "SXD" => LongAt(p, 5),
                "AHD" or "AHX" => LongAt(p, 6), "ASC" or "ASD" => LongAt(p, 7), _ => 0
            };
            int endLane = p[0] switch {
                "SLD" or "SLC" or "SXC" or "SXD" => IntAt(p, 6, lane),
                "ASC" or "ASD" => IntAt(p, 8, lane), _ => lane
            };
            int endWidth = p[0] switch {
                "SLD" or "SLC" or "SXC" or "SXD" => IntAt(p, 7, width),
                "ASC" or "ASD" => IntAt(p, 9, width), _ => width
            };
            AirDirection direction = p[0] switch {
                "AIR" => AirDirection.Up, "AUL" => AirDirection.UpLeft, "AUR" => AirDirection.UpRight,
                "ADR" => AirDirection.DownRight, "ADL" => AirDirection.DownLeft, "ADW" => AirDirection.Down,
                _ => AirDirection.None
            };
            float airHeight = p[0] is "ASC" or "ASD" ? FloatAt(p, 6, 5) : 5;
            float endAirHeight = p[0] is "ASC" or "ASD" ? FloatAt(p, 10, airHeight) : airHeight;
            // HXD stores its EX-head style/direction after the duration.
            string source = p[0] == "HXD" ? StringAt(p, 6) : StringAt(p, 5);
            // SLD/SXD may own a slide cap. SLC/SXC only shape the curve;
            // ResolveSegmentVisibility removes duplicate caps when an owning
            // record is connected to the previous segment.
            bool visibleHead = p[0] is not ("AHD" or "AHX");
            return new RawNote(kind, tick, Math.Max(0, duration), lane, width, endLane, endWidth,
                direction, airHeight, endAirHeight, visibleHead, p[0], source);
        }

        private static int[] AllocateJudgePoints(List<RawNote> notes, Dictionary<string, int> totals,
            TickTimeMapper time)
        {
            var result = new int[notes.Count];
            for (int i = 0; i < notes.Count; i++)
                result[i] = notes[i].ExplicitJudgeTicks?.Count ?? 0;
            void Distribute(List<int> indices, int target, HashSet<int> mandatory, Func<RawNote, double> weight)
            {
                foreach (int i in mandatory) result[i]++;
                int remaining = Math.Max(0, target - mandatory.Count);
                if (remaining == 0 || indices.Count == 0) return;
                double sum = indices.Sum(i => Math.Max(1.0, weight(notes[i])));
                var fractions = new List<(int index, double fraction)>();
                int assigned = 0;
                foreach (int i in indices) {
                    double exact = remaining * Math.Max(1.0, weight(notes[i])) / sum;
                    int whole = (int)Math.Floor(exact); result[i] += whole; assigned += whole;
                    fractions.Add((i, exact - whole));
                }
                foreach (var item in fractions.OrderByDescending(x => x.fraction).Take(remaining - assigned)) result[item.index]++;
            }
            List<int> Where(Func<RawNote, bool> f) => Enumerable.Range(0, notes.Count).Where(i => f(notes[i])).ToList();
            int Total(string key, int fallback) => totals.TryGetValue(key, out int value) ? value : fallback;
            double DurationMs(RawNote note) => Math.Max(0,
                time.ToMs(note.Tick + note.Duration) - time.ToMs(note.Tick));

            // The arcade TAP total includes MNE checkpoints. MNE still has an
            // independent hazard checker at runtime, but its safe crossing is
            // a JC and its hit is a MISS, so it belongs to the combo total.
            var taps = Where(n => n.VisibleHead &&
                (n.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Mine or NoteKind.Hold or NoteKind.Slide));
            Distribute(taps, Total("TAP", taps.Count), taps.ToHashSet(), n => n.Width);
            var flicks = Where(n => n.Kind == NoteKind.Flick);
            Distribute(flicks, Total("FLK", flicks.Count), flicks.ToHashSet(), n => n.Width);
            // HLD and SLD judge points belong to the complete continuous
            // object, not to each exported fragment independently. Allocating
            // each fragment separately moves points at SLC/SXC seams and is
            // the source of several long-note timing/display mismatches.
            AllocateLongJudgeTicks(notes, totals, result, time);
            var ordinaryAirHeads = Where(n => n.Kind == NoteKind.Air && n.VisibleHead);
            foreach (int i in ordinaryAirHeads) result[i]++;
            var airLongHeads = Enumerable.Range(0, notes.Count).Where(i =>
                (notes[i].Kind == NoteKind.AirAction && notes[i].VisibleHead) ||
                (notes[i].Kind == NoteKind.AirHold && !IsHeadlessAirHoldContinuation(notes[i], i, notes)))
                .ToList();
            foreach (int i in airLongHeads) result[i]++;

            // ALD and ASD_NODE are discrete authored AIR planes already counted
            // in airLongHeads. Retain body points for sustained AIR-HOLD and
            // AIR-SLIDE sections after reserving the discrete endpoint planes.
            // The remaining AIR-SLIDE KEEP cadence is still a legacy allocator;
            // geometry flags cannot replace those sustained checkpoints.
            var airBodies = Where(n => n.OriginalType is "AHD" or "AHX" or "ASD");
            int airTarget = Total("AIR", ordinaryAirHeads.Count + airLongHeads.Count + airBodies.Count);
            int bodyTarget = Math.Max(0, airTarget - ordinaryAirHeads.Count - airLongHeads.Count);
            var mandatoryAirBodies = airBodies.Where(i => notes[i].OriginalType != "ASD")
                .Take(bodyTarget).ToHashSet();
            Distribute(airBodies, bodyTarget, mandatoryAirBodies, DurationMs);

            return result;
        }

        private sealed class LongJudgeChain
        {
            public readonly List<int> Members = new List<int>();
            public long StartTick;
            public long EndTick;
            public NoteKind Kind;
            public int BodyPoints;
        }

        private static void AllocateLongJudgeTicks(IReadOnlyList<RawNote> notes,
            Dictionary<string, int> totals, int[] result, TickTimeMapper time)
        {
            var chainByIndex = Enumerable.Repeat(-1, notes.Count).ToArray();
            var chains = new List<LongJudgeChain>();
            var endings = new Dictionary<long, List<int>>();

            for (int i = 0; i < notes.Count; i++) {
                RawNote note = notes[i];
                if (!IsJudgeableLong(note)) continue;
                int owner = -1;
                if (endings.TryGetValue(note.Tick, out var candidates))
                    for (int c = candidates.Count - 1; c >= 0; c--) {
                        int candidate = candidates[c];
                        if (IsContinuousSegment(notes[candidate], note)) {
                            owner = candidate;
                            break;
                        }
                    }

                int chainIndex;
                if (owner >= 0) {
                    chainIndex = chainByIndex[owner];
                } else {
                    chainIndex = chains.Count;
                    chains.Add(new LongJudgeChain {
                        StartTick = note.Tick,
                        EndTick = note.Tick + note.Duration,
                        Kind = note.Kind
                    });
                }
                chainByIndex[i] = chainIndex;
                LongJudgeChain chain = chains[chainIndex];
                chain.Members.Add(i);
                chain.StartTick = Math.Min(chain.StartTick, note.Tick);
                chain.EndTick = Math.Max(chain.EndTick, note.Tick + note.Duration);
                if (!endings.TryGetValue(note.Tick + note.Duration, out var list))
                    endings[note.Tick + note.Duration] = list = new List<int>();
                list.Add(i);
            }

            AllocateLongKind(NoteKind.Hold, "HLD");
            AllocateLongKind(NoteKind.Slide, "SLD");

            void AllocateLongKind(NoteKind kind, string totalKey)
            {
                var selected = Enumerable.Range(0, chains.Count)
                    .Where(i => chains[i].Kind == kind && chains[i].Members.Count > 0)
                    .ToList();
                if (selected.Count == 0) return;
                int target = totals.TryGetValue(totalKey, out int value) ? Math.Max(0, value) : 0;
                double Weight(int chainIndex) => Math.Max(.001,
                    time.ToMs(chains[chainIndex].EndTick) - time.ToMs(chains[chainIndex].StartTick));
                // KEEP density follows elapsed audio time. Raw tick length is
                // wrong across BPM changes: equal tick spans at 120 and 240 BPM
                // must not receive the same number of checkpoints.
                double totalWeight = selected.Sum(Weight);
                var fractions = new List<(int chain, double fraction)>();
                int assigned = 0;
                foreach (int chainIndex in selected) {
                    double exact = target * Weight(chainIndex) / totalWeight;
                    int whole = (int)Math.Floor(exact);
                    chains[chainIndex].BodyPoints = whole;
                    assigned += whole;
                    fractions.Add((chainIndex, exact - whole));
                }
                foreach (var item in fractions.OrderByDescending(x => x.fraction).Take(target - assigned))
                    chains[item.chain].BodyPoints++;

                foreach (int chainIndex in selected)
                    AssignChainPoints(chains[chainIndex], notes, result);
            }
        }

        private static bool IsJudgeableLong(RawNote note) =>
            note.Duration > 0 && (note.Kind is NoteKind.Hold or NoteKind.Slide);

        private static void AssignChainPoints(LongJudgeChain chain, IReadOnlyList<RawNote> notes, int[] result)
        {
            var pointsByNote = new Dictionary<int, List<long>>();
            foreach (int index in chain.Members) pointsByNote[index] = new List<long>();

            bool hasHead = chain.Members.Any(index => notes[index].VisibleHead);
            int bodyCount = chain.BodyPoints;
            for (int point = 0; point < bodyCount; point++) {
                double fraction = hasHead
                    ? (point + 1d) / (bodyCount + 1d)
                    : bodyCount == 0 ? 0 : point / (double)bodyCount;
                long tick = chain.StartTick + (long)Math.Round(
                    (chain.EndTick - chain.StartTick) * fraction,
                    MidpointRounding.AwayFromZero);
                int owner = chain.Members[chain.Members.Count - 1];
                foreach (int index in chain.Members) {
                    RawNote note = notes[index];
                    if (tick < note.Tick + note.Duration) { owner = index; break; }
                }
                pointsByNote[owner].Add(tick);
            }

            foreach (int index in chain.Members) {
                RawNote note = notes[index];
                var points = pointsByNote[index];
                // TAP allocation may give a wide/special head more than one
                // same-time judge point. Preserve every one in the explicit
                // list; adding only a single head used to drop points when a
                // chain also had KEEP checkpoints.
                int headPoints = note.VisibleHead ? Math.Max(0, result[index]) : 0;
                for (int head = 0; head < headPoints; head++) points.Add(note.Tick);
                points.Sort();
                note.ExplicitJudgeTicks = points;
                // The head is already allocated by T_JUDGE_TAP/ALL. Only add
                // the continuous-body points here; replacing the value would
                // erase the head contribution for the first chain segment.
                result[index] += Math.Max(0, points.Count - headPoints);
            }
        }

        private sealed class TickTimeMapper
        {
            private readonly long resolution;
            private readonly List<RawBpm> bpms;
            public TickTimeMapper(long resolution, List<RawBpm> bpms) { this.resolution = resolution; this.bpms = bpms; }
            public double ToMs(long target)
            {
                double ms = 0; long cursor = 0; double bpm = bpms[0].Bpm;
                foreach (var change in bpms.Skip(1)) {
                    if (change.Tick >= target) break;
                    // C2S RESOLUTION spans one authored measure and the original
                    // runtime advances that measure as four BPM quarter notes.
                    // MET records alter beat-line/chart presentation, not this
                    // absolute audio/judgement clock. This was cross-checked on
                    // 2,227 extracted songs, including non-4/4 MET records.
                    ms += (change.Tick - cursor) * 60000d * 4d / (bpm * resolution);
                    cursor = change.Tick; bpm = change.Bpm;
                }
                return ms + (target - cursor) * 60000d * 4d / (bpm * resolution);
            }
        }

        private static long AbsoluteTick(string measure, string tick, long resolution) =>
            ParseLong(measure) * resolution + ParseLong(tick);
        private static string StringAt(string[] p, int i, string fallback = "") => i < p.Length && p[i].Length > 0 ? p[i] : fallback;
        private static long LongAt(string[] p, int i, long fallback = 0) => i < p.Length && long.TryParse(p[i], out var v) ? v : fallback;
        private static int IntAt(string[] p, int i, int fallback = 0) => i < p.Length && int.TryParse(p[i], out var v) ? v : fallback;
        private static double DoubleAt(string[] p, int i, double fallback = 0) => i < p.Length && double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        private static float FloatAt(string[] p, int i, float fallback = 0) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        private static long ParseLong(string text) => long.TryParse(text, out var value) ? value : 0;
    }
}
