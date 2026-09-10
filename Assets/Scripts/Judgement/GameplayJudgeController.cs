using System;
using System.Collections.Generic;
using System.Linq;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Judgement
{
    public sealed class GameplayScoreState
    {
        public int JusticeCritical { get; private set; }
        public int Justice { get; private set; }
        public int Attack { get; private set; }
        public int Miss { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Resolved => JusticeCritical + Justice + Attack + Miss;

        public int Score(int total)
        {
            if (total <= 0) return 0;
            double baseScore = 1_000_000.0 * (JusticeCritical + Justice + Attack * .5) / total;
            double criticalBonus = 10_000.0 * JusticeCritical / total;
            return Mathf.Clamp((int)Math.Round(baseScore + criticalBonus), 0, 1_010_000);
        }

        public void Add(Chart.Judgement judgement)
        {
            switch (judgement) {
                case Chart.Judgement.JusticeCritical: JusticeCritical++; break;
                case Chart.Judgement.Justice: Justice++; break;
                case Chart.Judgement.Attack: Attack++; break;
                default: Miss++; break;
            }
            if (judgement == Chart.Judgement.Miss) Combo = 0;
            else { Combo++; MaxCombo = Math.Max(MaxCombo, Combo); }
        }

        public void Reset()
        {
            JusticeCritical = Justice = Attack = Miss = Combo = MaxCombo = 0;
        }

        public void BreakCombo() => Combo = 0;
    }

    /// <summary>
    /// Runtime judge-point consumer. Every long checkpoint is independent, so a
    /// later checkpoint can reconnect after MISS and there is never a tail judge.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class GameplayJudgeController : MonoBehaviour
    {
        private ParsedChart chart;
        private DspChartClock clock;
        private ArcadeInputRig input;
        private List<ScheduledJudgePoint> points;
        private bool[] resolved;
        private double[] lastKeepCoverageMs;
        private bool[] keepCoveredLastSample;
        private int nextPoint;
        private double lastAirMotionMs = double.NegativeInfinity;
        private float lastAirMotionVelocity;
        private int lastAirMotionDirection;
        private int lastAirMotionSequence;
        private int consumedAirMotionSequence = -1;
        private double consumedAirMotionPointTime = double.NegativeInfinity;
        private double lastAirPresentMs = double.NegativeInfinity;
        private readonly Dictionary<int, GroundFrameCandidate> groundCandidatesByPoint =
            new Dictionary<int, GroundFrameCandidate>();
        private int resolvedCount;
        private int scoringPointCount;
        private bool autoPlay;
        private bool airAreaAuto;
        private bool directionalAirAuto;
        private bool airLongNoMotion;
        private bool nonMissJusticeCritical;
        private bool mineImmunity;
        private int judgementOffsetMs;

        private readonly struct GroundFrameCandidate
        {
            public readonly int PointIndex;
            public readonly JudgementResult Result;
            public readonly double DistanceMs;

            public GroundFrameCandidate(int pointIndex, JudgementResult result, double distanceMs)
            {
                PointIndex = pointIndex;
                Result = result;
                DistanceMs = distanceMs;
            }
        }

        public GameplayScoreState ScoreState { get; } = new GameplayScoreState();
        public event Action<ScheduledJudgePoint, JudgementResult> Judged;

        public bool TryGetNote(int noteIndex, out ChartNote note)
        {
            if (chart != null && noteIndex >= 0 && noteIndex < chart.Notes.Count) {
                note = chart.Notes[noteIndex];
                return true;
            }
            note = null;
            return false;
        }

        public void Bind(ParsedChart parsedChart, DspChartClock chartClock, ArcadeInputRig inputRig,
            bool enableAutoPlay = false, bool enableAirAreaAuto = false,
            bool enableDirectionalAirAuto = false, bool enableMineImmunity = false,
            bool enableAirLongNoMotion = false,
            int offsetMs = 0, bool enableNonMissJusticeCritical = false,
            List<ScheduledJudgePoint> preparedPoints = null)
        {
            chart = parsedChart;
            clock = chartClock;
            input = inputRig;
            input?.ResetCaptureState();
            points = preparedPoints ?? JudgePointScheduler.Build(chart);
            resolved = new bool[points.Count];
            scoringPointCount = points.Count(point => point.IsScoring);
            lastKeepCoverageMs = new double[points.Count];
            keepCoveredLastSample = new bool[points.Count];
            for (int i = 0; i < lastKeepCoverageMs.Length; i++)
                lastKeepCoverageMs[i] = double.NegativeInfinity;
            ScoreState.Reset();
            nextPoint = 0;
            lastAirMotionMs = double.NegativeInfinity;
            lastAirMotionVelocity = 0;
            lastAirMotionDirection = 0;
            lastAirMotionSequence = 0;
            consumedAirMotionSequence = -1;
            consumedAirMotionPointTime = double.NegativeInfinity;
            lastAirPresentMs = double.NegativeInfinity;
            groundCandidatesByPoint.Clear();
            resolvedCount = 0;
            autoPlay = enableAutoPlay;
            airAreaAuto = enableAirAreaAuto;
            directionalAirAuto = enableDirectionalAirAuto;
            airLongNoMotion = enableAirLongNoMotion;
            nonMissJusticeCritical = enableNonMissJusticeCritical;
            mineImmunity = enableMineImmunity;
            judgementOffsetMs = Mathf.Clamp(offsetMs, -500, 500);
            Debug.Log($"Runtime judgement bound: {scoringPointCount} scoring judge points, " +
                $"{points.Count - scoringPointCount} runtime hazard points");
        }

        private void Update()
        {
            if (chart == null || clock == null || input == null || points == null || !clock.Playing || clock.Preparing) return;
            double chartOffset = judgementOffsetMs * clock.Rate;
            double now = clock.TimeMs - chartOffset;
            AirState air = input.Ground.Current.Air;
            if (air.Present || !double.IsNegativeInfinity(air.LastPresentTimeMs))
                lastAirPresentMs = clock.AudioTimeMsToChartTimeMs(air.LastPresentTimeMs) - chartOffset;
            if (!double.IsNegativeInfinity(air.LastMotionTimeMs) &&
                air.MotionSequence != lastAirMotionSequence) {
                lastAirMotionSequence = air.MotionSequence;
                lastAirMotionMs = clock.AudioTimeMsToChartTimeMs(air.LastMotionTimeMs) - chartOffset;
                lastAirMotionVelocity = air.VerticalVelocity;
                lastAirMotionDirection = air.MotionDirection;
            }

            double rate = clock.Rate;
            BuildGroundCandidates();
            // AIR-HOLD owns the widest original input window (36 reference
            // frames). Observe checkpoints across that full interval so its
            // early J/AT ranges are reachable instead of being clipped by the
            // shorter AIR-ACTION look-ahead.
            double lookAheadWindow = Math.Max(JudgementEngine.AirHoldAttackMs,
                Math.Max(JudgementEngine.FlickWindowMs,
                    JudgementEngine.Frames(16.0)));
            double lookAhead = now + lookAheadWindow * rate;
            for (int index = nextPoint; index < points.Count && points[index].TimeMs <= lookAhead; index++) {
                if (resolved[index]) continue;
                ScheduledJudgePoint point = points[index];
                ChartNote note = chart.Notes[point.NoteIndex];

                bool automatic = autoPlay ||
                    (airAreaAuto && note.Kind is (NoteKind.AirHold or NoteKind.AirAction or NoteKind.AirLadder)) ||
                    (directionalAirAuto && note.Kind == NoteKind.Air);
                if (automatic) {
                    // Never fall through to physical input while an AUTO mode owns
                    // this note. Touching early must not resolve or disturb it.
                    if (point.TimeMs <= now)
                        Resolve(index, point, Chart.Judgement.JusticeCritical, 0);
                    continue;
                }

                if (note.Kind == NoteKind.Mine) {
                    if (mineImmunity) {
                        if (point.TimeMs <= now) Resolve(index, point, Chart.Judgement.JusticeCritical, 0, false);
                        continue;
                    }
                    double mineDelta = RealDelta(now, point.TimeMs);
                    // MINE uses IN/OUT for the collision interval. NG is only
                    // the later point at which an untouched hazard is retired;
                    // a late touch must never be able to trigger it.
                    if (JudgementEngine.IsMineTouchWindow(mineDelta) && IsGroundCovered(note)) {
                        Resolve(index, point, Chart.Judgement.Miss, mineDelta, false);
                        continue;
                    }
                    if (JudgementEngine.IsMineCleanupDue(mineDelta))
                        Resolve(index, point, Chart.Judgement.JusticeCritical, 0, false);
                    continue;
                }

                bool continuation = point.PointIndex > 0 || note.IsContinuation || note.IsSlideCheckpoint ||
                    (!note.VisibleHead && note.Kind != NoteKind.AirHold);
                if (continuation && IsLong(note.Kind)) {
                    bool covered = IsCheckpointCovered(note, point.TimeMs, now);
                    // A KEEP checkpoint is a state sample, not a second tap.
                    // When one rendered frame crosses several checkpoints, a
                    // continuously covered panel must still be considered on
                    // time for each checkpoint. Using the late render timestamp
                    // as the grade delta turned held notes into MISS at low FPS.
                    double keepDelta = JudgementEngine.CheckpointCoverageDelta(covered,
                        keepCoveredLastSample[index], lastKeepCoverageMs[index], now,
                        point.TimeMs, rate);
                    keepCoveredLastSample[index] = covered;
                    if (covered) lastKeepCoverageMs[index] = now;
                    bool airHold = note.Kind == NoteKind.AirHold;
                    bool airAction = note.Kind == NoteKind.AirAction;
                    bool airLadder = note.Kind == NoteKind.AirLadder;
                    double keepWindowRealMs = airHold
                        ? JudgementEngine.AirHoldAttackMs
                        : airAction || airLadder ? JudgementEngine.Frames(16.0)
                        : JudgementEngine.Frames(JudgementEngine.KeepAttackFrames);
                    double keepWindow = keepWindowRealMs * rate;
                    // keepDelta is already in real milliseconds; only the
                    // chart-clock cleanup comparison below needs a scaled window.
                    if (point.TimeMs <= now &&
                        (covered || Math.Abs(keepDelta) <= keepWindowRealMs)) {
                        Chart.Judgement checkpoint = airHold
                            ? JudgementEngine.JudgeAirHoldCheckpoint(true, keepDelta)
                            : airAction || airLadder
                                ? JudgementEngine.JudgeAirActionCheckpoint(true, keepDelta)
                            : JudgementEngine.JudgeCheckpoint(true, keepDelta);
                        Resolve(index, point, checkpoint, keepDelta);
                    } else if (point.TimeMs <= now && now - point.TimeMs > keepWindow) {
                        Resolve(index, point, Chart.Judgement.Miss, RealDelta(now, point.TimeMs));
                    }
                    continue;
                }

                if (TryJudgeHead(index, point, note, now, out JudgementResult result)) {
                    Resolve(index, point, result.Grade, result.DeltaMs);
                    continue;
                }

                bool longHead = point.PointIndex == 0 && note.VisibleHead &&
                    note.Kind is (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide);
                double missWindow = longHead
                    ? JudgementEngine.GroundHeadMissWindowMs(note.Kind)
                    : JudgementEngine.MissWindowMs(note);
                if (now - point.TimeMs > missWindow * rate)
                    Resolve(index, point, Chart.Judgement.Miss, RealDelta(now, point.TimeMs));
            }

            while (nextPoint < resolved.Length && resolved[nextPoint]) nextPoint++;
        }

        private void BuildGroundCandidates()
        {
            groundCandidatesByPoint.Clear();
            if (autoPlay || input.Ground.Current.Ground == null || input.Ground.Current.Ground.Count == 0)
                return;

            double rate = clock.Rate;
            double maxWindow = Math.Max(JudgementEngine.FlickWindowMs,
                JudgementEngine.Frames(JudgementEngine.SlideTriggerFrames)) * rate;
            CollectFlickCandidates(rate);
            // The cabinet resolves one current 16-cell panel image. Select one
            // temporal plane for that image first, then allow every covered
            // note on that plane. Choosing one note per finger made a large
            // contact jump between nearby vertical notes and discarded chords.
            double bestPlaneTime = double.PositiveInfinity;
            double bestPlaneDistance = double.PositiveInfinity;
            foreach (GroundContact contact in input.Ground.Current.Ground)
            {
                if (contact.Phase == GroundPhase.Up) continue;
                double inputChartTime = clock.AudioTimeMsToChartTimeMs(contact.TimeMs) - judgementOffsetMs * rate;
                double closestTime = FindClosestGroundTime(contact, inputChartTime, maxWindow,
                    out double closestDistance);
                if (double.IsPositiveInfinity(closestTime)) continue;
                if (closestDistance < bestPlaneDistance ||
                    // The original overlap protection assigns an exact shared
                    // boundary to the later timing plane. Selecting the earlier
                    // point here would prevent that later candidate from ever
                    // reaching AcceptedGroundLanes().
                    (Math.Abs(closestDistance - bestPlaneDistance) < .001 && closestTime > bestPlaneTime))
                {
                    bestPlaneDistance = closestDistance;
                    bestPlaneTime = closestTime;
                }
            }

            if (double.IsPositiveInfinity(bestPlaneTime)) return;
            // Same-tick chord records map to the same double timestamp. Keep a
            // tiny tolerance for BPM conversion noise, but do not use a frame-
            // sized tolerance: a large touch radius must never turn adjacent
            // vertical notes into one chord.
            const double samePlaneToleranceMs = .1;
            foreach (GroundContact contact in input.Ground.Current.Ground)
            {
                if (contact.Phase == GroundPhase.Up) continue;
                double inputChartTime = clock.AudioTimeMsToChartTimeMs(contact.TimeMs) - judgementOffsetMs * rate;
                int first = LowerBoundPointTime(bestPlaneTime - samePlaneToleranceMs);
                int end = UpperBoundPointTime(bestPlaneTime + samePlaneToleranceMs);
                for (int index = first; index < end; index++)
                {
                    if (resolved[index]) continue;
                    ScheduledJudgePoint point = points[index];
                    if (point.TimeMs < bestPlaneTime - samePlaneToleranceMs) continue;
                    ChartNote note = chart.Notes[point.NoteIndex];
                    if (note.Kind == NoteKind.Flick) continue;
                    if (!IsGroundHead(point, note)) continue;
                    if (!TryEvaluateGroundContact(point, note, contact, inputChartTime,
                            out JudgementResult result)) continue;
                    if (!groundCandidatesByPoint.ContainsKey(index))
                        groundCandidatesByPoint.Add(index,
                            new GroundFrameCandidate(index, result,
                                Math.Abs(RealDelta(inputChartTime, point.TimeMs))));
                }
            }
        }

        private void CollectFlickCandidates(double rate)
        {
            // Mobile FLICK shares a fresh gesture across all open windows.
            // Other ground heads still use their single timing-plane selector.
            double window = JudgementEngine.FlickWindowMs * rate;
            foreach (GroundContact contact in input.Ground.Current.Ground) {
                if (!input.Ground.IsFlick(contact)) continue;
                double inputChartTime = clock.AudioTimeMsToChartTimeMs(contact.TimeMs) - judgementOffsetMs * rate;
                int first = LowerBoundPointTime(inputChartTime - window);
                int end = UpperBoundPointTime(inputChartTime + window);
                for (int index = first; index < end; index++) {
                    if (resolved[index] || groundCandidatesByPoint.ContainsKey(index)) continue;
                    ScheduledJudgePoint point = points[index];
                    ChartNote note = chart.Notes[point.NoteIndex];
                    if (note.Kind != NoteKind.Flick || !IsGroundHead(point, note)) continue;
                    if (!TryEvaluateGroundContact(point, note, contact, inputChartTime,
                            out JudgementResult result)) continue;
                    groundCandidatesByPoint.Add(index, new GroundFrameCandidate(index, result,
                        Math.Abs(RealDelta(inputChartTime, point.TimeMs))));
                }
            }
        }

        private double FindClosestGroundTime(GroundContact contact, double inputChartTime,
            double maxWindow, out double closestDistance)
        {
            double closestTime = double.PositiveInfinity;
            closestDistance = double.PositiveInfinity;
            int first = LowerBoundPointTime(inputChartTime - maxWindow);
            int end = UpperBoundPointTime(inputChartTime + maxWindow);
            for (int index = first; index < end; index++)
            {
                if (resolved[index]) continue;
                ScheduledJudgePoint point = points[index];
                ChartNote note = chart.Notes[point.NoteIndex];
                if (note.Kind == NoteKind.Flick) continue;
                if (!IsGroundHead(point, note)) continue;
                if (!TryEvaluateGroundContact(point, note, contact, inputChartTime,
                        out JudgementResult _)) continue;

                double distance = Math.Abs(RealDelta(inputChartTime, point.TimeMs));
                if (distance < closestDistance ||
                    (Math.Abs(distance - closestDistance) < .001 && point.TimeMs > closestTime))
                {
                    closestDistance = distance;
                    closestTime = point.TimeMs;
                }
            }
            return closestTime;
        }

        private int LowerBoundPointTime(double timeMs)
        {
            int low = nextPoint, high = points.Count;
            while (low < high) {
                int mid = (low + high) >> 1;
                if (points[mid].TimeMs < timeMs) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private int UpperBoundPointTime(double timeMs)
        {
            int low = nextPoint, high = points.Count;
            while (low < high) {
                int mid = (low + high) >> 1;
                if (points[mid].TimeMs <= timeMs) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        private static bool IsGroundHead(ScheduledJudgePoint point, ChartNote note)
        {
            if (!note.VisibleHead || note.IsSlideCheckpoint) return false;
            // A wide authored record can own multiple same-time combo points.
            // They are all resolved by the same panel image. Long-note body
            // points remain KEEP checkpoints and must not become new heads.
            if (point.PointIndex > 0 &&
                (note.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide))
                return false;
            return note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or
                NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide;
        }

        private bool TryEvaluateGroundContact(ScheduledJudgePoint point, ChartNote note,
            GroundContact contact, double inputChartTime, out JudgementResult result)
        {
            if (contact.Phase == GroundPhase.Up) {
                result = default;
                return false;
            }
            bool simpleFlick = note.Kind == NoteKind.Flick;
            if (!simpleFlick && !ContactOverlaps(contact, note.Lane, note.Width, note.Kind))
            {
                result = default;
                return false;
            }
            if (note.Kind == NoteKind.Flick)
            {
                if (!FlickDirectionMatches(note, contact))
                {
                    result = default;
                    return false;
                }
                if (!input.Ground.IsFlick(contact))
                {
                    result = default;
                    return false;
                }
            }
            // Mobile FLICK is deliberately lane-independent. It models a
            // horizontal gesture on the panel, so contact width and authored
            // lane overlap must not make the note easier or harder.
            if (!simpleFlick && AcceptedGroundLanes(point, note, contact, inputChartTime) == 0)
            {
                result = default;
                return false;
            }
            bool longHead = point.PointIndex == 0 && note.VisibleHead &&
                note.Kind is (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide);
            NoteKind judgedKind = point.PointIndex == 0 && note.Kind == NoteKind.Hold &&
                string.Equals(note.OriginalType, "HXD", StringComparison.OrdinalIgnoreCase)
                    ? NoteKind.ExTap
                    : note.Kind;
            result = note.Kind == NoteKind.Flick
                ? JudgementEngine.JudgeFlick(inputChartTime, point.TimeMs, clock.Rate)
                : JudgementEngine.JudgeGround(judgedKind, inputChartTime, point.TimeMs, clock.Rate,
                    hard: true, longHead: longHead);
            return result.Grade != Chart.Judgement.Miss;
        }

        private bool TryJudgeHead(int pointIndex, ScheduledJudgePoint point, ChartNote note, double now,
            out JudgementResult result)
        {
            double pointTime = point.TimeMs;
            if (note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction) {
                bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
                double activationWindow = JudgementEngine.Frames(6.0) * clock.Rate;
                bool sustainHead = note.Kind is NoteKind.AirHold or NoteKind.AirAction;
                bool closeEnoughToStart = Math.Abs(now - pointTime) <= activationWindow;
                bool airPresent = IsAirContactActive(now);
                if (sustainHead && airPresent && closeEnoughToStart) {
                    bool recentMotion = lastAirMotionMs >= pointTime - activationWindow &&
                        lastAirMotionMs <= pointTime + activationWindow;
                    // AIR-HOLD starts from sensor presence. AIR ACTION keeps
                    // its active gesture requirement unless the explicit
                    // simple long-note MOD is enabled.
                    bool canStart = note.Kind == NoteKind.AirHold || airLongNoMotion || recentMotion;
                    if (canStart) {
                        if (note.Kind == NoteKind.AirAction && recentMotion && !airLongNoMotion &&
                            !CanConsumeAirMotion(pointTime)) {
                            result = default;
                            return false;
                        }
                        double eventTime = note.Kind == NoteKind.AirHold || airLongNoMotion
                            ? now : lastAirMotionMs;
                        result = note.Kind == NoteKind.AirAction
                            ? JudgementEngine.JudgeAirAction(eventTime, pointTime, clock.Rate)
                            : JudgementEngine.JudgeAir(eventTime, pointTime, down,
                                note.Kind == NoteKind.AirHold, clock.Rate, note.AirFlick);
                        return result.Grade != Chart.Judgement.Miss;
                    }
                }
                double airWindow = JudgementEngine.MissWindowMs(note.Kind, down, note.AirFlick) * clock.Rate;
                bool motion = lastAirMotionMs >= pointTime - airWindow &&
                              lastAirMotionMs <= pointTime + airWindow;
                // The arcade AIR checker consumes a sensor movement pulse and
                // does not compare it with the arrow's visual facing. A hand
                // may slide anywhere inside the AIR area, upward or downward,
                // and still trigger the AIR note. Facing only selects timing.
                bool accepted = airPresent &&
                    (note.Kind == NoteKind.AirHold || motion ||
                     (note.Kind == NoteKind.AirAction && airLongNoMotion && closeEnoughToStart));
                if (accepted && motion && note.Kind != NoteKind.AirHold &&
                    !CanConsumeAirMotion(pointTime))
                    accepted = false;
                if (accepted) {
                    double eventTime = note.Kind == NoteKind.AirHold ? now : lastAirMotionMs;
                    if (note.Kind == NoteKind.AirAction && airLongNoMotion && !motion)
                        eventTime = now;
                    result = note.Kind == NoteKind.AirAction
                        ? JudgementEngine.JudgeAirAction(eventTime, pointTime, clock.Rate)
                        : JudgementEngine.JudgeAir(eventTime, pointTime, down,
                            note.Kind == NoteKind.AirHold, clock.Rate, note.AirFlick);
                    return result.Grade != Chart.Judgement.Miss;
                }
                result = default;
                return false;
            }

            if (groundCandidatesByPoint.TryGetValue(pointIndex, out GroundFrameCandidate candidate))
            {
                result = candidate.Result;
                return result.Grade != Chart.Judgement.Miss;
            }
            result = default;
            return false;
        }

        private bool CanConsumeAirMotion(double pointTime)
        {
            if (lastAirMotionSequence <= 0 || double.IsNegativeInfinity(lastAirMotionMs)) return false;
            // Multiple AIR records at one authored time are a valid chord and
            // share the same sensor event. A later authored plane must wait for
            // a new sensor event, even when the old event is still inside the
            // generous AIR timing window.
            if (consumedAirMotionSequence == lastAirMotionSequence)
                return Math.Abs(consumedAirMotionPointTime - pointTime) <= .1;
            consumedAirMotionSequence = lastAirMotionSequence;
            consumedAirMotionPointTime = pointTime;
            return true;
        }

        private ushort AcceptedGroundLanes(ScheduledJudgePoint point, ChartNote note,
            GroundContact contact, double inputChartTime)
        {
            // PressedLaneMask can contain a lane reached between two Unity
            // samples, or an adjacent lane covered by the contact radius. It
            // is therefore wider than FirstLane/LastLane in both cases.
            int first = 0;
            int last = ArcadeTouchInput.LaneCount - 1;
            ushort accepted = 0;
            for (int lane = first; lane <= last; lane++) {
                if (lane < note.Lane || lane >= note.Lane + Math.Max(1, note.Width)) continue;
                ushort laneBit = (ushort)(1 << lane);
                // TAP/EX heads need a newly occupied cell. Sliding into a new
                // cell produces the same edge as a new press; resting does not.
                // KEEP-style heads still accept continuous coverage.
                bool needsPress = note.Kind is NoteKind.Tap or NoteKind.ExTap ||
                    (note.Kind == NoteKind.Hold && note.OriginalType == "HXD");
                ushort mask = needsPress ? contact.PressedLaneMask : contact.CoveredLaneMask;
                bool edge = (mask & laneBit) != 0 ||
                    (note.Kind == NoteKind.Slide && ContactOverlaps(contact, note.Lane, note.Width, note.Kind));
                if (!edge) continue;
                if (point.UsesGroundProtection && !point.AcceptsGroundLane(lane, inputChartTime)) continue;
                return laneBit;
            }
            return accepted;
        }

        private bool IsCheckpointCovered(ChartNote note, double pointTime, double now)
        {
            if (note.Kind is NoteKind.AirHold or NoteKind.AirLadder) return IsAirContactActive(now);
            if (note.Kind is NoteKind.AirAction)
                return IsAirContactActive(now) &&
                       (airLongNoMotion && note.EndTimeMs > note.TimeMs ||
                         now - lastAirMotionMs <= JudgementEngine.Frames(16.0) * clock.Rate);

            double duration = Math.Max(.001, note.EndTimeMs - note.TimeMs);
            float progress = Mathf.Clamp01((float)((pointTime - note.TimeMs) / duration));
            int lane = Mathf.RoundToInt(Mathf.Lerp(note.Lane, note.EndLane, progress));
            int width = Math.Max(1, Mathf.RoundToInt(Mathf.Lerp(note.Width, note.EndWidth, progress)));
            foreach (GroundContact contact in input.Ground.Current.Ground)
                if (contact.Phase != GroundPhase.Up && ContactOverlaps(contact, lane, width, note.Kind)) return true;
            return false;
        }

        private bool IsAirContactActive(double chartNow)
        {
            if (input.Ground.Current.Air.Present) return true;
            if (double.IsNegativeInfinity(lastAirPresentMs)) return false;
            return chartNow - lastAirPresentMs <=
                JudgementEngine.Frames(JudgementEngine.AirContactGraceFrames) * clock.Rate;
        }

        private bool IsGroundCovered(ChartNote note)
        {
            foreach (GroundContact contact in input.Ground.Current.Ground)
                if (contact.Phase != GroundPhase.Up && ContactOverlaps(contact, note.Lane, note.Width)) return true;
            return false;
        }

        private double RealDelta(double chartTimeMs, double pointTimeMs) =>
            (chartTimeMs - pointTimeMs) / Math.Max(.1, clock.Rate);

        private static bool ContactOverlaps(GroundContact contact, int lane, int width,
            NoteKind kind = NoteKind.Tap)
        {
            int noteWidth = Math.Max(1, width);
            // The original SLIDE checker uses a physical area wider than the
            // authored one for widths 1..3: 1.9/2.9/3.9 cells. Keep this
            // expansion local to SLIDE so TAP chords and dense protection do
            // not become easier by accident.
            float margin = kind == NoteKind.Slide && noteWidth <= 3 ? .45f : 0f;
            int first = Mathf.Clamp(Mathf.FloorToInt(lane - margin), 0, ArcadeTouchInput.LaneCount - 1);
            int last = Mathf.Clamp(Mathf.CeilToInt(lane + noteWidth + margin), first + 1,
                ArcadeTouchInput.LaneCount);
            ushort mask = 0;
            for (int index = first; index < last; index++) mask |= (ushort)(1 << index);
            return (contact.CoveredLaneMask & mask) != 0;
        }

        private static bool HasGroundPress(GroundContact contact, int lane, int width)
        {
            int first = Mathf.Clamp(lane, 0, ArcadeTouchInput.LaneCount - 1);
            int last = Mathf.Min(ArcadeTouchInput.LaneCount, lane + Math.Max(1, width));
            ushort mask = 0;
            for (int index = first; index < last; index++) mask |= (ushort)(1 << index);
            return (contact.PressedLaneMask & mask) != 0;
        }

        private static bool IsLong(NoteKind kind) => kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide
            or NoteKind.AirHold or NoteKind.AirAction or NoteKind.AirLadder;

        private static bool FlickDirectionMatches(ChartNote note, GroundContact contact)
        {
            if (string.Equals(note.SourceType, "L", StringComparison.OrdinalIgnoreCase))
                return contact.FlickDirection < 0;
            if (string.Equals(note.SourceType, "R", StringComparison.OrdinalIgnoreCase))
                return contact.FlickDirection > 0;
            return true;
        }

        private void Resolve(int index, ScheduledJudgePoint point, Chart.Judgement grade, double deltaMs,
            bool countScore = true)
        {
            if (resolved[index]) return;
            if (nonMissJusticeCritical && grade != Chart.Judgement.Miss)
                grade = Chart.Judgement.JusticeCritical;
            resolved[index] = true;
            resolvedCount++;
            if (countScore && point.IsScoring) ScoreState.Add(grade);
            // Hazard results can bypass score accounting but must still break
            // the live combo before the HUD receives the judgement event.
            else if (grade == Chart.Judgement.Miss && chart.Notes[point.NoteIndex].Kind == NoteKind.Mine)
                ScoreState.BreakCombo();
            Judged?.Invoke(point, new JudgementResult(grade, deltaMs));
            if (resolvedCount == points.Count) {
                Debug.Log($"Judgement complete: score={ScoreState.Score(scoringPointCount)} | " +
                          $"JC={ScoreState.JusticeCritical} J={ScoreState.Justice} " +
                          $"AT={ScoreState.Attack} MISS={ScoreState.Miss} MAXCOMBO={ScoreState.MaxCombo}");
            }
        }
    }
}
