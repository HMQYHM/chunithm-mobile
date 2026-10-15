using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;
using UnityEngine;
using UnityEngine.Networking;
using Grade = ChunithmMobile.Chart.Judgement;

namespace ChunithmMobile.Runtime
{
    /// <summary>One shared cue policy for actual results and Full AUTO PCM rendering.
    /// KEEP scoring ticks update sustained coverage; they are not repeated one-shot sounds.
    /// Cue IDs follow the verified Windows table. CRI AISAC/DSP is not a Unity effect.</summary>
    public static class JudgementAudioPolicy
    {
        public static readonly int[] Cues = { 50,59,60,61,71,72,80,81,82,83,58,306,89,97,98,99,303,578,84,85,90,91,92,93,94,95,407,408 };
        public static int OneShot(ChartNote note, int pointIndex, Grade grade)
        {
            if (pointIndex != 0) return 0;
            bool miss = grade == Grade.Miss;
            if (note.Kind == NoteKind.Mine) return miss ? 303 : 60;
            if (note.Kind == NoteKind.Air) return miss ? 0 :
                note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight ? 306 : 58;
            if (note.Kind == NoteKind.AirAction && note.EndTimeMs <= note.TimeMs + .001)
                return miss ? 0 : note.OriginalType == "ALD" ? 578 : 89;
            if (IsLong(note)) {
                if (note.Kind is NoteKind.AirHold or NoteKind.AirAction) return 0;
                if (note.Kind == NoteKind.Slide && note.IsSlideCheckpoint)
                    return ResultCue(note.Kind, grade);
                if (!note.VisibleHead || note.IsContinuation) return 0;
                // A ground-long head belongs to the TAP checker; its body owns
                // the HOLD/SLIDE loop, and the independently timed tail owns its end cue.
                return ResultCue(NoteKind.Tap, grade);
            }
            return ResultCue(note.Kind, grade);
        }
        public static bool IsLong(ChartNote note) => note.EndTimeMs > note.TimeMs + .001 &&
            note.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or NoteKind.AirHold or NoteKind.AirAction;
        public static int Family(ChartNote note) => note.Kind == NoteKind.Slide ? 1 :
            note.Kind is NoteKind.AirHold or NoteKind.AirAction ? 2 : 0;
        public static int LoopCue(int family, Grade grade)
        {
            if (family == 2) return grade == Grade.Miss ? 0 : 85;
            if (family == 1) return grade == Grade.Miss ? 408 : grade == Grade.Attack ? 93 : grade == Grade.Justice ? 94 : 95;
            return grade == Grade.Miss ? 407 : grade == Grade.Attack ? 90 : grade == Grade.Justice ? 91 : 92;
        }
        public static int ResultCue(NoteKind kind, Grade grade)
        {
            if (grade == Grade.Miss) return 61;
            if (kind == NoteKind.Flick) return grade == Grade.Attack ? 97 : grade == Grade.Justice ? 98 : 99;
            if (kind is NoteKind.Hold or NoteKind.HeavenHold) return grade == Grade.Attack ? 82 : grade == Grade.Justice ? 80 : 71;
            if (kind == NoteKind.Slide) return grade == Grade.Attack ? 83 : grade == Grade.Justice ? 81 : 72;
            return grade == Grade.Attack ? 50 : grade == Grade.Justice ? 59 : 60;
        }
    }

    /// <summary>A bank belongs to one preparation generation. Optional audio/cue_ID.wav
    /// skin files override individual feedback cues; missing/broken files retain built-ins.
    /// Only external clips are destroyed when that generation ends.</summary>
    public sealed class JudgementAudioBank : IDisposable
    {
        private readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
        private readonly List<AudioClip> owned = new List<AudioClip>();
        public AudioClip Get(int cue) => clips.TryGetValue(cue, out AudioClip clip) ? clip : null;
        public async Task LoadAsync()
        {
            try {
                foreach (int cue in JudgementAudioPolicy.Cues) {
                    AudioClip clip = Resources.Load<AudioClip>("Audio/Judgement/cue_" + cue);
                    clip?.LoadAudioData();
                    string path = GameplaySkinManager.GetAudioPath("cue_" + cue + ".wav");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                        try {
                            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.WAV)) {
                                var operation = request.SendWebRequest();
                                while (!operation.isDone) await Task.Yield();
                                if (request.result == UnityWebRequest.Result.Success) {
                                    AudioClip external = DownloadHandlerAudioClip.GetContent(request);
                                    if (external != null && external.samples > 0) { clip = external; owned.Add(external); }
                                } else Debug.LogWarning($"Judgement skin cue {cue} failed; using built-in: {request.error}");
                            }
                        } catch (Exception error) { Debug.LogWarning($"Judgement skin cue {cue} failed; using built-in: {error.Message}"); }
                    }
                    if (clip == null) throw new InvalidOperationException("Missing judgement cue " + cue);
                    clips[cue] = clip;
                }
            } catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (AudioClip clip in owned) if (clip != null) UnityEngine.Object.Destroy(clip); owned.Clear(); clips.Clear(); }
    }

    public readonly struct JudgementAudioEvent
    {
        public readonly int Cue;
        public readonly double StartMs, EndMs;
        public readonly bool Loop;
        public JudgementAudioEvent(int cue, double start, double end = 0, bool loop = false)
        { Cue = cue; StartMs = start; EndMs = end; Loop = loop; }
    }

    public static class AutoJudgementAudioPlan
    {
        public static List<JudgementAudioEvent> Build(ParsedChart chart)
        {
            var result = new List<JudgementAudioEvent>();
            var unique = new HashSet<string>();
            foreach (ScheduledJudgePoint point in JudgePointScheduler.Build(chart)) {
                int cue = JudgementAudioPolicy.OneShot(chart.Notes[point.NoteIndex], point.PointIndex, Grade.JusticeCritical);
                if (cue != 0 && unique.Add(cue + ":" + point.TimeMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
                    result.Add(new JudgementAudioEvent(cue, point.TimeMs));
            }
            var spans = new Dictionary<int, List<JudgementAudioEvent>>();
            foreach (ChartNote note in chart.Notes) {
                if (!JudgementAudioPolicy.IsLong(note)) continue;
                int family = JudgementAudioPolicy.Family(note);
                int cue = JudgementAudioPolicy.LoopCue(family, Grade.JusticeCritical);
                if (!spans.TryGetValue(cue, out var list)) spans[cue] = list = new List<JudgementAudioEvent>();
                list.Add(new JudgementAudioEvent(cue, note.TimeMs, note.EndTimeMs, true));
                if (family == 2 && !note.IsContinuation && unique.Add("84:" + note.TimeMs.ToString("R")))
                    result.Add(new JudgementAudioEvent(84, note.TimeMs));
                if (family != 2 && (note.LongChainId < 0 || note.IsLongChainTail) &&
                    unique.Add("tail:" + family + ":" + note.EndTimeMs.ToString("R")))
                    result.Add(new JudgementAudioEvent(JudgementAudioPolicy.ResultCue(note.Kind, Grade.JusticeCritical), note.EndTimeMs));
            }
            // The original has shared family channels. Union overlapping body
            // spans so chords/continuation records do not amplify a loop repeatedly.
            foreach (var pair in spans) {
                pair.Value.Sort((a,b) => a.StartMs.CompareTo(b.StartMs));
                double start = pair.Value[0].StartMs, end = pair.Value[0].EndMs;
                foreach (var span in pair.Value) {
                    if (span.StartMs <= end + .001) end = Math.Max(end, span.EndMs);
                    else { result.Add(new JudgementAudioEvent(pair.Key, start, end, true)); start = span.StartMs; end = span.EndMs; }
                }
                result.Add(new JudgementAudioEvent(pair.Key, start, end, true));
            }
            return result;
        }
    }

    /// <summary>Preloaded, bounded realtime voices. Full AUTO uses only the baked
    /// song source. Manual feedback follows actual grades and live long coverage.</summary>
    public sealed class GameplayJudgementAudio : MonoBehaviour
    {
        private readonly List<AudioSource> shots = new List<AudioSource>();
        private readonly AudioSource[] loops = new AudioSource[3];
        private readonly Dictionary<int, Grade> grades = new Dictionary<int, Grade>();
        private readonly HashSet<int> entered = new HashSet<int>(), ended = new HashSet<int>();
        private readonly Dictionary<int, double> lastCueMs = new Dictionary<int, double>();
        private LocalChartPlaybackSession session;
        private GameplayJudgeController judge;
        private ParsedChart chart;
        private JudgementAudioBank bank;
        private float volume;
        private bool baked;
        public bool UsesBakedTrack => baked;
        public void Bind(LocalChartPlaybackSession owner, ParsedChart data, JudgementAudioBank audio, bool fullAutoBaked, float gain)
        {
            Clear(); session = owner; chart = data; bank = audio; baked = fullAutoBaked; volume = Mathf.Clamp01(gain);
            if (baked || volume <= 0) return;
            judge = GetComponent<GameplayJudgeController>();
            if (judge != null) judge.Judged += OnJudged;
            for (int i = 0; i < 12; i++) shots.Add(CreateSource(false));
            for (int i = 0; i < loops.Length; i++) loops[i] = CreateSource(true);
        }
        private AudioSource CreateSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 0; source.priority = 0;
            source.volume = volume; source.pitch = (float)session.Clock.Rate; return source;
        }
        private void OnJudged(ScheduledJudgePoint point, JudgementResult result)
        {
            if (baked || chart == null || volume <= 0 || !session.Clock.Playing) return;
            grades[point.NoteIndex] = result.Grade;
            PlayCue(JudgementAudioPolicy.OneShot(chart.Notes[point.NoteIndex], point.PointIndex, result.Grade));
        }
        private void PlayCue(int cue)
        {
            if (cue == 0 || bank == null) return;
            // Shared same-cue chord feedback is emitted once per DSP timestamp.
            double now = session.Clock.TimeMs;
            if (lastCueMs.TryGetValue(cue, out double previous) && Math.Abs(previous - now) < .01) return;
            lastCueMs[cue] = now;
            AudioSource target = null;
            foreach (var source in shots) if (!source.isPlaying) { target = source; break; }
            if (target == null && shots.Count > 0) target = shots[0];
            if (target == null) return;
            target.Stop(); target.clip = bank.Get(cue); target.Play();
        }
        private void Update()
        {
            if (chart == null || baked || bank == null || session == null || judge == null || volume <= 0) return;
            if (!session.Clock.Playing || session.Clock.Preparing) { Silence(); return; }
            double now = session.Clock.TimeMs;
            int[] activeCue = {0,0,0};
            for (int i = 0; i < chart.Notes.Count; i++) {
                ChartNote note = chart.Notes[i];
                if (!JudgementAudioPolicy.IsLong(note) || now < note.TimeMs) continue;
                int family = JudgementAudioPolicy.Family(note);
                if (now >= note.EndTimeMs) {
                    if (ended.Add(i) && family != 2 && (note.LongChainId < 0 || note.IsLongChainTail))
                        PlayCue(JudgementAudioPolicy.ResultCue(note.Kind, grades.TryGetValue(i, out Grade tail) ? tail : Grade.Miss));
                    continue;
                }
                bool covered = judge.IsLongAudioCovered(i, now);
                Grade grade = covered ? grades.TryGetValue(i, out Grade last) && last != Grade.Miss ? last : Grade.JusticeCritical : Grade.Miss;
                if (covered && family == 2 && !note.IsContinuation && entered.Add(i)) PlayCue(84);
                int cue = JudgementAudioPolicy.LoopCue(family, grade);
                // Successful coverage of any same-family long keeps its shared
                // loop audible; an unrelated missed chain cannot mute that contact.
                if (covered || activeCue[family] == 0) activeCue[family] = cue;
            }
            for (int i = 0; i < loops.Length; i++) {
                AudioSource source = loops[i]; AudioClip clip = bank.Get(activeCue[i]);
                if (source.clip == clip && source.isPlaying) continue;
                source.Stop(); source.clip = clip; if (clip != null) source.Play();
            }
        }
        public void Silence() { foreach (var source in shots) if (source != null) source.Stop(); foreach (var source in loops) if (source != null) source.Stop(); }
        public void Clear()
        {
            if (judge != null) judge.Judged -= OnJudged;
            Silence(); foreach (var source in shots) if (source != null) Destroy(source); shots.Clear();
            for (int i = 0; i < loops.Length; i++) { if (loops[i] != null) Destroy(loops[i]); loops[i] = null; }
            grades.Clear(); entered.Clear(); ended.Clear(); lastCueMs.Clear();
            chart = null; bank = null; judge = null; session = null; baked = false;
        }
        private void OnDestroy() => Clear();
    }
}
