using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using ChunithmMobile.Judgement;
using ChunithmMobile.Input;
using ChunithmMobile.Platform;
using ChunithmMobile.Runtime;
using UnityEditor;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // 原版播放映射综合测试：覆盖滚速、判定时间、长条采样、AIR 和高密度保护边界。
    /// <summary>Small deterministic checks for the original C2S scroll semantics.</summary>
    public static class OriginalPlaybackSelfTest
    {
        [MenuItem("CHUNITHM Mobile/Verify original playback map")]
        public static void Run()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "SLP\t1\t0\t384\t-1.000000\t0\n" +
                "SLP\t0\t0\t1536\t2.000000\t1\n" +
                "SLA\t2\t0\t8\t4\t384\t1\n" +
                "TAP\t2\t0\t1\t2\n" +
                "TAP\t2\t0\t9\t2\n";

            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);
            var map = new OriginalPlaybackMap(chart);

            // At 120 BPM a 4/4 measure is exactly two seconds. This protects
            // against accidentally treating C2S RESOLUTION as ticks per beat.
            Require(Math.Abs(chart.Notes[0].TimeMs - 4000) < .001,
                "C2S measure timing was not converted as four quarter-note beats");

            Require(chart.Notes.Count == 2, "synthetic notes were not parsed");
            Require(chart.Notes[0].ScrollProfileId == 0, "global lane got a local SLP profile");
            Require(chart.Notes[1].ScrollProfileId == 1, "SLA did not assign the local SLP profile");

            double globalReverseA = map.NoteDistance(chart.Notes[0], 2500);
            double globalReverseB = map.NoteDistance(chart.Notes[0], 3000);
            Require(globalReverseB > globalReverseA,
                "negative global SLP did not move the note away from the judgement line");

            double localAt2500 = map.NoteDistance(chart.Notes[1], 2500);
            double globalAt2500 = map.NoteDistance(chart.Notes[0], 2500);
            Require(Math.Abs(localAt2500 - globalAt2500) > .001,
                "local SLA/SLP mapping incorrectly followed the global course");

            VerifyPortableAudioClock();
            VerifyScoreModel();
            VerifyDenseGroundProtection();
            VerifyAirJudgementModel();
            VerifyAirSensorHeightMapping();
            VerifyAirSensorEdges();
            VerifyAirDirectionVisualMapping();
            VerifyStandaloneAirVisualOwnership();
            VerifyMultiplayerClockCalibration();
            VerifyLongPointDistributionUsesElapsedTime();
            VerifyDeclaredJudgeMismatchUsesScheduledTotal();
            VerifySlideChainHeadVisibility();
            VerifyExHoldStyleAndVisualOwnership();
            VerifyExHoldKeepCandidateIsolation();
            VerifyKeepOffsetAndSameTimePoints();
            VerifyAirHoldBodyOwnership();
            VerifyOutOfOrderContinuationOwnership();
            VerifyDenseAirPairTimingPlane();
                VerifyVisualSpeedScale();
                VerifyVisibleNoteSpeedRange();
                VerifyIndependentLongHeadVisibility();

            Debug.Log("Original playback self-test passed: global reverse and local SLA/SLP are independent.");
            ValidateExternalChartIfRequested();
        }

        private static void VerifyStandaloneAirVisualOwnership()
        {
            // This is the arcade composition shown as a ground press followed
            // by a lift into the AIR area. All three records deliberately share
            // one timing plane and lane range: the TAP, directional AIR arrow,
            // and AIR-HOLD body must survive parsing as independent visuals.
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "T_JUDGE_TAP\t1\n" +
                "T_JUDGE_AIR\t2\n" +
                "TAP\t1\t0\t4\t4\n" +
                "AIR\t1\t0\t4\t4\tTAP\n" +
                "AHD\t1\t0\t4\t4\tTAP\t96\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            ChartNote tap = chart.Notes.Single(note => note.Kind == NoteKind.Tap);
            ChartNote air = chart.Notes.Single(note => note.Kind == NoteKind.Air);
            ChartNote hold = chart.Notes.Single(note => note.Kind == NoteKind.AirHold);
            Require(tap.HasVisualHead && air.HasVisualHead && hold.HasVisualHead,
                "same-time TAP/AIR/AIR-HOLD composition lost a visual head");
            Require(air.VisibleHead && air.JudgePointCount == 1,
                "standalone AIR arrow lost its independent judgement ownership");

            const string touchingIndependentHolds =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "T_JUDGE_AIR\t2\n" +
                "AHD\t1\t0\t4\t4\tTAP\t96\n" +
                "AHD\t1\t96\t4\t4\tTAP\t96\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(touchingIndependentHolds)))
                chart = C2sParser.Parse(stream);
            Require(chart.Notes.All(note => note.Kind == NoteKind.AirHold && note.HasVisualHead),
                "touching independent AIR-HOLD starts were merged by geometry alone");
        }

        private static void VerifyDenseGroundProtection()
        {
            var notes = new System.Collections.Generic.List<ChartNote> {
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000, Lane = 2, Width = 2,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1100, Lane = 3, Width = 2,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1050, Lane = 8, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 }
            };
            var chart = new ParsedChart(notes, 2000, 120, 3,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var points = JudgePointScheduler.Build(chart);
            ScheduledJudgePoint first = points.Find(point => point.NoteIndex == 0);
            ScheduledJudgePoint second = points.Find(point => point.NoteIndex == 1);
            ScheduledJudgePoint independent = points.Find(point => point.NoteIndex == 2);

            Require(first.AcceptsGroundLane(2, 1060),
                "non-overlapping lane was incorrectly protected");
            Require(!first.AcceptsGroundLane(3, 1050) && second.AcceptsGroundLane(3, 1050),
                "dense same-lane boundary was not assigned to the later note");
            Require(independent.AcceptsGroundLane(8, 1000),
                "a different lane inherited another lane's protection boundary");

            var asymmetricNotes = new System.Collections.Generic.List<ChartNote> {
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 900, Lane = 6, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000, Lane = 6, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1400, Lane = 6, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.ExTap, TimeMs = 2000, Lane = 10, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 2050, Lane = 10, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 }
            };
            var asymmetricChart = new ParsedChart(asymmetricNotes, 2500, 120, asymmetricNotes.Count,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var asymmetricPoints = JudgePointScheduler.Build(asymmetricChart);
            ScheduledJudgePoint middle = asymmetricPoints.Find(point => point.NoteIndex == 1);
            ScheduledJudgePoint exTap = asymmetricPoints.Find(point => point.NoteIndex == 3);
            Require(!middle.AcceptsGroundLane(6, 949) && middle.AcceptsGroundLane(6, 1150) &&
                    !middle.AcceptsGroundLane(6, 1200),
                "ground protection did not clip early and late neighbours independently");
            Require(!exTap.UsesGroundProtection,
                "ExTAP incorrectly inherited TAP MINIMALIZE protection");

            var boundaryNotes = new System.Collections.Generic.List<ChartNote> {
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000, Lane = 4, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 },
                new ChartNote { Kind = NoteKind.Tap, TimeMs = 1100, Lane = 4, Width = 1,
                    VisibleHead = true, JudgePointCount = 1 }
            };
            var boundaryChart = new ParsedChart(boundaryNotes, 1500, 120, 2,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var boundaryPoints = JudgePointScheduler.Build(boundaryChart);
            ScheduledJudgePoint earlier = boundaryPoints.Find(point => point.NoteIndex == 0);
            ScheduledJudgePoint later = boundaryPoints.Find(point => point.NoteIndex == 1);
            const double sharedBoundary = 1050;
            Require(!earlier.AcceptsGroundLane(4, sharedBoundary) &&
                    later.AcceptsGroundLane(4, sharedBoundary),
                "exact dense-note boundary was not assigned to the later timing plane");
        }

        private static void VerifyScoreModel()
        {
            var score = new GameplayScoreState();
            score.Add(Chart.Judgement.JusticeCritical);
            Require(score.Score(1) == 1_010_000, "all-JC score is not 1,010,000");
            score.Reset();
            score.Add(Chart.Judgement.Justice);
            Require(score.Score(1) == 1_000_000, "all-JUSTICE score is not 1,000,000");
            score.Reset();
            score.Add(Chart.Judgement.Attack);
            Require(score.Score(1) == 500_000, "ATTACK score weight is not 50 percent");

            JudgementResult normalRate = JudgementEngine.JudgeGround(NoteKind.Tap, 80, 0, 1.0);
            JudgementResult fastRate = JudgementEngine.JudgeGround(NoteKind.Tap, 120, 0, 1.5);
            Require(normalRate.Grade == fastRate.Grade && Math.Abs(fastRate.DeltaMs - 80) < .001,
                "playback speed changed the real-time judgement window");
        }

        private static void VerifyAirJudgementModel()
        {
            // AIR timing is direction-profiled, but the input pulse itself is
            // direction-agnostic. Protect the asymmetric original windows and
            // their fixed real-time duration at a changed playback rate.
            JudgementResult upOnTime = JudgementEngine.JudgeAir(0, 0, false, false, 1.0);
            JudgementResult upEarly = JudgementEngine.JudgeAir(-201, 0, false, false, 1.0);
            JudgementResult downOnTime = JudgementEngine.JudgeAir(0, 0, true, false, 1.0);
            JudgementResult downEarly = JudgementEngine.JudgeAir(-201, 0, true, false, 1.0);
            JudgementResult fastRate = JudgementEngine.JudgeAir(-150, 0, false, false, 1.5);
            JudgementResult normalRate = JudgementEngine.JudgeAir(-150, 0, false, false, 1.0);
            JudgementResult flickDownLate = JudgementEngine.JudgeAir(JudgementEngine.Frames(10), 0,
                true, false, 1.0, true);
            JudgementResult actionEarlyAttack = JudgementEngine.JudgeAirAction(
                -JudgementEngine.Frames(15), 0);
            JudgementResult actionEarlyJustice = JudgementEngine.JudgeAirAction(
                -JudgementEngine.Frames(10), 0);
            JudgementResult actionLateMiss = JudgementEngine.JudgeAirAction(
                JudgementEngine.Frames(16) + .01, 0);
            Chart.Judgement holdCritical = JudgementEngine.JudgeAirHoldCheckpoint(true, 190);
            Chart.Judgement holdJustice = JudgementEngine.JudgeAirHoldCheckpoint(true, 350);
            Chart.Judgement holdAttack = JudgementEngine.JudgeAirHoldCheckpoint(true, 500);
            Chart.Judgement holdMiss = JudgementEngine.JudgeAirHoldCheckpoint(true, 601);
            Chart.Judgement actionCritical = JudgementEngine.JudgeAirActionCheckpoint(true,
                JudgementEngine.Frames(16.0));
            Chart.Judgement actionMiss = JudgementEngine.JudgeAirActionCheckpoint(true,
                JudgementEngine.Frames(16.0) + .01);
            Require(upOnTime.Grade == Chart.Judgement.JusticeCritical &&
                    downOnTime.Grade == Chart.Judgement.JusticeCritical,
                "AIR on-time input was not critical");
            Require(upEarly.Grade == Chart.Judgement.Miss &&
                    downEarly.Grade == Chart.Judgement.JusticeCritical,
                "AIR up/down JC-or-MISS windows are not asymmetric");
            Require(fastRate.Grade == normalRate.Grade && Math.Abs(fastRate.DeltaMs + 100) < .001,
                "AIR playback rate changed the real-time judgement delta");
            Require(flickDownLate.Grade == Chart.Judgement.JusticeCritical,
                "AIR FLICKDOWN incorrectly used the ordinary AIRDOWN late window");
            Require(actionEarlyAttack.Grade == Chart.Judgement.JusticeCritical &&
                    actionEarlyJustice.Grade == Chart.Judgement.JusticeCritical &&
                    actionLateMiss.Grade == Chart.Judgement.Miss &&
                    Math.Abs(JudgementEngine.MissWindowMs(NoteKind.AirAction) -
                        JudgementEngine.Frames(16)) < .001,
                "AIR ACTION did not use its independent symmetric timing profile");
            Require(holdCritical == Chart.Judgement.JusticeCritical &&
                    holdJustice == Chart.Judgement.JusticeCritical &&
                    holdAttack == Chart.Judgement.JusticeCritical &&
                    holdMiss == Chart.Judgement.Miss,
                "AIR-HOLD continuation windows do not match the original profile");
            Require(JudgementEngine.AirContactGraceFrames < JudgementEngine.KeepAttackFrames,
                "AIR contact grace is too wide to be a short sensor-history bridge");
            Require(actionCritical == Chart.Judgement.JusticeCritical &&
                    actionMiss == Chart.Judgement.Miss,
                "AIR ACTION ladder checkpoints are not fixed-JC inside the 16-frame window");

            double continuousKeep = JudgementEngine.CheckpointCoverageDelta(true, true,
                990, 1010, 1000);
            double lateReconnect = JudgementEngine.CheckpointCoverageDelta(true, false,
                double.NegativeInfinity, 1100, 1000);
            double earlyRelease = JudgementEngine.CheckpointCoverageDelta(false, false,
                900, 1000, 1000);
            double fastRateReconnect = JudgementEngine.CheckpointCoverageDelta(true, false,
                double.NegativeInfinity, 1150, 1000, 1.5);
            Require(Math.Abs(continuousKeep) < .001 &&
                    Math.Abs(lateReconnect - 100) < .001 &&
                    Math.Abs(earlyRelease + 100) < .001 &&
                    Math.Abs(fastRateReconnect - 100) < .001,
                "KEEP coverage history erased reconnect timing or changed with playback rate");

            JudgementResult flickCritical = JudgementEngine.JudgeFlick(JudgementEngine.Frames(4), 0);
            JudgementResult flickJustice = JudgementEngine.JudgeFlick(95, 0);
            JudgementResult flickMiss = JudgementEngine.JudgeFlick(JudgementEngine.FlickLateWindowMs + .01, 0);
            Require(flickCritical.Grade == Chart.Judgement.JusticeCritical &&
                    flickJustice.Grade == Chart.Judgement.JusticeCritical &&
                    flickMiss.Grade == Chart.Judgement.Miss,
                "Mobile FLICK does not preserve the expanded gesture-time JC/MISS window");

            Require(JudgementEngine.IsMineTouchWindow(JudgementEngine.MineInMs) &&
                    JudgementEngine.IsMineTouchWindow(JudgementEngine.MineOutMs) &&
                    !JudgementEngine.IsMineTouchWindow(JudgementEngine.MineOutMs + .001) &&
                    JudgementEngine.IsMineCleanupDue(JudgementEngine.MineNgMs + .001),
                "MINE IN/OUT/NG boundaries are not asymmetric");
        }

        private static void VerifyLongPointDistributionUsesElapsedTime()
        {
            // Both HOLDs have the same authored tick duration, but the second
            // occurs after the BPM halves and therefore lasts twice as long.
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t240\n" +
                "BPM\t0\t0\t240\n" +
                "BPM\t1\t0\t120\n" +
                "T_JUDGE_TAP\t2\n" +
                "T_JUDGE_HLD\t6\n" +
                "T_JUDGE_SLD\t0\n" +
                "T_JUDGE_AIR\t0\n" +
                "T_JUDGE_FLK\t0\n" +
                "T_JUDGE_ALL\t8\n" +
                "HLD\t0\t0\t0\t4\t192\n" +
                "HLD\t1\t0\t8\t4\t192\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            Require(chart.Notes.Count == 2 &&
                    chart.Notes[1].JudgePointCount - 1 ==
                    (chart.Notes[0].JudgePointCount - 1) * 2,
                "long-note judge points were distributed by raw ticks instead of elapsed time");
            Require(JudgePointScheduler.Build(chart).Count == chart.OriginalJudgeTotal,
                "elapsed-time distribution changed the original judge total");
        }

        private static void VerifyAirSensorHeightMapping()
        {
            Require(AirSensorInput.SensorAtHeight(0f) == 0 &&
                    AirSensorInput.SensorAtHeight(.499f) == 2 &&
                    AirSensorInput.SensorAtHeight(1f) == 5,
                "AIR sensor levels were not mapped from vertical hand height");
            Require(AirSensorInput.CrossedSensorMask(1, 4) == 0b011110 &&
                    AirSensorInput.CrossedSensorMask(4, 1) == 0b011110,
                "AIR motion did not expose every crossed height sensor");
            Require(AirSensorInput.DisplacementLogCount == 15 &&
                    AirSensorInput.DisplacementLimit == 14 &&
                    Math.Abs(AirSensorInput.MotionReferenceMs - 100) < .001,
                "AIR displacement history does not match DIS_LOG/DIS_LIMIT/LOG");
            Require(!AirSensorInput.HasOriginalDisplacement(0,
                        AirSensorInput.MotionHeightThreshold - .001f) &&
                    AirSensorInput.HasOriginalDisplacement(0,
                        AirSensorInput.MotionHeightThreshold),
                "AIR HEIGHT_ABS=25 threshold was not preserved in normalized input space");
        }

        private static void VerifyAirSensorEdges()
        {
            ushort lowAndMiddle = 0b001001;
            ushort middleAndHigh = 0b101000;
            Require(AirSensorInput.EnteredSensorMask(0, lowAndMiddle) == lowAndMiddle,
                "the first hand entering AIR did not create a sensor edge");
            Require(AirSensorInput.EnteredSensorMask(lowAndMiddle, middleAndHigh) == 0b100000 &&
                    AirSensorInput.ExitedSensorMask(lowAndMiddle, middleAndHigh) == 0b000001,
                "AIR sensor replacement edges were not separated");
            Require(AirSensorInput.ExitedSensorMask(middleAndHigh, 0) == middleAndHigh,
                "leaving AIR did not create a sensor edge");
        }

        private static void VerifyVisualSpeedScale()
        {
            Require(Math.Abs(OriginalNoteSpeed.VisualMultiplier(OriginalNoteSpeed.DefaultOption) - 2.4f) < .0001,
                "the global 2x visual speed multiplier is missing");
            Require(Math.Abs(OriginalNoteSpeed.VisualMultiplier(96) -
                OriginalNoteSpeed.VisualMultiplier(48) * 2f) < .0001,
                "note speed multiplier did not scale linearly after the 2x boost");
        }

        private static void VerifyVisibleNoteSpeedRange()
        {
            Require(Math.Abs(OriginalNoteSpeed.MaximumParameter - 17f) < .0001f,
                "visible note speed maximum must be 17.0");
            Require(OriginalNoteSpeed.Count == 171,
                "visible note speed must expose 0.1 steps from 0.0 through 17.0");
            Require(OriginalNoteSpeed.Label(OriginalNoteSpeed.Count - 1) == "17.0",
                "visible note speed last label is not 17.0");
            Require(OriginalNoteSpeed.ClampOption(9999) == OriginalNoteSpeed.Count - 1,
                "note speed clamp exceeds the visible maximum");
        }

        private static void VerifySlideChainHeadVisibility()
        {
            // SLD owns the slide head; SLC sections continue its body.
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "SLD\t0\t0\t0\t4\t24\t4\t4\n" +
                "SLC\t0\t24\t4\t4\t24\t8\t4\n" +
                "SLC\t0\t48\t8\t4\t48\t8\t4\n";

            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            Require(chart.Notes.Count == 3, "slide chain fixture was not parsed");
            Require(chart.Notes[0].VisibleHead, "the first SLD segment lost its slide head");
            Require(!chart.Notes[1].VisibleHead && !chart.Notes[2].VisibleHead,
                "continuous slide segments incorrectly rendered duplicate heads");
            Require(!chart.Notes[2].VisibleTail,
                "an SLC curve-control endpoint incorrectly gained a slide tail cap");

            const string cappedSource =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "SLD\t0\t0\t0\t4\t24\t4\t4\n" +
                "SLC\t0\t24\t4\t4\t24\t8\t4\n" +
                "SLD\t0\t48\t8\t4\t48\t8\t4\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(cappedSource)))
                chart = C2sParser.Parse(stream);
            Require(chart.Notes[2].VisibleTail,
                "a terminal SLD record lost its authored slide tail cap");
        }

        private static void VerifyDeclaredJudgeMismatchUsesScheduledTotal()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "T_JUDGE_TAP\t1\n" +
                "T_JUDGE_HLD\t0\n" +
                "T_JUDGE_SLD\t0\n" +
                "T_JUDGE_AIR\t0\n" +
                "T_JUDGE_FLK\t0\n" +
                "T_JUDGE_ALL\t99\n" +
                "TAP\t0\t0\t4\t4\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);
            int scheduled = JudgePointScheduler.Build(chart).Count(point => point.IsScoring);
            Require(chart.DeclaredJudgeTotal == 99 && chart.OriginalJudgeTotal == 1 && scheduled == 1,
                "malformed T_JUDGE_ALL fabricated checkpoints or changed the playable total");
        }

        private static void VerifyAirDirectionVisualMapping()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "AIR\t0\t0\t0\t2\tTAP\n" +
                "AUL\t0\t24\t2\t2\tTAP\n" +
                "AUR\t0\t48\t4\t2\tTAP\n" +
                "ADW\t0\t72\t6\t2\tTAP\n" +
                "ADL\t0\t96\t8\t2\tTAP\n" +
                "ADR\t0\t120\t10\t2\tTAP\n" +
                "ASC\t0\t144\t12\t2\tTAP\t5.0\t24\t12\t2\t5.0\tDEF\n" +
                "ASD\t0\t168\t14\t2\tTAP\t5.0\t24\t14\t2\t5.0\tDEF\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);
            AirDirection[] directions = chart.Notes.Select(note => note.AirDirection).ToArray();
            Require(directions.SequenceEqual(new[] { AirDirection.Up, AirDirection.UpLeft,
                    AirDirection.UpRight, AirDirection.Down, AirDirection.DownLeft,
                    AirDirection.DownRight, AirDirection.None, AirDirection.None }),
                "C2S AIR opcode mapping changed or ASC/ASD gained a false direction");

            var mirrorMethod = typeof(ChartPlaybackRenderer).GetMethod("ShouldMirrorAirMesh",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(mirrorMethod != null &&
                    (bool)mirrorMethod.Invoke(null, new object[] { AirDirection.UpLeft }) &&
                    (bool)mirrorMethod.Invoke(null, new object[] { AirDirection.DownLeft }) &&
                    !(bool)mirrorMethod.Invoke(null, new object[] { AirDirection.UpRight }) &&
                    !(bool)mirrorMethod.Invoke(null, new object[] { AirDirection.DownRight }),
                "tilted AIR mesh mirroring is opposite to AUL/AUR/ADL/ADR");

            ChartMirror.Apply(chart);
            Require(chart.Notes[0].AirDirection == AirDirection.Up &&
                    chart.Notes[1].AirDirection == AirDirection.UpRight &&
                    chart.Notes[2].AirDirection == AirDirection.UpLeft &&
                    chart.Notes[3].AirDirection == AirDirection.Down &&
                    chart.Notes[4].AirDirection == AirDirection.DownRight &&
                    chart.Notes[5].AirDirection == AirDirection.DownLeft &&
                    chart.Notes[6].AirDirection == AirDirection.None &&
                    chart.Notes[7].AirDirection == AirDirection.None,
                "MIRROR MOD did not swap AIR horizontal directions exactly once");
        }

        private static void VerifyMultiplayerClockCalibration()
        {
            var root = new GameObject("Multiplayer clock regression");
            try
            {
                var client = root.AddComponent<MultiplayerClient>();
                Require(Math.Abs(client.ServerDeadlineDelayMilliseconds(
                        16_000, 15_000, 1_000_000) - 1_000) < .001 &&
                        Math.Abs(client.ServerDeadlineDelayMilliseconds(
                        6_000, 0, 5_000) - 1_000) < .001,
                    "uncalibrated deadline fallback trusted the device wall-clock offset");
                MethodInfo observe = typeof(MultiplayerClient).GetMethod("ObserveServerClock",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(observe != null, "server clock calibration entry point is missing");

                // This device is ten seconds behind the server. With an 80ms
                // round trip, the echoed server timestamp describes the local
                // request midpoint and must produce an exact 10-second offset.
                observe.Invoke(client, new object[] {
                    new MultiplayerPongMessage { clientTime = 5_000, serverTime = 15_040 },
                    5_080L
                });
                Require(client.HasServerClockSample &&
                        Math.Abs(client.ServerClockOffsetMilliseconds - 10_000) < .001 &&
                        Math.Abs(client.ServerDeadlineDelayMilliseconds(16_000, 15_000, 5_080) - 920) < .001 &&
                        client.ServerDeadlineToLocalUnixMilliseconds(16_000, 15_000) == 6_000,
                    "server deadline did not compensate a slow device wall clock");

                var aheadRoot = new GameObject("Ahead multiplayer clock regression");
                try {
                    var ahead = aheadRoot.AddComponent<MultiplayerClient>();
                    observe.Invoke(ahead, new object[] {
                        new MultiplayerPongMessage { clientTime = 1_005_000, serverTime = 5_040 },
                        1_005_080L
                    });
                    Require(Math.Abs(ahead.ServerDeadlineDelayMilliseconds(6_000, 5_000, 1_005_080) - 920) < .001,
                        "server deadline did not compensate a fast device wall clock");
                } finally {
                    UnityEngine.Object.DestroyImmediate(aheadRoot);
                }
            }
            finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyIndependentLongHeadVisibility()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "HLD\t0\t0\t0\t4\t24\n" +
                "HLD\t0\t24\t0\t4\t24\n" +
                "SLD\t0\t48\t4\t4\t24\t4\t4\n" +
                "SLD\t0\t72\t4\t4\t24\t4\t4\n";

            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            Require(chart.Notes.Count == 4, "independent long-note fixture was not parsed");
            foreach (ChartNote note in chart.Notes)
                Require(note.VisibleHead && note.VisibleTail && !note.IsContinuation,
                    "touching independent SLD/HLD records lost their own head or tail");
        }

        private static void VerifyExHoldStyleAndVisualOwnership()
        {
            string[] styles = { "UP", "DW", "CE", "LC", "RC", "LS", "RS", "BS" };
            var source = new StringBuilder()
                .Append("RESOLUTION\t384\n")
                .Append("BPM_DEF\t120\n")
                .Append("BPM\t0\t0\t120\n");
            for (int i = 0; i < styles.Length; i++) {
                source.Append("CHR\t").Append(i + 1).Append("\t0\t4\t4\t")
                    .Append(styles[i]).Append('\n');
                source.Append("HXD\t").Append(i + 1).Append("\t0\t4\t4\t48\t")
                    .Append(styles[i]).Append('\n');
            }

            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source.ToString())))
                chart = C2sParser.Parse(stream);

            int exHoldIndex = 0;
            foreach (ChartNote note in chart.Notes) {
                if (note.OriginalType != "HXD") continue;
                Require(note.Kind == NoteKind.Hold,
                    "HXD entered the HeavenHold renderer instead of the ground HOLD renderer");
                Require(exHoldIndex < styles.Length && note.SourceType == styles[exHoldIndex],
                    "HXD style/direction was not read from column 6");
                Require(note.HasVisualHead && note.VisibleTail,
                    "HXD lost its EX head or HOLD tail");
                exHoldIndex++;
            }
            Require(exHoldIndex == styles.Length, "HXD style fixture did not parse every direction");
            foreach (ChartNote note in chart.Notes)
                Require(note.Kind != NoteKind.HeavenHold,
                    "HXD fixture generated a green Heaven roof or purple Heaven walls");
        }

        private static void VerifyExHoldKeepCandidateIsolation()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "T_JUDGE_TAP\t2\n" +
                "T_JUDGE_HLD\t3\n" +
                "CHR\t0\t0\t4\t4\tUP\n" +
                "HXD\t0\t0\t4\t4\t192\tUP\n" +
                "TAP\t0\t68\t4\t1\n";
            ParsedChart parsed;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                parsed = C2sParser.Parse(stream);
            var parsedPoints = JudgePointScheduler.Build(parsed);
            Require(parsedPoints.FindAll(point =>
                    parsed.Notes[point.NoteIndex].OriginalType == "HXD").Count == 4,
                "HXD fixture lost its EX head or KEEP judge points");

            MethodInfo candidateCheck = typeof(GameplayJudgeController).GetMethod("IsGroundHead",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(candidateCheck != null, "ground candidate predicate was not found");
            foreach (ScheduledJudgePoint point in parsedPoints) {
                ChartNote note = parsed.Notes[point.NoteIndex];
                bool candidate = (bool)candidateCheck.Invoke(null, new object[] { point, note });
                if (note.OriginalType == "HXD")
                    Require(candidate == (point.PointIndex == 0),
                        "HXD did not expose exactly one ground EX head");
                if (note.Kind is NoteKind.Tap or NoteKind.ExTap)
                    Require(candidate, "CHR/TAP head was hidden by the neighbouring HXD span");
            }

            // Older cached indexes can still contain a visible HeavenHold head.
            // Its first point remains pressable, but every later point is KEEP.
            var legacyHeaven = new ChartNote {
                Kind = NoteKind.HeavenHold, TimeMs = 1000, EndTimeMs = 2000,
                Lane = 4, Width = 4, EndLane = 4, EndWidth = 4,
                VisibleHead = true, JudgePointCount = 3
            };
            var legacyChart = new ParsedChart(
                new System.Collections.Generic.List<ChartNote> { legacyHeaven }, 2500, 120, 3,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var legacyPoints = JudgePointScheduler.Build(legacyChart);
            Require((bool)candidateCheck.Invoke(null, new object[] { legacyPoints[0], legacyHeaven }),
                "legacy HeavenHold head stopped accepting its initial press");
            for (int i = 1; i < legacyPoints.Count; i++)
                Require(!(bool)candidateCheck.Invoke(null, new object[] { legacyPoints[i], legacyHeaven }),
                    "HeavenHold KEEP checkpoint was treated as a new ground head");
        }

        private static void VerifyKeepOffsetAndSameTimePoints()
        {
            var longNote = new ChartNote {
                Kind = NoteKind.Hold, TimeMs = 1000, EndTimeMs = 2000,
                Lane = 4, Width = 2, EndLane = 4, EndWidth = 2,
                VisibleHead = true, JudgePointCount = 3
            };
            var chart = new ParsedChart(
                new System.Collections.Generic.List<ChartNote> { longNote }, 2500, 120, 3,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var points = JudgePointScheduler.Build(chart);
            Require(points.Count == 3, "long-note KEEP point count changed");
            Require(Math.Abs(points[0].TimeMs - 1000) < .001,
                "long-note head time was shifted with KEEP");
            double keepOffset = JudgementEngine.Frames(JudgementEngine.KeepOffsetFrames);
            Require(Math.Abs(points[1].TimeMs - (1000 + 1000d / 3d + keepOffset)) < .001 &&
                    Math.Abs(points[2].TimeMs - (1000 + 2000d / 3d + keepOffset)) < .001,
                "KEEP checkpoints are missing the original half-frame offset");

            var wideTap = new ChartNote {
                Kind = NoteKind.Tap, TimeMs = 3000, EndTimeMs = 3000,
                Lane = 0, Width = 8, VisibleHead = true, JudgePointCount = 2
            };
            var sameTimeChart = new ParsedChart(
                new System.Collections.Generic.List<ChartNote> { wideTap }, 3500, 120, 2,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var sameTime = JudgePointScheduler.Build(sameTimeChart);
            Require(sameTime.Count == 2 && sameTime[0].TimeMs == sameTime[1].TimeMs,
                "same-time authored judge points were collapsed");
        }

        private static void VerifyAirHoldBodyOwnership()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "T_JUDGE_AIR\t2\n" +
                "AHD\t1\t0\t4\t4\tHLD\t96\n" +
                "AHX\t1\t96\t4\t4\tAHD\t96\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);
            Require(chart.Notes.Count == 2 && !chart.Notes[0].VisibleHead &&
                    !chart.Notes[1].VisibleHead && chart.Notes[0].HasVisualHead &&
                    !chart.Notes[1].HasVisualHead &&
                    !chart.Notes[0].IsContinuation && chart.Notes[1].IsContinuation,
                "AIR-HOLD body/continuation ownership is incorrect");
            var points = JudgePointScheduler.Build(chart);
            Require(points.Count > 0 && Math.Abs(points[0].TimeMs - chart.Notes[0].TimeMs) < .001,
                "the first AIR-HOLD body checkpoint was shifted away from its authored start");
        }

        private static void VerifyOutOfOrderContinuationOwnership()
        {
            // The file order is intentionally reversed. The renderer must still
            // keep one head and one final tail for a continuous slide chain.
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "SLC\t0\t24\t4\t4\t24\t8\t4\n" +
                "SLD\t0\t0\t0\t4\t24\t4\t4\n";

            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            Require(chart.Notes.Count == 2 && chart.Notes[0].VisibleHead &&
                    !chart.Notes[1].VisibleHead && !chart.Notes[1].VisibleTail,
                "continuation ownership depended on C2S line order");
        }

        private static void VerifyDenseAirPairTimingPlane()
        {
            // The renderer's AIR partner lookup used to inspect only a small
            // sorted neighbourhood. Keep enough same-tick ground records in
            // this fixture to prove that pairing is time-plane based.
            var notes = new System.Collections.Generic.List<ChartNote>();
            for (int lane = 0; lane < 16; lane++) {
                notes.Add(new ChartNote {
                    Kind = NoteKind.Tap, TimeMs = 1000, EndTimeMs = 1000,
                    Lane = lane, Width = 1, VisibleHead = true
                });
            }
            notes.Add(new ChartNote {
                Kind = NoteKind.Tap, TimeMs = 1000.2, EndTimeMs = 1000.2,
                Lane = 5, Width = 2, VisibleHead = true
            });
            notes.Add(new ChartNote {
                Kind = NoteKind.Air, TimeMs = 1000, EndTimeMs = 1000,
                Lane = 5, Width = 2, VisibleHead = true
            });
            var chart = new ParsedChart(notes, 2000, 120, notes.Count,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());

            // This is the same deterministic selection rule used by the
            // renderer: exact time plane first, then maximum lane overlap.
            ChartNote air = notes[17];
            ChartNote matching = null;
            int overlapBest = 0;
            foreach (ChartNote ground in notes) {
                if (ground.Kind == NoteKind.Air || Math.Abs(ground.TimeMs - air.TimeMs) > .1) continue;
                int overlap = Math.Max(0, Math.Min(ground.Lane + ground.Width,
                    air.Lane + air.Width) - Math.Max(ground.Lane, air.Lane));
                if (overlap > overlapBest) { overlapBest = overlap; matching = ground; }
            }
            Require(matching != null && matching.TimeMs == 1000 && matching.Lane == 5,
                "AIR pairing selected a different timing plane or missed the dense chord");
        }

        private static void VerifyPortableAudioClock()
        {
            var source = new FakeAudioTimeSource { Seconds = 10 };
            var clock = new DspChartClock(source);
            clock.Play(-2000);
            source.Seconds = 10.5;
            Require(Math.Abs(clock.TimeMs - (-1500)) < .001,
                "injected audio clock did not advance chart time");
            Require(Math.Abs(clock.AudioTimeMsToChartTimeMs(10_500) - (-1500)) < .001,
                "input DSP timestamp did not map to chart time");

            clock.SetRate(.8);
            source.Seconds = 11.0;
            Require(Math.Abs(clock.TimeMs - (-1100)) < .001,
                "playback rate was not applied to the injected audio clock");
            Require(Math.Abs(clock.AudioTimeMsToChartTimeMs(11_000) - (-1100)) < .001,
                "rate-adjusted input timestamp did not map to chart time");

            clock.Pause();
            double paused = clock.TimeMs;
            source.Seconds = 20;
            Require(Math.Abs(clock.TimeMs - paused) < .001,
                "paused chart advanced with platform audio time");
        }

        private sealed class FakeAudioTimeSource : IAudioTimeSource
        {
            public double Seconds { get; set; }
        }

        private static void ValidateExternalChartIfRequested()
        {
            string path = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (Directory.Exists(path)) {
                string[] charts = Directory.GetFiles(path, "*.c2s", SearchOption.AllDirectories);
                Require(charts.Length > 0, "CHUNITHM_CHART_PATH directory has no C2S: " + path);
                Array.Sort(charts, StringComparer.OrdinalIgnoreCase);
                foreach (string chart in charts) ValidateExternalChart(chart);
                return;
            }
            Require(File.Exists(path), "CHUNITHM_CHART_PATH does not exist: " + path);
            ValidateExternalChart(path);
        }

        private static void ValidateExternalChart(string path)
        {
            ParsedChart chart = C2sParser.Parse(path);
            var map = new OriginalPlaybackMap(chart);
            var judgePoints = JudgePointScheduler.Build(chart);
            int scoringPointCount = 0;
            foreach (ScheduledJudgePoint point in judgePoints)
                if (point.IsScoring) scoringPointCount++;
            Require(chart.Notes.Count > 0, "external chart has no notes");
            if (scoringPointCount != chart.OriginalJudgeTotal) {
                var noteKinds = new System.Collections.Generic.Dictionary<string, int>();
                var pointKinds = new System.Collections.Generic.Dictionary<string, int>();
                var parsedKinds = new System.Collections.Generic.Dictionary<string, int>();
                var scheduledKinds = new System.Collections.Generic.Dictionary<string, int>();
                var parsedPairs = new System.Collections.Generic.Dictionary<string, int>();
                var scheduledPairs = new System.Collections.Generic.Dictionary<string, int>();
                var parsedJudgeCounts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (ChartNote note in chart.Notes) {
                    string key = note.OriginalType ?? note.Kind.ToString();
                    noteKinds[key] = noteKinds.TryGetValue(key, out int count) ? count + 1 : 1;
                    string parsedKey = note.Kind.ToString();
                    parsedKinds[parsedKey] = parsedKinds.TryGetValue(parsedKey, out int parsedCount)
                        ? parsedCount + 1 : 1;
                    string pairKey = parsedKey + "/" + key;
                    parsedPairs[pairKey] = parsedPairs.TryGetValue(pairKey, out int pairCount)
                        ? pairCount + 1 : 1;
                    parsedJudgeCounts[key] = parsedJudgeCounts.TryGetValue(key, out int judgeCount)
                        ? judgeCount + note.JudgePointCount : note.JudgePointCount;
                }
                foreach (ScheduledJudgePoint point in judgePoints) {
                    ChartNote note = chart.Notes[point.NoteIndex];
                    string key = note.OriginalType ?? note.Kind.ToString();
                    pointKinds[key] = pointKinds.TryGetValue(key, out int count) ? count + 1 : 1;
                    string scheduledKey = note.Kind.ToString();
                    scheduledKinds[scheduledKey] = scheduledKinds.TryGetValue(scheduledKey, out int scheduledCount)
                        ? scheduledCount + 1 : 1;
                    string scheduledPairKey = scheduledKey + "/" + key;
                    scheduledPairs[scheduledPairKey] = scheduledPairs.TryGetValue(scheduledPairKey,
                        out int scheduledPairCount) ? scheduledPairCount + 1 : 1;
                }
                Debug.LogError($"Judge schedule detail: path={path} notesByType={FormatCounts(noteKinds)} " +
                               $"pointsByType={FormatCounts(pointKinds)} parsedKinds={FormatCounts(parsedKinds)} " +
                               $"scheduledKinds={FormatCounts(scheduledKinds)} parsedPairs={FormatCounts(parsedPairs)} " +
                               $"scheduledPairs={FormatCounts(scheduledPairs)} parsedJudgeCounts={FormatCounts(parsedJudgeCounts)}");
            }
            Require(scoringPointCount == chart.OriginalJudgeTotal,
                $"judge schedule mismatch: scoring={scoringPointCount}, runtime={judgePoints.Count}, " +
                $"original={chart.OriginalJudgeTotal}");
            for (int i = 1; i < judgePoints.Count; i++)
                Require(judgePoints[i - 1].TimeMs <= judgePoints[i].TimeMs,
                    "judge schedule is not chronological");
            for (int i = 0; i < chart.Notes.Count; i++) {
                ChartNote note = chart.Notes[i];
                Require(note.Width > 0, $"external chart note {i} has invalid width");
                // Test before, at, and after the authored time. This catches bad
                // integration around negative/zero/extreme speed segments.
                double[] samples = { note.TimeMs - 1000, note.TimeMs - 1, note.TimeMs,
                    note.TimeMs + 1, note.TimeMs + 1000 };
                foreach (double now in samples) {
                    double distance = map.NoteDistance(note, now);
                    Require(!double.IsNaN(distance) && !double.IsInfinity(distance),
                        $"external chart note {i} produced a non-finite distance at {now}ms");
                }
            }

            Debug.Log($"External chart playback validation passed: {path} | notes={chart.Notes.Count} | " +
                      $"judge={scoringPointCount} | runtime={judgePoints.Count} | " +
                      $"profiles={chart.FieldScrollProfiles.Count} | assignments={chart.ScrollAssignments.Count}");
        }

        private static string FormatCounts(System.Collections.Generic.Dictionary<string, int> counts)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var pair in counts) parts.Add(pair.Key + "=" + pair.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts.ToArray());
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
