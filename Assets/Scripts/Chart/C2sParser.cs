using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ChunithmMobile.Chart
{
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
            public string OriginalType, SourceType, AirLineColor, AirLadderSecondaryColor;
            public int AirLadderStyle;
            public List<long> ExplicitJudgeTicks;
            public RawNote(NoteKind kind, long tick, long duration, int lane, int width, int endLane, int endWidth,
                AirDirection direction, float airHeight, float endAirHeight, bool visibleHead,
                string originalType, string sourceType, string airLineColor,
                int airLadderStyle, string airLadderSecondaryColor)
            { Kind = kind; Tick = tick; Duration = duration; Lane = lane; Width = width; EndLane = endLane; EndWidth = endWidth;
              Direction = direction; AirHeight = airHeight; EndAirHeight = endAirHeight; VisibleHead = visibleHead;
              OriginalType = originalType; SourceType = sourceType; AirLineColor = airLineColor;
              AirLadderStyle = airLadderStyle; AirLadderSecondaryColor = airLadderSecondaryColor; }
        }

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
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                    default:
                        if (ParseNote(p, resolution) is { } note) rawNotes.Add(note);
                        break;
                }
            }

            // C2S exporters normally write note records chronologically, but
            // that is not part of the file contract. Continuation ownership
            // (HLD/SLD/AIR-HOLD sections) is temporal, so normalize note order
            // before resolving visibility and allocating judge points. Keep
            // command ordering for BPM/scroll records unchanged.
            rawNotes = rawNotes.OrderBy(note => note.Tick).ToList();

            if (bpms.All(x => x.Tick != 0)) bpms.Add(new RawBpm(0, defaultBpm));
            bpms.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            var time = new TickTimeMapper(resolution, bpms);
            ResolveSegmentVisibility(rawNotes);
            bool[] slideCheckpoints = ResolveSlideCheckpoints(rawNotes);
            var judgePoints = AllocateJudgePoints(rawNotes, totals, time);
            var notesAt = rawNotes.GroupBy(n => n.Tick).ToDictionary(g => g.Key, g => g.ToList());
            var visualHeads = ResolveSlideVisualHeads(rawNotes, notesAt);
            ResolveAirHoldVisualHeads(rawNotes, visualHeads);
            ResolveAirActionVisualHeads(rawNotes, visualHeads);
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
                    VisibleTail = n.Duration > 0 && !continuation && n.Kind != NoteKind.AirLadder && n.Kind != NoteKind.AirLine,
                    OriginalType = n.OriginalType, SourceType = n.SourceType,
                    AirLineColor = n.AirLineColor, AirLadderStyle = n.AirLadderStyle,
                    AirLadderSecondaryColor = n.AirLadderSecondaryColor,
                    JudgePointCount = judgePoints[i],
                    ExplicitJudgeTimesMs = n.ExplicitJudgeTicks?.Select(time.ToMs).ToList()
                };
            }).OrderBy(n => n.TimeMs).ToList();
            ResolveLongVisualChains(notes, rawNotes);

            double latestNoteMs = notes.Count == 0 ? 0 : notes.Max(n => Math.Max(n.TimeMs, n.EndTimeMs));
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
            int total = totals.TryGetValue("ALL", out var all) ? all : notes.Sum(n => n.JudgePointCount);
            return new ParsedChart(notes, duration, bpms[0].Bpm, total, mappedScrolls, mappedProfiles, mappedAssignments);
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

        private static void ResolveSegmentVisibility(IReadOnlyList<RawNote> notes)
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
            // ALD is a decorative overlay and AHD/AHX are AIR-HOLD body
            // records. SLD owns a slide head; SLC/SXC/SXD are its continuous
            // body sections. HXD is a standalone EX HOLD and owns its head.
            return note.OriginalType is not
                ("ALD" or "AHD" or "AHX" or "SLC" or "SXC" or "SXD");
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
            if (previous == null || current == null || previous.Duration <= 0 ||
                previous.Kind is NoteKind.AirLadder or NoteKind.AirLine ||
                current.Kind is NoteKind.AirLadder or NoteKind.AirLine)
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
                    if (previous.Kind != NoteKind.AirHold ||
                        previous.Tick + previous.Duration != note.Tick) continue;
                    int previousEnd = previous.EndLane + Math.Max(1, previous.EndWidth);
                    int currentEnd = note.Lane + Math.Max(1, note.Width);
                    if (previous.EndLane < currentEnd && note.Lane < previousEnd) {
                        hasPrevious = true;
                        break;
                    }
                }
                if (!hasPrevious) visualHeads[i] = true;
            }
        }

        private static void ResolveAirActionVisualHeads(IReadOnlyList<RawNote> notes, bool[] visualHeads)
        {
            // ASD carries an authored action cap even inside a connected
            // trail. ASC only shapes the curve unless it owns a starting head.
            // Visual nodes must not introduce extra scoring heads.
            for (int i = 0; i < notes.Count; i++) {
                RawNote note = notes[i];
                if (note.Kind != NoteKind.AirAction) continue;
                visualHeads[i] = note.OriginalType == "ASD" || note.VisibleHead;
            }
        }

        private static RawNote ParseNote(string[] p, long resolution)
        {
            if (p.Length < 5) return null;
            int airLadderInterval = p[0] == "ALD" ? Math.Max(0, IntAt(p, 5)) : 0;
            var kind = p[0] switch {
                "TAP" => NoteKind.Tap, "CHR" => NoteKind.ExTap, "MNE" => NoteKind.Mine,
                "FLK" => NoteKind.Flick, "HLD" or "HXD" => NoteKind.Hold,
                "SLD" or "SLC" or "SXC" or "SXD" => NoteKind.Slide,
                "AIR" or "AUL" or "AUR" or "ADR" or "ADL" or "ADW" => NoteKind.Air,
                "AHD" or "AHX" => NoteKind.AirHold, "ASC" or "ASD" => NoteKind.AirAction,
                "ALD" => airLadderInterval > 0 ? NoteKind.AirLadder : NoteKind.AirLine,
                _ => (NoteKind)(-1)
            };
            if ((int)kind < 0) return null;
            long tick = AbsoluteTick(p[1], p[2], resolution);
            int lane = IntAt(p, 3), width = IntAt(p, 4);
            long duration = p[0] switch {
                "HLD" or "HXD" or "SLD" or "SLC" or "SXC" or "SXD" => LongAt(p, 5),
                "AHD" or "AHX" => LongAt(p, 6), "ASC" or "ASD" or "ALD" => LongAt(p, 7), _ => 0
            };
            int endLane = p[0] switch {
                "SLD" or "SLC" or "SXC" or "SXD" => IntAt(p, 6, lane),
                "ASC" or "ASD" or "ALD" => IntAt(p, 8, lane), _ => lane
            };
            int endWidth = p[0] switch {
                "SLD" or "SLC" or "SXC" or "SXD" => IntAt(p, 7, width),
                "ASC" or "ASD" or "ALD" => IntAt(p, 9, width), _ => width
            };
            AirDirection direction = p[0] switch {
                "AIR" => AirDirection.Up, "AUL" => AirDirection.UpLeft, "AUR" => AirDirection.UpRight,
                "ADR" => AirDirection.DownRight, "ADL" => AirDirection.DownLeft, "ADW" => AirDirection.Down,
                "ASD" => AirDirection.Down, "ASC" => AirDirection.Up,
                _ => AirDirection.None
            };
            float airHeight = p[0] is "ASC" or "ASD" or "ALD" ? FloatAt(p, 6, 5) : 5;
            float endAirHeight = p[0] is "ASC" or "ASD" or "ALD" ? FloatAt(p, 10, airHeight) : airHeight;
            // HXD stores its EX-head style/direction after the duration.
            string source = p[0] == "HXD" ? StringAt(p, 6) : StringAt(p, 5);
            // SLD owns a slide head. SLC/SXC/SXD are continuous body records;
            // ResolveSegmentVisibility keeps them headless even when a chain
            // is represented by many short authored sections.
            bool visibleHead = p[0] is not ("ALD" or "AHD" or "AHX");
            string color = p[0] == "ALD" ? StringAt(p, 11, "DEF") : "DEF";
            int ladderStyle = airLadderInterval;
            string ladderSecondaryColor = p[0] == "ALD" ? StringAt(p, 12, "DEF") : "DEF";
            return new RawNote(kind, tick, Math.Max(0, duration), lane, width, endLane, endWidth,
                direction, airHeight, endAirHeight, visibleHead, p[0], source, color,
                ladderStyle, ladderSecondaryColor);
        }

        private static int[] AllocateJudgePoints(List<RawNote> notes, Dictionary<string, int> totals,
            TickTimeMapper time)
        {
            var result = new int[notes.Count];
            AllocateAirLadderJudgeTicks(notes);
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

            // The arcade TAP total includes MNE checkpoints. MNE still has an
            // independent hazard checker at runtime, but its safe crossing is
            // a JC and its hit is a MISS, so it belongs to the combo total.
            var taps = Where(n => n.VisibleHead &&
                (n.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Mine or NoteKind.Hold or NoteKind.Slide));
            Distribute(taps, Total("TAP", taps.Count), taps.ToHashSet(), n => n.Width);
            var flicks = Where(n => n.Kind == NoteKind.Flick);
            Distribute(flicks, Total("FLK", flicks.Count), flicks.ToHashSet(), n => n.Width);
            var holds = Where(n => (n.Kind is NoteKind.Hold or NoteKind.HeavenHold) && n.Duration > 0);
            double DurationMs(RawNote note) => Math.Max(0,
                time.ToMs(note.Tick + note.Duration) - time.ToMs(note.Tick));
            Distribute(holds, Total("HLD", 0), new HashSet<int>(), DurationMs);
            var slides = Where(n => n.Kind == NoteKind.Slide && n.Duration > 0);
            Distribute(slides, Total("SLD", 0), new HashSet<int>(), DurationMs);
            var ordinaryAirHeads = Where(n => n.Kind == NoteKind.Air && n.VisibleHead);
            foreach (int i in ordinaryAirHeads) result[i]++;
            var airLongHeads = Enumerable.Range(0, notes.Count).Where(i =>
                (notes[i].Kind == NoteKind.AirAction && notes[i].VisibleHead) ||
                (notes[i].Kind == NoteKind.AirHold && !IsHeadlessAirHoldContinuation(notes[i], i, notes)))
                .ToList();
            foreach (int i in airLongHeads) result[i]++;

            // ASC is curve geometry. AHD/AHX and ASD are checkpoint-bearing
            // records; spreading AIR totals over ASC moves judgments away
            // from the authored action planes.
            var airBodies = Where(n => n.OriginalType is "AHD" or "AHX" or "ASD");
            int ladderPoints = notes.Sum(n => n.ExplicitJudgeTicks?.Count ?? 0);
            int airTarget = Total("AIR",
                ordinaryAirHeads.Count + airLongHeads.Count + airBodies.Count + ladderPoints);
            int bodyTarget = Math.Max(0,
                airTarget - ordinaryAirHeads.Count - airLongHeads.Count - ladderPoints);
            Distribute(airBodies, bodyTarget, airBodies.ToHashSet(), DurationMs);

            if (totals.TryGetValue("ALL", out int expected)) {
                int delta = expected - result.Sum();
                var adjustable = Enumerable.Range(0, notes.Count)
                    .Where(i => notes[i].Kind is not (NoteKind.AirLadder or NoteKind.AirLine))
                    .OrderByDescending(i => notes[i].Duration).ToList();
                if (delta > 0 && adjustable.Count > 0) result[adjustable[0]] += delta;
                else if (delta < 0) foreach (int i in adjustable.OrderByDescending(i => result[i])) {
                    int remove = Math.Min(result[i], -delta); result[i] -= remove; delta += remove; if (delta == 0) break;
                }
            }
            return result;
        }

        private sealed class AirLadderChain
        {
            public int HeadIndex;
            public long StartTick;
            public long EndTick;
            public int Interval;
        }

        private readonly struct AirLadderEndpoint : IEquatable<AirLadderEndpoint>
        {
            private readonly long tick;
            private readonly int lane;
            private readonly int width;
            private readonly int heightBits;
            private readonly int interval;

            public AirLadderEndpoint(long tick, int lane, int width, float height, int interval)
            {
                this.tick = tick;
                this.lane = lane;
                this.width = width;
                heightBits = BitConverter.SingleToInt32Bits(height);
                this.interval = interval;
            }

            public bool Equals(AirLadderEndpoint other) =>
                tick == other.tick && lane == other.lane && width == other.width &&
                heightBits == other.heightBits && interval == other.interval;

            public override bool Equals(object obj) => obj is AirLadderEndpoint other && Equals(other);

            public override int GetHashCode()
            {
                unchecked {
                    int hash = tick.GetHashCode();
                    hash = hash * 397 ^ lane;
                    hash = hash * 397 ^ width;
                    hash = hash * 397 ^ heightBits;
                    return hash * 397 ^ interval;
                }
            }
        }

        private static void AllocateAirLadderJudgeTicks(IReadOnlyList<RawNote> notes)
        {
            var chains = new List<AirLadderChain>();
            var endpoints = new Dictionary<AirLadderEndpoint, Queue<int>>();
            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                RawNote note = notes[noteIndex];
                if (note.Kind != NoteKind.AirLadder || note.AirLadderStyle <= 0) continue;

                var start = new AirLadderEndpoint(note.Tick, note.Lane, note.Width,
                    note.AirHeight, note.AirLadderStyle);
                int chainIndex = -1;
                if (endpoints.TryGetValue(start, out Queue<int> candidates) && candidates.Count > 0)
                    chainIndex = candidates.Dequeue();

                if (chainIndex < 0)
                {
                    chainIndex = chains.Count;
                    chains.Add(new AirLadderChain {
                        HeadIndex = noteIndex,
                        StartTick = note.Tick,
                        EndTick = note.Tick + note.Duration,
                        Interval = note.AirLadderStyle
                    });
                }
                else
                {
                    chains[chainIndex].EndTick = note.Tick + note.Duration;
                }

                var end = new AirLadderEndpoint(note.Tick + note.Duration,
                    note.EndLane, note.EndWidth, note.EndAirHeight, note.AirLadderStyle);
                if (!endpoints.TryGetValue(end, out Queue<int> owners))
                {
                    owners = new Queue<int>();
                    endpoints.Add(end, owners);
                }
                owners.Enqueue(chainIndex);
            }

            foreach (AirLadderChain chain in chains)
            {
                var ticks = new List<long>();
                long tick = chain.StartTick;
                while (tick <= chain.EndTick)
                {
                    ticks.Add(tick);
                    if (tick > long.MaxValue - chain.Interval) break;
                    tick += chain.Interval;
                }
                notes[chain.HeadIndex].ExplicitJudgeTicks = ticks;
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
