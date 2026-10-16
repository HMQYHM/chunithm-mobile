using System;
using System.Collections;
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
                Require(spans.Length==2 && spans[0].StartMs==500 && spans[0].EndMs==1800 && spans[1].StartMs==900 && spans[1].EndMs==2000,"independent long lifetimes merged");
                Require(plan.All(e=>e.Loop),"long head/checkpoint/tail emitted extra one-shots");
                CheckLimiterAndChains();
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
                realtime.Bind(session,chart,bank,false,1);Require(root.GetComponents<AudioSource>().Length==12,"manual voice pool missing/bound exceeded");
                Require(root.GetComponents<AudioSource>().All(v => v.priority == GameplayJudgementAudio.AudioPriority),
                    "judgement voices must remain lower priority than the song");
                Require(GameplayJudgementAudio.AudioPriority > LocalChartPlaybackSession.MusicAudioPriority,
                    "song priority must beat judgement voices");
                // Exercise the realtime entry with delayed callbacks: window keys
                // come from the chart point, not the frame executing this method.
                session.Clock.Play(1000);
                var onJudged = typeof(GameplayJudgementAudio).GetMethod("OnJudged",Private);
                for (int i=0;i<7;i++) onJudged.Invoke(realtime,new object[]{new ScheduledJudgePoint(0,0,1000),new JudgementResult(Grade.JusticeCritical,0)});
                var gate = (JudgementAudioLimiter)typeof(GameplayJudgementAudio).GetField("limiter",Private).GetValue(realtime);
                Require(!gate.TryAccept(60,1000),"realtime chord exceeded four");
                Require(gate.TryAccept(59,1000) && gate.TryAccept(60,1001),"realtime categories/windows merged");
                realtime.Silence();Require(root.GetComponents<AudioSource>().All(v=>!v.isPlaying),"pause/stop left cues playing");
                realtime.Clear();Require(!realtime.UsesBakedTrack,"session state leaked");
                CheckRealtimeLongLifecycle(realtime,session,bank);
                // Chunk continuity, clipped negative offset and resampling/loop
                // phase must match one-pass PCM output, independent of frame size.
                var layer=new GameplayAudioMixLayer(impulse,2000,-1,5,true,1);
                var all=new float[12];layer.AddTo(all,0,1);
                var a=new float[5];var b=new float[7];layer.AddTo(a,0,1);layer.AddTo(b,5,1);
                Require(all.SequenceEqual(a.Concat(b)),"loop phase changed at chunk seam");
                Debug.Log("Judgement audio regression passed: original PCM availability, grade/direction/mine/ALD mapping, KEEP suppression, long lifetimes, per-cue four-voice cap, AUTO mix toggles/offsets, bounded manual voices, duplicate guard, pause, chunk/resample continuity.");
            } finally {bank.Dispose();if(impulse!=null)UnityEngine.Object.DestroyImmediate(impulse);if(song!=null)UnityEngine.Object.DestroyImmediate(song);UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckRealtimeLongLifecycle(GameplayJudgementAudio realtime, LocalChartPlaybackSession session, JudgementAudioBank bank)
        {
            var chain=new List<ChartNote>();
            for(int i=0;i<6;i++)chain.Add(new ChartNote {Kind=NoteKind.Slide,LongChainId=12,TimeMs=500+i*200,
                EndTimeMs=700+i*200,JudgePointCount=5,IsContinuation=i>0,IsSlideCheckpoint=i>0});
            realtime.Bind(session,Chart(chain),bank,false,1);session.Clock.Play(500);
            var judged=typeof(GameplayJudgementAudio).GetMethod("OnJudged",Private);
            var update=typeof(GameplayJudgementAudio).GetMethod("UpdateLongVoices",Private);
            judged.Invoke(realtime,new object[]{new ScheduledJudgePoint(0,0,500),new JudgementResult(Grade.Justice,0)});
            update.Invoke(realtime,new object[]{500.0});
            var voices=(IList)typeof(GameplayJudgementAudio).GetField("longs",Private).GetValue(realtime);
            Require(voices.Count==1,"realtime split chain into segment voices");
            object voice=voices[0];var type=voice.GetType();
            object source=type.GetField("Source").GetValue(voice);
            Require(source!=null && (int)type.GetField("Cue").GetValue(voice)==94,"head grade did not select sustained cue");
            judged.Invoke(realtime,new object[]{new ScheduledJudgePoint(3,0,1100),new JudgementResult(Grade.Miss,0)});
            update.Invoke(realtime,new object[]{1100.0});
            Require(ReferenceEquals(source,type.GetField("Source").GetValue(voice)) && (int)type.GetField("Cue").GetValue(voice)==94,
                "checkpoint restarted/changed long cue");
            realtime.Silence();update.Invoke(realtime,new object[]{1501.0});
            Require(ReferenceEquals(source,type.GetField("Source").GetValue(voice)),"pause created a second lifetime source");
            update.Invoke(realtime,new object[]{1700.0});
            Require(type.GetField("Source").GetValue(voice)==null,"sustained cue outlived chain tail");
            update.Invoke(realtime,new object[]{1800.0});
            Require(type.GetField("Source").GetValue(voice)==null,"completed chain restarted");
            // A hidden AIR entrance with no contact on its first frame must
            // still admit its one lifetime sound when the result arrives later.
            realtime.Bind(session,Chart(new List<ChartNote>{new ChartNote {Kind=NoteKind.AirHold,TimeMs=500,EndTimeMs=1700,
                VisibleHead=false,IsContinuation=true,JudgePointCount=5}}),bank,false,1);
            update.Invoke(realtime,new object[]{500.0});
            voices=(IList)typeof(GameplayJudgementAudio).GetField("longs",Private).GetValue(realtime);
            voice=voices[0];type=voice.GetType();
            Require(!(bool)type.GetField("Started").GetValue(voice),"hidden AIR prematurely committed a silent lifetime");
            judged.Invoke(realtime,new object[]{new ScheduledJudgePoint(0,0,500),new JudgementResult(Grade.JusticeCritical,0)});
            update.Invoke(realtime,new object[]{520.0});
            Require(type.GetField("Source").GetValue(voice)!=null,"hidden AIR never started after delayed contact");
            var overlaps=new List<ChartNote>();
            for(int i=0;i<7;i++)overlaps.Add(new ChartNote {Kind=NoteKind.Hold,TimeMs=500,EndTimeMs=1700,JudgePointCount=5});
            realtime.Bind(session,Chart(overlaps),bank,false,1);
            for(int i=0;i<7;i++)judged.Invoke(realtime,new object[]{new ScheduledJudgePoint(i,0,500),new JudgementResult(Grade.JusticeCritical,0)});
            update.Invoke(realtime,new object[]{500.0});
            var loops=(IList)typeof(GameplayJudgementAudio).GetField("loops",Private).GetValue(realtime);
            Require(loops.Count==4,"realtime long chord did not admit exactly four voices");
            realtime.Clear();
        }
        private static void CheckLimiterAndChains()
        {
            var limiter = new JudgementAudioLimiter();
            for(int i=0;i<7;i++) Require(limiter.TryAccept(60,1000.1)==(i<4),"same cue/window limit");
            Require(limiter.TryAccept(59,1000.1) && limiter.TryAccept(60,1001.1),"different cue/time incorrectly combined");
            Require(!limiter.TryAccept(60,1000.8),"delayed same-window event evaded cap");
            limiter.Clear();Require(limiter.TryAccept(60,1000),"limiter leaked across sessions");
            var notes=new List<ChartNote>();
            for(int i=0;i<7;i++) notes.Add(new ChartNote {Kind=NoteKind.Tap,TimeMs=1000,JudgePointCount=1});
            notes.Add(new ChartNote {Kind=NoteKind.Flick,TimeMs=1000,JudgePointCount=1});
            notes.Add(new ChartNote {Kind=NoteKind.Tap,TimeMs=1002,JudgePointCount=1});
            var chord=Chart(notes); int points=JudgePointScheduler.Build(chord).Count;
            var sounds=AutoJudgementAudioPlan.Build(chord);
            Require(sounds.Count(e=>e.Cue==60 && e.StartMs==1000)==4 && sounds.Any(e=>e.Cue==99) &&
                sounds.Any(e=>e.StartMs==1002),"AUTO cue cap/categories/windows");
            Require(JudgePointScheduler.Build(chord).Count==points && chord.Notes.Count==9,"audio changed score allocation");
            foreach(NoteKind kind in new[]{NoteKind.Hold,NoteKind.HeavenHold,NoteKind.Slide,NoteKind.AirHold,NoteKind.AirAction}) {
                var chain=new List<ChartNote>();
                for(int i=0;i<6;i++) chain.Add(new ChartNote {Kind=kind,LongChainId=42,TimeMs=500+i*200,
                    EndTimeMs=700+i*200,JudgePointCount=5,IsContinuation=i>0,IsSlideCheckpoint=i>0,
                    IsLongChainHead=i==0,IsLongChainTail=i==5});
                var events=AutoJudgementAudioPlan.Build(Chart(chain));
                Require(events.Count==1 && events[0].Loop && events[0].StartMs==500 && events[0].EndMs==1700,
                    "chain nodes restarted sound: "+kind);
            }
            var overlaps=new List<ChartNote>();
            for(int i=0;i<7;i++) overlaps.Add(new ChartNote {Kind=NoteKind.Hold,TimeMs=500,EndTimeMs=2000,JudgePointCount=4});
            Require(AutoJudgementAudioPlan.Build(Chart(overlaps)).Count==4,"independent simultaneous longs not capped at four");
        }
        private static ParsedChart Chart(List<ChartNote> notes) => new ParsedChart(notes,3000,120,notes.Sum(n=>n.JudgePointCount),new List<ScrollEvent>(),new Dictionary<int,List<ScrollEvent>>(),new List<ScrollAssignment>());
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
