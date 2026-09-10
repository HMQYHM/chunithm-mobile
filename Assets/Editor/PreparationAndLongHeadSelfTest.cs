using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Input;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    public static class PreparationAndLongHeadSelfTest
    {
        private sealed class TestTime : IAudioTimeSource { public double Seconds { get; set; } }

        public static void BuildVerifiedApk()
        {
            Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            VerifyFirstFrame();
            foreach (double rate in new[] { .5, 1.0, 1.5, 2.0 })
            foreach (int offset in new[] { -500, 0, 500 }) {
                var time = new TestTime { Seconds = 100 };
                var clock = new DspChartClock(time);
                clock.SetRate(rate);
                double start = 102 + Math.Max(0, -offset) / 1000d;
                clock.PlayScheduled(start, 0, 102);
                double frozen = clock.TimeMs;
                time.Seconds = 101.999;
                Require(clock.Preparing && Math.Abs(clock.TimeMs - frozen) < .001, "preparation advanced chart time");
                time.Seconds = 102;
                Require(!clock.Preparing && Math.Abs(clock.TimeMs - frozen) < .001, "preparation boundary jumped");
                time.Seconds = start + .1;
                Require(Math.Abs(clock.TimeMs - 100 * rate) < .001, "rate/offset changed after preparation");
            }
            var pauseTime = new TestTime { Seconds = 10 };
            var paused = new DspChartClock(pauseTime);
            paused.PlayScheduled(12, 0, 12);
            pauseTime.Seconds = 10.7;
            paused.Pause();
            pauseTime.Seconds = 30;
            paused.Resume();
            pauseTime.Seconds = 31.299;
            Require(paused.Preparing && Math.Abs(paused.TimeMs) < .001, "pause lost remaining preparation");
            pauseTime.Seconds = 31.31;
            Require(!paused.Preparing && Math.Abs(paused.TimeMs - 10) < .001, "resume start drifted");

            // A curve changes lane and width, with an unrelated shorter TAP
            // interleaved before the closing SLD. The final SLD is a KEEP.
            ParsedChart chart = Parse("T_JUDGE_TAP\t2\nT_JUDGE_SLD\t12\nT_JUDGE_ALL\t14\n" +
                "SLD\t0\t0\t0\t2\t48\t2\t2\n" +
                "SLC\t0\t48\t2\t2\t96\t4\t4\tSLD\n" +
                "TAP\t0\t100\t12\t1\n" +
                "SLD\t0\t144\t4\t4\t48\t8\t2\tSLD\n");
            Require(chart.Notes[0].VisibleHead && !chart.Notes[1].VisibleHead &&
                !chart.Notes[3].VisibleHead && chart.Notes[3].IsContinuation, "curve gained another head");
            var points = JudgePointScheduler.Build(chart);
            Require(points.Count == 14, "authored combo total changed");
            MethodInfo isHead = typeof(GameplayJudgeController).GetMethod("IsGroundHead", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var point in points.Where(p => p.NoteIndex == 1 || p.NoteIndex == 3))
                Require(!(bool)isHead.Invoke(null, new object[] { point, chart.Notes[point.NoteIndex] }), "bend entered TAP candidate path");
            ParsedChart independent = Parse("SLD\t0\t0\t0\t2\t48\t2\t2\n" +
                "SLD\t0\t48\t2\t2\t48\t4\t2\n" +
                "HLD\t1\t0\t0\t2\t48\nHLD\t1\t48\t0\t2\t48\n");
            Require(independent.Notes.All(n => n.VisibleHead), "independent touching long notes lost heads");
            ParsedChart headless = Parse("SLC\t0\t0\t0\t2\t48\t2\t2\tSLD\n" +
                "SLD\t0\t48\t2\t2\t48\t4\t2\tSLD\n");
            Require(headless.Notes.All(n => !n.VisibleHead), "headless curve gained head judgement");
            foreach (NoteKind kind in new[] { NoteKind.Hold, NoteKind.Slide, NoteKind.HeavenHold }) {
                var deltas = new[] { 0d, 50, 75, 100 };
                var grades = new[] { Chart.Judgement.JusticeCritical, Chart.Judgement.Justice, Chart.Judgement.Attack, Chart.Judgement.Miss };
                for (int i = 0; i < deltas.Length; i++)
                    Require(JudgementEngine.JudgeGround(kind, deltas[i], 0, longHead: true).Grade == grades[i], "long head grade mismatch");
            }
            Require(JudgementEngine.JudgeCheckpoint(true, 75) == Chart.Judgement.JusticeCritical &&
                JudgementEngine.JudgeCheckpoint(false, 0) == Chart.Judgement.Miss, "KEEP not JC/MISS");

            string fixture = Path.GetFullPath("Build/level-rule-fixture");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "test.c2s"), "RESOLUTION\t384\nBPM_DEF\t120\n");
            foreach (int fraction in new[] { 0, 10, 50, 59, 60, 90, 99 }) {
                File.WriteAllText(Path.Combine(fixture, "Music.xml"),
                    "<MusicData><fumens><MusicFumenData><enable>true</enable><file><path>test.c2s</path></file>" +
                    "<level>14</level><levelDecimal>" + fraction + "</levelDecimal></MusicFumenData></fumens></MusicData>");
                var song = (SongEntry)typeof(SongCatalog).GetMethod("ReadSong", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { fixture });
                Require(song.Charts.Single().Level == (fraction >= 60 ? "14+" : "14"), "plus threshold mismatch");
            }
            Debug.Log("Preparation/long-head/level regression passed: 2s freeze, rates/offsets, pause/resume, curved and headless chains, independent heads, JC/MISS KEEP, .60 plus threshold.");
        }

        private static void VerifyFirstFrame()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var root = new GameObject("Preparation first frame regression");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", flags).GetValue(renderer);
                var materials = (IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", flags).GetValue(renderer);
                if (materials.Count == 0) typeof(ChartPlaybackRenderer).GetMethod("Awake", flags).Invoke(renderer, null);
                var input = root.AddComponent<ArcadeInputRig>();
                if (input.Ground == null) typeof(ArcadeInputRig).GetMethod("Awake", flags).Invoke(input, null);
                var judge = root.AddComponent<GameplayJudgeController>();
                MethodInfo renderTick = typeof(ChartPlaybackRenderer).GetMethod("LateUpdate", flags);
                MethodInfo judgeTick = typeof(GameplayJudgeController).GetMethod("Update", flags);
                foreach (double rate in new[] { 1d, 2d, 1d }) {
                    ParsedChart chart = Parse("TAP\t0\t0\t2\t2\nSLD\t0\t0\t4\t2\t96\t6\t2\n");
                    var time = new TestTime { Seconds = 10 };
                    var clock = new DspChartClock(time);
                    clock.SetRate(rate);
                    renderer.Bind(chart, clock);
                    judge.Bind(chart, clock, input, preparedPoints: JudgePointScheduler.Build(chart));
                    int judged = 0;
                    Action<ScheduledJudgePoint, JudgementResult> count = (p, r) => judged++;
                    judge.Judged += count;
                    renderer.PrepareFirstFrame(0);
                    Require(active.Count == 2, "opening notes were not warmed");
                    clock.PlayScheduled(12, 0, 12);
                    time.Seconds = 11.99;
                    judgeTick.Invoke(judge, null);
                    renderTick.Invoke(renderer, null);
                    Require(judged == 0, "judgement ran during preparation");
                    foreach (object visual in active.Values)
                        Require(!((GameObject)visual.GetType().GetField("Root").GetValue(visual)).activeSelf,
                            "notes visible during preparation");
                    time.Seconds = 12;
                    renderTick.Invoke(renderer, null);
                    foreach (object visual in active.Values) {
                        Require(((GameObject)visual.GetType().GetField("Root").GetValue(visual)).activeSelf,
                            "opening note root remained hidden");
                        Require(((GameObject)visual.GetType().GetField("Head").GetValue(visual)).activeSelf,
                            "opening head missing on first playback frame");
                    }
                    time.Seconds = 12.3;
                    judgeTick.Invoke(judge, null);
                    Require(judged > 0, "judgement did not resume after preparation");
                    judge.Judged -= count;
                    renderer.Clear();
                }
                Debug.Log("First-frame regression passed: normal speed, 2x, replay; no hidden opening heads or preparation judgements.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ParsedChart Parse(string records)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("RESOLUTION\t384\nBPM_DEF\t120\n" + records));
            return C2sParser.Parse(stream);
        }
        private static void Require(bool success, string message)
        {
            if (!success) throw new InvalidOperationException(message);
        }
    }
}
