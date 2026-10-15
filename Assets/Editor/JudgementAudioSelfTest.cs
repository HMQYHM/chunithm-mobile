using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using UnityEngine;
using Grade = ChunithmMobile.Chart.Judgement;

namespace ChunithmMobile.Editor
{
    public static class JudgementAudioSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            var root = new GameObject("Judgement audio regression");
            var bank = new JudgementAudioBank();
            AudioClip impulse = null, song = null;
            try {
                bank.LoadAsync().GetAwaiter().GetResult();
                foreach (int cue in JudgementAudioPolicy.Cues) {
                    AudioClip clip = bank.Get(cue);
                    Require(clip != null && clip.loadState == AudioDataLoadState.Loaded, "cue unavailable " + cue);
                    var pcm = new float[clip.samples * clip.channels];
                    Require(clip.GetData(pcm, 0) && pcm.Any(v => Math.Abs(v) > .0001f), "cue silent/unreadable " + cue);
                }
                var tap = new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000 };
                foreach (var pair in new[] { (Grade.JusticeCritical,60), (Grade.Justice,59), (Grade.Attack,50), (Grade.Miss,61) })
                    Require(JudgementAudioPolicy.OneShot(tap, 0, pair.Item1) == pair.Item2, "TAP grade table");
                Require(JudgementAudioPolicy.OneShot(new ChartNote { Kind=NoteKind.Flick },0,Grade.Attack)==97, "FLICK table");
                Require(JudgementAudioPolicy.OneShot(new ChartNote { Kind=NoteKind.Air,AirDirection=AirDirection.Down },0,Grade.Justice)==306,"AIR down");
                Require(JudgementAudioPolicy.OneShot(new ChartNote { Kind=NoteKind.Air },0,Grade.Miss)==0,"AIR miss must stay silent");
                Require(JudgementAudioPolicy.OneShot(new ChartNote { Kind=NoteKind.Mine },0,Grade.Miss)==303,"MINE must not play calibration sound");
                Require(JudgementAudioPolicy.OneShot(new ChartNote { Kind=NoteKind.AirAction,OriginalType="ALD" },0,Grade.JusticeCritical)==578,"ALD table");
                var hold = new ChartNote { Kind=NoteKind.Hold,TimeMs=500,EndTimeMs=1800,VisibleTail=true,JudgePointCount=6 };
                Require(JudgementAudioPolicy.OneShot(hold,1,Grade.JusticeCritical)==0,"KEEP repeated a one-shot");
                var longChart = Chart(new List<ChartNote> { hold,new ChartNote { Kind=NoteKind.Hold,TimeMs=900,EndTimeMs=2000,JudgePointCount=5 } });
                var plan=AutoJudgementAudioPlan.Build(longChart);
                var spans=plan.Where(e=>e.Loop).ToArray();
                Require(spans.Length==1 && spans[0].StartMs==500 && spans[0].EndMs==2000,"long overlaps not unioned");
                Require(plan.Any(e=>!e.Loop && e.StartMs==1800),"tail omitted because scheduler has no tail checkpoint");
                // A deterministic impulse isolates event timing from original
                // waveform attack envelopes; production still uses original PCM.
                impulse=AudioClip.Create("test cue",2,1,1000,false);impulse.SetData(new[]{.25f,0f},0);
                var clips=(Dictionary<int,AudioClip>)typeof(JudgementAudioBank).GetField("clips",Private).GetValue(bank);
                foreach(int cue in JudgementAudioPolicy.Cues) clips[cue]=impulse;
                song=AudioClip.Create("test song",3000,1,1000,false);
                var shell=root.AddComponent<AppUiShell>();
                var settings=new AppSettings { MusicVolume=100, AnswerSoundEnabled=true,AnswerSoundVolume=100,KeySoundEnabled=true,KeySoundVolume=100,MusicOffsetMs=50,AnswerSoundOffsetMs=20 };
                typeof(AppUiShell).GetField("settings",Private).SetValue(shell,settings);
                root.AddComponent<ChartPlaybackRenderer>();root.AddComponent<GameplayJudgeController>();
                var session=root.AddComponent<LocalChartPlaybackSession>();
                typeof(LocalChartPlaybackSession).GetField("fullAutoAudio",Private).SetValue(session,true);
                var mix=typeof(LocalChartPlaybackSession).GetMethod("BuildMixedSongClip",Private);
                var chart=Chart(new List<ChartNote>{tap});
                foreach(var state in new[]{(true,true),(false,true),(true,false),(false,false)}) {
                    settings.AnswerSoundEnabled=state.Item1; settings.KeySoundEnabled=state.Item2;
                    AudioClip combined=(AudioClip)mix.Invoke(session,new object[]{song,chart,impulse,50,bank});
                    if(!state.Item1&&!state.Item2) {Require(combined==null,"disabled layers produced a mix");continue;}
                    var data=new float[3000];combined.GetData(data,0);
                    Require(Math.Abs(data[950]-(state.Item2?.25f:0f))<.001f,"AUTO judgement offset/toggle");
                    Require(Math.Abs(data[1003]-(state.Item1?.25f:0f))<.001f,"ANSWER offset applied twice");
                    UnityEngine.Object.DestroyImmediate(combined);
                }
                var realtime=root.AddComponent<GameplayJudgementAudio>();
                realtime.Bind(session,chart,bank,true,1);Require(realtime.UsesBakedTrack,"AUTO duplicate guard");
                Require(root.GetComponents<AudioSource>().Length==0,"AUTO allocated realtime voices");
                realtime.Bind(session,chart,bank,false,1);Require(root.GetComponents<AudioSource>().Length==15,"manual voice pool missing/bound exceeded");
                realtime.Silence();Require(root.GetComponents<AudioSource>().All(v=>!v.isPlaying),"pause/stop left cues playing");
                realtime.Clear();Require(!realtime.UsesBakedTrack,"session state leaked");
                // Chunk continuity, clipped negative offset and resampling/loop
                // phase must match one-pass PCM output, independent of frame size.
                var layer=new GameplayAudioMixLayer(impulse,2000,-1,5,true,1);
                var all=new float[12];layer.AddTo(all,0,1);
                var a=new float[5];var b=new float[7];layer.AddTo(a,0,1);layer.AddTo(b,5,1);
                Require(all.SequenceEqual(a.Concat(b)),"loop phase changed at chunk seam");
                Debug.Log("Judgement audio regression passed: original PCM availability, grade/direction/mine/ALD mapping, KEEP suppression, long union/tail, AUTO mix toggles/offsets, bounded manual voices, duplicate guard, pause, chunk/resample continuity.");
            } finally {bank.Dispose();if(impulse!=null)UnityEngine.Object.DestroyImmediate(impulse);if(song!=null)UnityEngine.Object.DestroyImmediate(song);UnityEngine.Object.DestroyImmediate(root);}
        }
        private static ParsedChart Chart(List<ChartNote> notes) => new ParsedChart(notes,3000,120,notes.Sum(n=>n.JudgePointCount),new List<ScrollEvent>(),new Dictionary<int,List<ScrollEvent>>(),new List<ScrollAssignment>());
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
