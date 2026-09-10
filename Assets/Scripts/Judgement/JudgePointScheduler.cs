using System;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;

namespace ChunithmMobile.Judgement
{
    public readonly struct ScheduledJudgePoint
    {
        public readonly int NoteIndex;
        public readonly int PointIndex;
        public readonly double TimeMs;
        public readonly ushort GroundLaneMask;
        public readonly bool IsScoring;
        private readonly double[] groundEarlyProtectionMs;
        private readonly double[] groundLateProtectionMs;

        public bool UsesGroundProtection => GroundLaneMask != 0;

        public ScheduledJudgePoint(int noteIndex, int pointIndex, double timeMs,
            ushort groundLaneMask = 0, double[] earlyProtectionMs = null,
            double[] lateProtectionMs = null, bool isScoring = true)
        {
            NoteIndex = noteIndex;
            PointIndex = pointIndex;
            TimeMs = timeMs;
            GroundLaneMask = groundLaneMask;
            IsScoring = isScoring;
            groundEarlyProtectionMs = earlyProtectionMs;
            groundLateProtectionMs = lateProtectionMs;
        }

        public bool AcceptsGroundLane(int lane, double inputChartTimeMs)
        {
            if ((uint)lane >= ArcadeTouchInput.LaneCount ||
                (GroundLaneMask & (1 << lane)) == 0) return false;
            double early = groundEarlyProtectionMs?[lane] ?? double.NegativeInfinity;
            double late = groundLateProtectionMs?[lane] ?? double.PositiveInfinity;
            // The later candidate owns an exact shared boundary. This ensures
            // one lane edge cannot resolve both sides of a dense-note pair.
            return inputChartTimeMs >= early && inputChartTimeMs < late;
        }

        internal void TightenLateProtection(int lane, double boundaryMs)
        {
            if (groundLateProtectionMs == null || (uint)lane >= (uint)groundLateProtectionMs.Length) return;
            groundLateProtectionMs[lane] = Math.Min(groundLateProtectionMs[lane], boundaryMs);
        }

        internal void TightenEarlyProtection(int lane, double boundaryMs)
        {
            if (groundEarlyProtectionMs == null || (uint)lane >= (uint)groundEarlyProtectionMs.Length) return;
            groundEarlyProtectionMs[lane] = Math.Max(groundEarlyProtectionMs[lane], boundaryMs);
        }
    }

    /// <summary>
    /// Expands the original per-type judge totals into deterministic runtime
    /// checkpoints. Long notes deliberately have no endpoint checkpoint: the
    /// last point is always before EndTimeMs, so holding past the tail is safe.
    /// Every checkpoint remains independent, allowing a MISS to reconnect.
    /// </summary>
    public static class JudgePointScheduler
    {
        public static List<ScheduledJudgePoint> Build(ParsedChart chart)
        {
            var result = new List<ScheduledJudgePoint>(Math.Max(0, chart.OriginalJudgeTotal));
            for (int noteIndex = 0; noteIndex < chart.Notes.Count; noteIndex++) {
                ChartNote note = chart.Notes[noteIndex];
                int count = Math.Max(0, note.JudgePointCount);
                if (note.Kind == NoteKind.AirLine) continue;
                if (note.ExplicitJudgeTimesMs is { Count: > 0 }) {
                    for (int pointIndex = 0; pointIndex < note.ExplicitJudgeTimesMs.Count; pointIndex++)
                        result.Add(new ScheduledJudgePoint(noteIndex, pointIndex,
                            note.ExplicitJudgeTimesMs[pointIndex]));
                    continue;
                }
                if (note.Kind == NoteKind.AirLadder) continue;
                // Mines are runtime hazards with their own checker, but their
                // authored TAP-category checkpoint count is still authoritative.
                if (count == 0 && note.Kind != NoteKind.Mine) continue;

                // MNE is checked by its own hazard path, but the original
                // T_JUDGE_TAP/T_JUDGE_ALL totals include its safe JC or MISS.
                // Only an active non-scoring MOD suppresses it at Resolve().
                bool isScoring = true;

                double duration = Math.Max(0, note.EndTimeMs - note.TimeMs);
                if (duration <= .001 || count == 1) {
                    // A wide/chord-like authored record may contribute more than
                    // one original judge point at the same timestamp. Never
                    // collapse those points or the chart combo total changes.
                    for (int pointIndex = 0; pointIndex < count; pointIndex++)
                        result.Add(new ScheduledJudgePoint(noteIndex, pointIndex, note.TimeMs,
                            isScoring: isScoring));
                    continue;
                }

                for (int pointIndex = 0; pointIndex < count; pointIndex++) {
                    // A true continuation section samples from its cell
                    // centres to avoid judging the shared segment seam twice.
                    // A standalone headless AIR-HOLD record still starts at
                    // its authored time; otherwise its first hit was moved to
                    // the middle of the hold.
                    double fraction = note.VisibleHead || !note.IsContinuation
                        ? (double)pointIndex / count
                        : (pointIndex + .5) / count;
                    double checkpointTime = note.TimeMs + duration * fraction;
                    if (pointIndex > 0 || note.IsContinuation)
                        checkpointTime += JudgementEngine.Frames(JudgementEngine.KeepOffsetFrames);
                    result.Add(new ScheduledJudgePoint(noteIndex, pointIndex,
                        checkpointTime, isScoring: isScoring));
                }
            }
            result.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
            ApplyGroundProtection(chart, result);
            return result;
        }

        private static void ApplyGroundProtection(ParsedChart chart, List<ScheduledJudgePoint> points)
        {
            var candidates = new List<int>();
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) {
                ScheduledJudgePoint source = points[pointIndex];
                ChartNote note = chart.Notes[source.NoteIndex];
                if (IsGroundHeadCandidate(note, source.PointIndex)) candidates.Add(pointIndex);
            }

            foreach (int pointIndex in candidates) {
                ScheduledJudgePoint source = points[pointIndex];
                ChartNote note = chart.Notes[source.NoteIndex];
                if (!UsesMinimalizedWindow(note)) continue;

                ushort laneMask = BuildLaneMask(note.Lane, note.Width);
                if (laneMask == 0) continue;
                var early = CreateBoundaryArray(double.NegativeInfinity);
                var late = CreateBoundaryArray(double.PositiveInfinity);
                points[pointIndex] = new ScheduledJudgePoint(source.NoteIndex, source.PointIndex,
                    source.TimeMs, laneMask, early, late, source.IsScoring);

                for (int lane = 0; lane < ArcadeTouchInput.LaneCount; lane++) {
                    if ((laneMask & (1 << lane)) == 0) continue;
                    double previousTime = double.NegativeInfinity;
                    double nextTime = double.PositiveInfinity;
                    foreach (int candidateIndex in candidates) {
                        if (candidateIndex == pointIndex) continue;
                        ScheduledJudgePoint candidate = points[candidateIndex];
                        ChartNote other = chart.Notes[candidate.NoteIndex];
                        if (!OverlapsLane(other, lane)) continue;
                        // MINIMALIZE belongs to the candidate's own checker.
                        // ExTAP and MINE have no density-shortened window and
                        // therefore must not steal a TAP/FLICK boundary.
                        if (!UsesMinimalizedWindow(other)) continue;
                        if (candidate.TimeMs < source.TimeMs - .001)
                            previousTime = Math.Max(previousTime, candidate.TimeMs);
                        else if (candidate.TimeMs > source.TimeMs + .001)
                            nextTime = Math.Min(nextTime, candidate.TimeMs);
                    }
                    // The original protection records clip each side and lane
                    // independently. A close predecessor must not shorten the
                    // late side, and a close successor must not shorten early.
                    // Use the source checker's outer window here. Applying one
                    // TAP.EASY six-frame window to every note made dense
                    // SLIDE/FLICK groups steal more time than their checker has.
                    double earlyWindow = GroundProtectionEarlyFrames(note);
                    if (!double.IsNegativeInfinity(previousTime))
                        early[lane] = Math.Max(source.TimeMs - JudgementEngine.Frames(earlyWindow),
                            (previousTime + source.TimeMs) * .5);
                    if (!double.IsPositiveInfinity(nextTime))
                        // Keep the later note's ownership boundary at the
                        // temporal midpoint. The normal TAP window is applied
                        // by JudgeGround; clamping this protection edge to
                        // source + the protection window incorrectly rejects the middle
                        // note before the next note can own the lane. JudgeGround
                        // applies the source note's own outer window separately.
                        late[lane] = (source.TimeMs + nextTime) * .5;
                }
            }
        }

        private static double GroundProtectionEarlyFrames(ChartNote note)
        {
            // FLICK.TAP and ordinary TAP use a five-frame hard-profile outer
            // capture window. SLIDE protects its wider trigger window while
            // JudgeGround still applies its narrower three-frame area grade.
            if (note.Kind == NoteKind.Slide) return JudgementEngine.SlideTriggerFrames;
            if (note.Kind == NoteKind.Flick) return JudgementEngine.FlickWindowMs / JudgementEngine.ReferenceFrameMs;
            return JudgementEngine.TapHardAttackFrames;
        }

        private static bool OverlapsLane(ChartNote note, int lane)
        {
            return lane >= note.Lane && lane < note.Lane + Math.Max(1, note.Width);
        }

        private static bool IsGroundHeadCandidate(ChartNote note, int pointIndex) =>
            pointIndex == 0 && note.VisibleHead && !note.IsSlideCheckpoint &&
            (note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide);

        private static bool UsesMinimalizedWindow(ChartNote note) =>
            !string.Equals(note.OriginalType, "HXD", StringComparison.OrdinalIgnoreCase) &&
            note.Kind is NoteKind.Tap or NoteKind.Flick or NoteKind.Hold or NoteKind.Slide;

        private static ushort BuildLaneMask(int firstLane, int width)
        {
            int first = Math.Max(0, firstLane);
            int last = Math.Min(ArcadeTouchInput.LaneCount, firstLane + Math.Max(1, width));
            ushort mask = 0;
            for (int lane = first; lane < last; lane++) mask |= (ushort)(1 << lane);
            return mask;
        }

        private static double[] CreateBoundaryArray(double value)
        {
            var result = new double[ArcadeTouchInput.LaneCount];
            for (int lane = 0; lane < result.Length; lane++) result[lane] = value;
            return result;
        }
    }
}
