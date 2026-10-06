using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Input;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // 地面输入回归测试：验证 16 分区、覆盖范围、移动阶段、flick 脉冲和多指样本。
    // 使用假的音频时间源，确保测试结果不依赖编辑器帧率。
    public static class GroundInputSelfTest
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo Capture = typeof(ArcadeTouchInput).GetMethod("CaptureSample", PrivateInstance);
        private static readonly MethodInfo Tick = typeof(GameplayJudgeController).GetMethod("Update", PrivateInstance);
        private static readonly MethodInfo Evaluate = typeof(GameplayJudgeController).GetMethod("TryEvaluateGroundContact", PrivateInstance);

        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void BuildMineComboVerifiedApk()
        {
            VerifyMineCombo();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void BuildSlideCheckpointVerifiedApk()
        {
            VerifySlideCheckpoints();
            Run();
            VerifyMineCombo();
            PreparationAndLongHeadSelfTest.Run();
            AndroidBuildConfigurator.BuildApk();
        }

        private static ChartNote[] ParseSlideFixture(string records)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("RESOLUTION\t384\nBPM_DEF\t120\n" + records));
            return C2sParser.Parse(stream).Notes.ToArray();
        }

        private static void VerifySlideCheckpoints()
        {
            const string chain = "SLD\t0\t0\t2\t2\t192\t2\t2\n" +
                "SLD\t0\t192\t2\t2\t192\t6\t4\n" +
                "SLD\t1\t0\t6\t4\t192\t6\t4\n";
            var parsed = ParseSlideFixture(chain);
            Require(!parsed[0].IsSlideCheckpoint && parsed[1].IsSlideCheckpoint && parsed[2].IsSlideCheckpoint,
                "SLD bend/width change/straight continuation was not linked");
            Require(parsed.All(n => n.VisibleHead), "checkpoint classification changed visual head allocation");
            var independent = ParseSlideFixture("SLD\t0\t0\t2\t2\t192\t2\t2\nSLD\t0\t192\t2\t2\t192\t2\t2\n" +
                "SLD\t1\t1\t2\t2\t96\t6\t2\n");
            Require(independent.All(n => !n.IsSlideCheckpoint), "independent straight/gapped slide lost head judgement");
            var fork = ParseSlideFixture("SLD\t0\t0\t7\t2\t192\t7\t2\n" +
                "TAP\t0\t180\t0\t1\nSLD\t0\t192\t7\t2\t96\t0\t2\nSLD\t0\t192\t7\t2\t96\t14\t2\n");
            Require(fork[2].IsSlideCheckpoint && fork[3].IsSlideCheckpoint, "fork with interleaved TAP lost owner");
            int cases = 0;
            foreach (double rate in new[] { .5, 1d, 2d })
            foreach (int offset in new[] { -500, 0, 500 })
            foreach (double late in new[] { 0d, 50, 100, 151 }) {
                var notes = ParseSlideFixture(chain);
                using var test = new Fixture(notes, rate, offsetMs: offset);
                Chart.Judgement? grade = null;
                test.Judge.Judged += (point, result) => {
                    if (point.NoteIndex == 1 && point.PointIndex == 0) grade = result.Grade;
                };
                double sample = 1000 + (offset + late) * rate;
                test.Frame(sample, Finger(1, 3f, TouchPhase.Began));
                Require(grade == (late <= 150 ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss),
                    $"bend produced {grade} at {late}ms, rate={rate}, offset={offset}");
                cases++;
            }
            using (var test = new Fixture(ParseSlideFixture(chain))) {
                Chart.Judgement? node = null;
                test.Judge.Judged += (point, result) => { if (point.NoteIndex == 1) node = result.Grade; };
                test.Frame(950, Finger(1, 3f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 3f, TouchPhase.Stationary));
                Require(node == Chart.Judgement.JusticeCritical, "bend required a new press");
            }
            foreach (double delta in new[] { 0d, 50, 75, 151 }) {
                using var test = new Fixture(ParseSlideFixture(chain));
                Chart.Judgement? head = null;
                test.Judge.Judged += (point, result) => { if (point.NoteIndex == 0 && point.PointIndex == 0) head = result.Grade; };
                test.Frame(delta, Finger(1, 3f, TouchPhase.Began));
                Require(head == JudgementEngine.JudgeGround(NoteKind.Slide, delta, 0, longHead: true).Grade,
                    "four-grade starting head changed");
            }
            string realPath = Path.GetFullPath("../\u63d0\u53d6\u7684\u6b4c\u66f2/100/chart/0100_03.c2s");
            var real = C2sParser.Parse(realPath);
            var points = JudgePointScheduler.Build(real);
            var checkpoints = real.Notes.Where(n => n.IsSlideCheckpoint).ToArray();
            Require(checkpoints.Length >= 6, "real-chart SLD forks not recognized");
            foreach (var point in points)
                if (real.Notes[point.NoteIndex].IsSlideCheckpoint)
                    Require(!point.UsesGroundProtection, "KEEP bend stole a TAP protection window");
            foreach (var note in checkpoints) note.IsSlideCheckpoint = false;
            var before = JudgePointScheduler.Build(real);
            Require(points.Select(p => (p.NoteIndex, p.PointIndex, p.TimeMs)).SequenceEqual(
                before.Select(p => (p.NoteIndex, p.PointIndex, p.TimeMs))), "bend fix moved or added judge points");
            Require(points.Count == real.OriginalJudgeTotal, "real chart combo total changed");
            Debug.Log($"Slide checkpoint regression passed: {cases} rate/offset/window cases, SLD forks/width changes, stationary contact, four-grade head, real chart {checkpoints.Length} junctions with unchanged judge times and totals.");
        }

        private static void VerifyMineCombo()
        {
            int cases = 0;
            foreach (double rate in new[] { .5, 1d, 2d })
            foreach (bool simple in new[] { false, true })
            foreach (bool immune in new[] { false, true })
            foreach (bool resting in new[] { false, true }) {
                using var test = new Fixture(new[] { Note(NoteKind.Tap, 500, 2),
                    Note(NoteKind.Tap, 750, 4), Note(NoteKind.Mine, 1000, 8),
                    Note(NoteKind.Tap, 1500, 2) }, rate, simple, mineImmunity: immune);
                test.Frame(500, Finger(1, 2.5f, TouchPhase.Began));
                test.Frame(600);
                test.Frame(750, Finger(1, 4.5f, TouchPhase.Began));
                test.Frame(800);
                Require(test.Judge.ScoreState.Combo == 2, "mine fixture needs a live combo");
                int events = 0, comboAtEvent = -1;
                Chart.Judgement? mineGrade = null;
                test.Judge.Judged += (point, result) => {
                    if (point.NoteIndex != 2) return;
                    events++; mineGrade = result.Grade; comboAtEvent = test.Judge.ScoreState.Combo;
                };
                if (resting) test.Frame(900, Finger(1, 8.5f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 8.5f, resting ? TouchPhase.Stationary : TouchPhase.Began));
                Require(mineGrade == (immune ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss),
                    "mine collision or immunity grade changed");
                Require(test.Judge.ScoreState.Combo == (immune ? 2 : 0) && comboAtEvent == (immune ? 2 : 0),
                    "mine did not break combo before HUD event, or immunity broke it");
                Require(test.Judge.ScoreState.MaxCombo == 2 && test.Judge.ScoreState.Resolved == 2,
                    "hazard combo reset changed historical max or scoring totals");
                test.Frame(1010, Finger(1, 8.5f, TouchPhase.Stationary));
                test.Frame(1200);
                Require(events == 1, "mine resolved more than once");
                test.Frame(1500, Finger(1, 2.5f, TouchPhase.Began));
                Require(test.Judge.ScoreState.Combo == (immune ? 3 : 1), "combo did not restart after mine");
                cases++;
            }
            foreach (bool automatic in new[] { false, true }) {
                using var test = new Fixture(new[] { Note(NoteKind.Tap, 500, 2), Note(NoteKind.Mine, 1000, 8) },
                    automatic: automatic);
                test.Frame(500, Finger(1, 2.5f, TouchPhase.Began));
                test.Frame(800);
                if (automatic) test.Frame(1000, Finger(1, 8.5f, TouchPhase.Began));
                test.Frame(1100);
                Require(test.Judge.ScoreState.Combo >= 1, "untouched or AUTO mine broke combo");
                cases++;
            }
            Debug.Log($"Mine combo regression passed: {cases} cases; press/held collision, .5x/1x/2x, simple judgement, immunity, AUTO, avoidance, event ordering, deduplication, next-note recovery.");
        }

        public static void Run()
        {
            VerifyDefaultFlick();
            VerifyOverlappingFlicks();
            foreach (double rate in new[] { .5, 1.0, 2.0 }) {
                foreach (NoteKind kind in new[] { NoteKind.Tap, NoteKind.ExTap })
                    foreach (bool simple in new[] { false, true }) {
                        using var test = new Fixture(new[] { Note(kind, 1000, 4), Note(kind, 1100, 4) }, rate, simple);
                        test.Frame(1000, Finger(1, 4.5f, TouchPhase.Began));
                        Require(test.Judge.ScoreState.Resolved == 1, "initial press must hit exactly one timing plane");
                        test.Frame(1100, Finger(1, 4.5f, TouchPhase.Stationary));
                        Require(test.Judge.ScoreState.Resolved == 1, "stationary contact hit the next TAP/EX");
                        test.Frame(1100 + 101 * rate, Finger(1, 4.5f, TouchPhase.Stationary));
                        Require(test.Judge.ScoreState.Miss == 1, "rested-on TAP/EX must eventually MISS");
                    }
            }

            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 4), Note(NoteKind.Tap, 1100, 5) })) {
                test.Frame(1000, Finger(1, 4.5f, TouchPhase.Began));
                test.Frame(1100, Finger(1, 5.5f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 2, "sliding into a new cell must trigger TAP");
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 4), Note(NoteKind.Tap, 1100, 4) })) {
                test.Frame(1000, Finger(1, 4.5f, TouchPhase.Began));
                test.Frame(1100, Finger(1, 4.5f, TouchPhase.Stationary), Finger(2, 4.5f, TouchPhase.Began));
                test.Frame(1110, Finger(1, 4.5f, TouchPhase.Ended), Finger(2, 4.5f, TouchPhase.Stationary));
                Require(test.Judge.ScoreState.Resolved == 1, "a second finger on an occupied cell generated a press");
                test.Frame(1120, Finger(2, 4.5f, TouchPhase.Ended));
                test.Frame(1130, Finger(2, 4.5f, TouchPhase.Began));
                Require(test.Judge.ScoreState.Resolved == 2, "release then re-press must trigger TAP");
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 4), Note(NoteKind.Tap, 1000, 6), Note(NoteKind.Tap, 1100, 5) })) {
                test.Rig.Ground.SetContactRangeLanes(3);
                test.Frame(1000, Finger(1, 5.5f, TouchPhase.Began));
                Require(test.Judge.ScoreState.Resolved == 2, "wide contact must preserve same-time chords");
                test.Frame(1100, Finger(1, 5.5f, TouchPhase.Stationary));
                Require(test.Judge.ScoreState.Resolved == 2, "wide stationary contact ate the next row");
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 6) })) {
                test.Rig.Ground.SetContactRangeLanes(3);
                test.Frame(900, Finger(1, 4.5f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 5.5f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 1, "newly covered outer cell lost its press edge");
                test.Frame(1010, Finger(1, 5.5f, TouchPhase.Moved));
                Require(test.Rig.Ground.Current.Ground[0].PressedLaneMask == 0, "same-position Moved generated another edge");
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 6) })) {
                test.Frame(900, Finger(1, 3.5f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 7.5f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 1, "fast sweep lost an intermediate cell");
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Tap, 1000, 15) })) {
                test.Frame(950, Finger(1, 3.5f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 5.5f, TouchPhase.Moved));
                GroundContact moving = test.Rig.Ground.Current.Ground[0];
                Require(test.Rig.Ground.IsFlick(moving), "fresh slash rejected");
                test.Frame(1010, Finger(1, 5.5f, TouchPhase.Stationary));
                GroundContact resting = test.Rig.Ground.Current.Ground[0];
                Require(!test.Rig.Ground.IsFlick(resting), "resting finger reused a slash");
                test.Frame(1020, Finger(1, 5.5f, TouchPhase.Moved));
                Require(!test.Rig.Ground.IsFlick(test.Rig.Ground.Current.Ground[0]), "zero-distance Moved reused a slash");
                foreach (NoteKind kind in new[] { NoteKind.Hold, NoteKind.HeavenHold, NoteKind.Slide }) {
                    ChartNote note = Note(kind, 1020, 5);
                    object[] args = { new ScheduledJudgePoint(0, 0, 1020), note, resting, 1020.0, null };
                    Require((bool)Evaluate.Invoke(test.Judge, args), "stationary coverage must still start a normal long note");
                    note.OriginalType = "HXD";
                    if (kind == NoteKind.Hold)
                        Require(!(bool)Evaluate.Invoke(test.Judge, args), "EX HOLD head must require a press");
                }
            }
            foreach (NoteKind kind in new[] { NoteKind.Hold, NoteKind.HeavenHold, NoteKind.Slide }) {
                ChartNote note = Note(kind, 1000, 5);
                using var test = new Fixture(new[] { note });
                test.Frame(1060);
                Require(test.Judge.ScoreState.Resolved == 0, "long head expired inside its four-grade window");
                test.Frame(1075, Finger(1, 5.5f, TouchPhase.Began));
                Require(test.Judge.ScoreState.Attack == 1, "late starting head must retain ATTACK instead of KEEP JC");
            }
            ChartNote exHold = Note(NoteKind.Hold, 1000, 5);
            exHold.OriginalType = "HXD";
            using (var test = new Fixture(new[] { exHold })) {
                test.Frame(1090);
                Require(test.Judge.ScoreState.Miss == 1, "EX HOLD incorrectly inherited the wider normal HOLD window");
            }
            foreach (double rate in new[] { .5, 1.0, 2.0 }) {
                foreach (bool reconnect in new[] { false, true }) {
                    ChartNote hold = Note(NoteKind.Hold, 1000, 5);
                    hold.EndTimeMs = 2600;
                    hold.JudgePointCount = 2;
                    double checkpoint = 1800 + JudgementEngine.Frames(JudgementEngine.KeepOffsetFrames);
                    using var test = new Fixture(new[] { hold }, rate);
                    test.Frame(1000, Finger(1, 5.5f, TouchPhase.Began));
                    test.Frame(checkpoint - (reconnect ? 200 : 100) * rate, Finger(1, 5.5f, TouchPhase.Stationary));
                    test.Frame(checkpoint);
                    if (reconnect) {
                        Require(test.Judge.ScoreState.Resolved == 1, "old coverage forced a premature MISS at high playback rate");
                        test.Frame(checkpoint + 50 * rate, Finger(1, 5.5f, TouchPhase.Began));
                    }
                    Require(test.Judge.ScoreState.JusticeCritical == 2, "KEEP release/reconnect tolerance changed with playback rate");
                }
            }
            Debug.Log("Ground input regression passed: static TAP/EX rejection at .5x/1x/2x with simple judgement on/off, slide edges, merged fingers, wide chords, outer cells, fast sweeps, fresh flicks, long coverage, HOLD/EX-HOLD expiry boundaries, rate-independent KEEP release/reconnect.");
        }

        private static void VerifyDefaultFlick()
        {
            double window = JudgementEngine.FlickWindowMs;
            double lateWindow = JudgementEngine.FlickLateWindowMs;
            Require(Math.Abs(window - (1000.0 / 60 * 5 + 20)) < .000001, "FLICK window must expand each side by 20ms");
            Require(Math.Abs(lateWindow - window - 10) < .000001,
                "FLICK late window must retain the additional 10ms");
            Require(JudgementEngine.MissWindowMs(NoteKind.Flick) == lateWindow,
                "FLICK cleanup differs from the late input window");
            foreach (double rate in new[] { .5, 1.0, 2.0 }) {
                foreach (double delta in new[] { -window, lateWindow, -window - .01, lateWindow + .01 }) {
                    bool hit = delta >= -window && delta <= lateWindow;
                    Require((JudgementEngine.JudgeFlick(delta * rate, 0, rate).Grade == Chart.Judgement.JusticeCritical) == hit,
                        "FLICK exact timing boundary changed with rate");
                }
                foreach (double delta in new[] { -window + .001, -95, 95, window - .001 })
                    foreach (int width in new[] { 1, 8 }) {
                        ChartNote note = Note(NoteKind.Flick, 1000, 8);
                        note.Width = width;
                        using var test = new Fixture(new[] { note }, rate);
                        double time = 1000 + delta * rate;
                        test.Frame(100, Finger(1, 7f, TouchPhase.Began));
                        test.Frame(time - 1, Finger(1, 7f, TouchPhase.Stationary));
                        test.Frame(time, Finger(1, 8.1f, TouchPhase.Moved));
                        Require(test.Judge.ScoreState.JusticeCritical == 1,
                            "default FLICK must accept a fresh gesture in its authored lane");
                    }
                using (var test = new Fixture(new[] { Note(NoteKind.Flick, 1000, 8) }, rate)) {
                    test.Frame(100, Finger(1, 7f, TouchPhase.Began));
                    test.Frame(1000 - (window + .01) * rate, Finger(1, 8.1f, TouchPhase.Moved));
                    Require(test.Judge.ScoreState.Resolved == 0, "FLICK hit outside the early boundary");
                    test.Frame(1000 + (lateWindow - .01) * rate, Finger(1, 8.1f, TouchPhase.Stationary));
                    Require(test.Judge.ScoreState.Resolved == 0, "FLICK expired early or reused a stationary gesture");
                    test.Frame(1000 + (lateWindow + .01) * rate, Finger(1, 8.1f, TouchPhase.Stationary));
                    Require(test.Judge.ScoreState.Miss == 1, "unhit FLICK did not MISS after its late boundary");
                }
                using (var test = new Fixture(new[] { Note(NoteKind.Flick, 1000, 8) }, rate)) {
                    // Keep the initial press outside the ordinary TAP attack
                    // window even at 2x. Otherwise the press itself correctly
                    // becomes ATTACK before the later slash sample arrives.
                    test.Frame(800, Finger(1, 8.8f, TouchPhase.Began));
                    test.Frame(1000, Finger(1, 8.1f, TouchPhase.Moved));
                    Require(test.Judge.ScoreState.JusticeCritical == 1,
                        "FLICK did not accept a gesture in the authored lane");
                }
            }
            using (var test = new Fixture(new[] { Note(NoteKind.Flick, 1000, 8), Note(NoteKind.Flick, 1200, 8) })) {
                test.Frame(900, Finger(1, 7f, TouchPhase.Began));
                test.Frame(1000, Finger(1, 7.4f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.Resolved == 0, "sub-half-cell movement triggered FLICK");
                test.Frame(1001, Finger(1, 8.1f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 1, "gesture hit a FLICK outside its window");
                test.Frame(1200, Finger(1, 8.4f, TouchPhase.Stationary));
                Require(test.Judge.ScoreState.Resolved == 1, "stationary contact repeated FLICK");
                test.Frame(1201, Finger(1, 7.8f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 2, "a fresh opposite-direction gesture was rejected");
            }
            Debug.Log("Default FLICK passed: timing window, authored-lane hitbox, half-cell gesture trigger, stationary rejection and no hits outside the timing window at .5x/1x/2x.");
        }

        private static void VerifyOverlappingFlicks()
        {
            foreach (double rate in new[] { .5, .8, 1.0, 1.2, 1.5, 2.0 })
                foreach (int offset in new[] { -500, 0, 500 })
                    foreach (bool sameLane in new[] { false, true }) {
                        double T(double milliseconds) => 1000 + (milliseconds - 1000) * rate;
                        double InputTime(double milliseconds) => T(milliseconds) + offset * rate;
                        ChartNote[] notes = {
                            Note(NoteKind.Flick, T(1000), 8),
                            Note(NoteKind.Flick, T(1040), sameLane ? 8 : 10),
                            Note(NoteKind.Flick, T(1080), sameLane ? 8 : 12),
                            Note(NoteKind.Flick, T(1200), 8)
                        };
                        using var test = new Fixture(notes, rate, offsetMs: offset);
                        var events = new List<int>();
                        test.Judge.Judged += (point, result) => events.Add(point.NoteIndex);
                        test.Frame(InputTime(900), Finger(1, 7f, TouchPhase.Began), Finger(2, 7f, TouchPhase.Began));
                        test.Frame(InputTime(1050), Finger(1, 12.6f, TouchPhase.Moved), Finger(2, 12.6f, TouchPhase.Moved));
                        Require(test.Judge.ScoreState.JusticeCritical == 3 && events.Count == 3 &&
                            events.Contains(0) && events.Contains(1) && events.Contains(2),
                            "one gesture must resolve all overlapping FLICK windows exactly once, regardless of lane/rate/offset");
                        test.Frame(InputTime(1200), Finger(1, 12.6f, TouchPhase.Stationary));
                        test.Frame(InputTime(1200.1), Finger(1, 12.6f, TouchPhase.Moved));
                        Require(test.Judge.ScoreState.Resolved == 3, "a stale gesture hit a FLICK entering the window later");
                        test.Frame(InputTime(1201), Finger(1, 7f, TouchPhase.Moved));
                        Require(test.Judge.ScoreState.JusticeCritical == 4 && events.Count == 4,
                            "a fresh gesture did not resolve the remaining FLICK exactly once");
                    }

            using (var test = new Fixture(new[] {
                Note(NoteKind.Flick, 1000, 5), Note(NoteKind.Tap, 1048, 5),
                Note(NoteKind.Flick, 1050, 5), Note(NoteKind.Tap, 1080, 5) })) {
                var events = new List<int>();
                test.Judge.Judged += (point, result) => events.Add(point.NoteIndex);
                test.Frame(900, Finger(1, 4.9f, TouchPhase.Began));
                test.Frame(1050, Finger(1, 5.5f, TouchPhase.Moved));
                Require(events.Count == 3 && events.Contains(0) && events.Contains(1) && events.Contains(2),
                    "TAP and FLICK blocked each other or nearby TAP planes were merged");
                test.Frame(1080, Finger(1, 5.5f, TouchPhase.Stationary));
                Require(events.Count == 3, "FLICK batching allowed a stationary TAP");
            }
            using (var test = new Fixture(new[] {
                Note(NoteKind.Flick, 1000, 8), Note(NoteKind.Flick, 1000, 8),
                Note(NoteKind.Flick, 1040, 8) })) {
                test.Frame(900, Finger(1, 7f, TouchPhase.Began));
                test.Frame(1050, Finger(1, 8.6f, TouchPhase.Moved));
                Require(test.Judge.ScoreState.JusticeCritical == 3,
                    "same-time/same-lane duplicate FLICK records lost independent scores");
            }
            Debug.Log("Overlapping FLICK passed: 1000/1040/1080ms all hit at 1050ms; 1200ms stays pending; duplicate records/multi-finger deduplication, TAP coexistence, stationary rejection, six rates and +/-500ms judge offsets.");
        }

        private static ChartNote Note(NoteKind kind, double time, int lane) => new ChartNote {
            Kind = kind, TimeMs = time, EndTimeMs = time, Lane = lane, Width = 1,
            EndLane = lane, EndWidth = 1, VisibleHead = true, HasVisualHead = true, JudgePointCount = 1
        };

        private static Touch Finger(int id, float lane, TouchPhase phase)
        {
            const float cell = 7.428571f;
            return new Touch { fingerId = id, phase = phase,
                position = new Vector2(lane * (cell + 1f) / (16f * cell + 15f) * 1600f, 100f) };
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private sealed class Source : IAudioTimeSource { public double Seconds { get; set; } }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject root = new GameObject("Ground input regression");
            private readonly Source source = new Source();
            private readonly double rate;
            public readonly ArcadeInputRig Rig;
            public readonly GameplayJudgeController Judge;

            public Fixture(ChartNote[] notes, double rate = 1, bool simple = false, int offsetMs = 0,
                bool mineImmunity = false, bool automatic = false)
            {
                this.rate = rate;
                Rig = root.AddComponent<ArcadeInputRig>();
                if (Rig.Ground == null) typeof(ArcadeInputRig).GetMethod("Awake", PrivateInstance).Invoke(Rig, null);
                Judge = root.AddComponent<GameplayJudgeController>();
                var chart = new ParsedChart(new List<ChartNote>(notes), 3000, 120, notes.Length,
                    new List<ScrollEvent>(), new Dictionary<int, List<ScrollEvent>>(), new List<ScrollAssignment>());
                var clock = new DspChartClock(source);
                clock.SetRate(rate);
                clock.Play(0);
                Judge.Bind(chart, clock, Rig, offsetMs: offsetMs, enableNonMissJusticeCritical: simple,
                    enableMineImmunity: mineImmunity, enableAutoPlay: automatic);
            }

            public void Frame(double chartTime, params Touch[] contacts)
            {
                source.Seconds = chartTime / rate / 1000.0;
                Capture.Invoke(Rig.Ground, new object[] { source.Seconds * 1000, 1600, 900, contacts });
                Tick.Invoke(Judge, null);
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
