using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Platform;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    // 地雷/倒播回归测试：确认地雷时间窗口和倒退播放时的可见性边界不混淆。
    public static class MineReverseSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private sealed class Source : IAudioTimeSource { public double Seconds => 0; }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        public static void BuildVerifiedApk()
        {
            Run();
            OpeningNoteRenderSelfTest.Run();
            WorldEndAirNodeSelfTest.Run();
            AndroidBuildConfigurator.BuildApk();
        }

        public static void Run()
        {
            const string records = "RESOLUTION\t384\nBPM_DEF\t120\n" +
                "SLP\t0\t0\t768\t-1.0\t0\n" +
                "MNE\t1\t0\t0\t2\nTAP\t1\t0\t4\t2\n" +
                "AIR\t1\t0\t8\t2\nASD\t1\t0\t12\t2\t0\t3\t192\t12\t2\t3\n";
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(records));
            ParsedChart chart = C2sParser.Parse(data);
            var merge = typeof(LocalChartPlaybackSession).GetMethod("ShouldMergeAnswerSound", Static);
            var starts = new List<int>();
            int mines = 0;
            foreach (var point in JudgePointScheduler.Build(chart)) {
                if (chart.Notes[point.NoteIndex].Kind != NoteKind.Mine) continue;
                mines++;
                Require((bool)merge.Invoke(null, new object[] { chart.Notes[point.NoteIndex], point }),
                    "mine missing from answer timeline");
                Require(!(bool)merge.Invoke(null, new object[] { chart.Notes[point.NoteIndex],
                    new ScheduledJudgePoint(point.NoteIndex, 1, point.TimeMs) }), "duplicate mine cue");
            }
            Require(mines == 1, "mine scheduler fixture failed");
            starts.AddRange(new[] { 100, 100, 100, 100, 100, 100, 200 });
            int dropped = (int)typeof(LocalChartPlaybackSession).GetMethod("LimitConcurrentAnswerSounds", Static)
                .Invoke(null, new object[] { starts, 4 });
            Require(dropped == 2 && starts.Count == 5, "four-cue mix limit changed");

            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var root = new GameObject("Mine reverse regression");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials", Private)
                    .GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                var clock = new DspChartClock(new Source());
                renderer.Bind(chart, clock);
                var active = (IDictionary)typeof(ChartPlaybackRenderer).GetField("active", Private).GetValue(renderer);
                var render = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private);
                var map = new OriginalPlaybackMap(chart);
                Require(map.NoteDistance(chart.Notes[0], 1990) < 0, "fixture is not below line during reverse");
                render.Invoke(renderer, new object[] { 1990d });
                foreach (var value in active.Values) {
                    Require(!Part(value, "Head").activeSelf, "future reverse head leaked below line");
                    Require(!Part(value, "Tail").activeSelf, "future reverse tail leaked below line");
                }
                render.Invoke(renderer, new object[] { 2000d });
                Require(active.Contains(0) && Part(active[0], "Head").activeSelf, "mine missing at hit time");
                renderer.MarkJudged(0, 0);
                render.Invoke(renderer, new object[] { 2001d });
                Require(!Part(active[0], "Head").activeSelf, "judged mine resurrected");

                using var forwardData = new MemoryStream(Encoding.UTF8.GetBytes(
                    "RESOLUTION\t384\nBPM_DEF\t120\nTAP\t1\t0\t0\t2\n"));
                renderer.Bind(C2sParser.Parse(forwardData), clock);
                render.Invoke(renderer, new object[] { 1900d });
                Require(active.Contains(0) && Part(active[0], "Head").activeSelf, "normal approach hidden");
                render.Invoke(renderer, new object[] { 2010d });
                Require(Part(active[0], "Head").activeSelf, "pending normal head hidden");

                var audio = root.AddComponent<InterfaceAudioController>();
                if (typeof(InterfaceAudioController).GetField("gameplayKeySource", Private).GetValue(audio) == null)
                    typeof(InterfaceAudioController).GetMethod("Awake", Private).Invoke(audio, null);
                var key = (AudioSource)typeof(InterfaceAudioController).GetField("gameplayKeySource", Private).GetValue(audio);
                Require(key != null && key.volume > 0, "mine key output muted");
                Require(typeof(InterfaceAudioController).GetField("switchClip", Private).GetValue(audio) != null,
                    "mine key fallback missing");
                audio.Configure(false, 1, true, 1, 1);
                audio.PlayMineKeySound();
                Require(!key.isPlaying, "disabled key sound played");
                Debug.Log("Mine/reverse tests passed: mine cue, duplicate suppression, four-cue limit, reverse heads/tails, normal approach, judgement lifetime, key source and toggle.");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject Part(object visual, string name) =>
            (GameObject)visual.GetType().GetField(name).GetValue(visual);
    }
}
