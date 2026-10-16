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
    /// KEEP scoring ticks never restart a sustained lifetime or emit extra shots.
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
            // One lifetime owns the sustained feedback, including its head.
            // Geometric junctions and KEEP/tail results never create extra sounds.
            if (IsLong(note)) return 0;
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

    /// <summary>Same cue in the same 1 ms chart-time window admits four voices.
    /// Authored timestamps keep chords grouped even if judgements arrive on
    /// different render frames. Categories and scoring are completely separate.</summary>
    public sealed class JudgementAudioLimiter
    {
        public const int MaxVoicesPerCue = 4;
        private readonly Dictionary<(int cue, long window), int> counts = new Dictionary<(int, long), int>();
        public bool TryAccept(int cue, double chartMs)
        {
            if (cue == 0) return false;
            var key = (cue, (long)Math.Floor(chartMs));
            counts.TryGetValue(key, out int count);
            if (count >= MaxVoicesPerCue) return false;
            counts[key] = count + 1;
            return true;
        }
        public void Clear() => counts.Clear();
    }

    /// <summary>A parsed chain, not a ribbon segment, owns one feedback lifetime.
    /// Families prevent unrelated ground/AIR records from sharing an owner.</summary>
    public sealed class LongJudgementAudioLifetime
    {
        public int Family;
        public double StartMs = double.PositiveInfinity, EndMs;
        public readonly List<int> Members = new List<int>();
        public static List<LongJudgementAudioLifetime> Build(ParsedChart chart)
        {
            var result = new List<LongJudgementAudioLifetime>();
            var chains = new Dictionary<(int family, int chain), LongJudgementAudioLifetime>();
            for (int i = 0; i < chart.Notes.Count; i++) {
                ChartNote note = chart.Notes[i];
                if (!JudgementAudioPolicy.IsLong(note)) continue;
                int family = JudgementAudioPolicy.Family(note);
                // Negative chain IDs describe independent objects, not one shared chain.
                var key = (family, note.LongChainId >= 0 ? note.LongChainId : -i - 1);
                if (!chains.TryGetValue(key, out var life)) {
                    life = new LongJudgementAudioLifetime { Family = family };
                    chains.Add(key, life); result.Add(life);
                }
                life.Members.Add(i);
                life.StartMs = Math.Min(life.StartMs, note.TimeMs);
                life.EndMs = Math.Max(life.EndMs, note.EndTimeMs);
            }
            result.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            return result;
        }
    }

    public static class AutoJudgementAudioPlan
    {
        public static List<JudgementAudioEvent> Build(ParsedChart chart)
        {
            var candidates = new List<JudgementAudioEvent>();
            foreach (ScheduledJudgePoint point in JudgePointScheduler.Build(chart)) {
                int cue = JudgementAudioPolicy.OneShot(chart.Notes[point.NoteIndex], point.PointIndex, Grade.JusticeCritical);
                if (cue != 0) candidates.Add(new JudgementAudioEvent(cue, point.TimeMs));
            }
            foreach (var life in LongJudgementAudioLifetime.Build(chart))
                candidates.Add(new JudgementAudioEvent(JudgementAudioPolicy.LoopCue(life.Family,
                    Grade.JusticeCritical), life.StartMs, life.EndMs, true));
            candidates.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            var result = new List<JudgementAudioEvent>();
            var limiter = new JudgementAudioLimiter();
            foreach (var sound in candidates)
                if (limiter.TryAccept(sound.Cue, sound.StartMs)) result.Add(sound);
            return result;
        }
    }

    /// <summary>Full AUTO uses only the baked track. Manual feedback owns pooled
    /// voices and one sustained source per admitted chain, until its authored end.</summary>
    public sealed class GameplayJudgementAudio : MonoBehaviour
    {
        // Unity uses a smaller number for a higher-priority voice. Judgement
        // feedback must never virtualize the song or other gameplay audio on
        // devices with a limited hardware voice budget.
        public const int AudioPriority = 240;
        private const int InitialShotSources = 12;
        private const int MaxShotSources = 24;

        private sealed class LongVoice
        {
            public LongJudgementAudioLifetime Life;
            public bool Started;
            public int Cue;
            public AudioSource Source;
            public Grade? HeadGrade;
        }
        private readonly List<AudioSource> shots = new List<AudioSource>();
        private readonly List<AudioSource> loops = new List<AudioSource>();
        private readonly List<LongVoice> longs = new List<LongVoice>();
        private readonly Dictionary<int, LongVoice> longByNote = new Dictionary<int, LongVoice>();
        private readonly JudgementAudioLimiter limiter = new JudgementAudioLimiter();
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
            for (int i = 0; i < InitialShotSources; i++) shots.Add(CreateSource(false));
            foreach (var life in LongJudgementAudioLifetime.Build(chart)) {
                var voice = new LongVoice { Life = life };
                longs.Add(voice);
                foreach (int index in life.Members) longByNote[index] = voice;
            }
        }
        private AudioSource CreateSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 0;
            source.priority = AudioPriority;
            source.volume = volume; source.pitch = (float)session.Clock.Rate; return source;
        }
        private void OnJudged(ScheduledJudgePoint point, JudgementResult result)
        {
            if (baked || chart == null || volume <= 0 || !session.Clock.Playing) return;
            if (longByNote.TryGetValue(point.NoteIndex, out var voice)) {
                // Only the head verdict selects the lifetime cue. Intermediate
                // misses/checkpoints still score normally, but cannot restart it.
                if (!voice.Started && point.PointIndex == 0 &&
                    Math.Abs(chart.Notes[point.NoteIndex].TimeMs - voice.Life.StartMs) < .001)
                    voice.HeadGrade = result.Grade;
                return;
            }
            PlayCue(JudgementAudioPolicy.OneShot(chart.Notes[point.NoteIndex], point.PointIndex, result.Grade), point.TimeMs);
        }
        private void PlayCue(int cue, double chartMs)
        {
            if (bank == null || !limiter.TryAccept(cue, chartMs) ||
                PlayingCueCount(cue) >= JudgementAudioLimiter.MaxVoicesPerCue) return;
            AudioSource target = shots.Find(source => !source.isPlaying);
            // Grow only when necessary; never stop a different cue to reuse its voice.
            // Keep the realtime feedback pool bounded. A dense chord is
            // dropped after the per-cue limiter instead of creating enough
            // AudioSources to consume the device voice budget.
            if (target == null && shots.Count < MaxShotSources) {
                target = CreateSource(false); shots.Add(target);
            }
            if (target == null) return;
            target.clip = bank.Get(cue); target.Play();
        }

        private int PlayingCueCount(int cue)
        {
            AudioClip clip = bank?.Get(cue);
            if (clip == null) return 0;
            int count = 0;
            foreach (var source in shots)
                if (source != null && source.isPlaying && source.clip == clip) count++;
            foreach (var source in loops)
                if (source != null && source.isPlaying && source.clip == clip) count++;
            return count;
        }
        private void Update()
        {
            if (chart == null || baked || bank == null || session == null || judge == null || volume <= 0) return;
            if (!session.Clock.Playing || session.Clock.Preparing) { Silence(); return; }
            UpdateLongVoices(session.Clock.TimeMs);
        }
        private void UpdateLongVoices(double now)
        {
            foreach (var voice in longs) {
                var life = voice.Life;
                if (now >= life.EndMs) {
                    if (voice.Source != null) { voice.Source.Stop(); voice.Source = null; }
                    voice.Started = true;
                    continue;
                }
                if (now < life.StartMs) continue;
                if (!voice.Started) {
                    Grade grade = voice.HeadGrade ?? Grade.JusticeCritical;
                    if (!voice.HeadGrade.HasValue) {
                        bool covered = false, pendingHead = false;
                        foreach (int index in life.Members) {
                            var note = chart.Notes[index];
                            if (Math.Abs(note.TimeMs - life.StartMs) < .001 && note.VisibleHead &&
                                !note.IsContinuation && !note.IsSlideCheckpoint && note.JudgePointCount > 0)
                                pendingHead = true;
                            if (now >= note.TimeMs && now < note.EndTimeMs && judge.IsLongAudioCovered(index, now)) covered = true;
                        }
                        // Wait for the head result when one exists; hidden-head
                        // AIR/curve chains use coverage at their lifetime entrance.
                        // A hidden-head chain may be contacted a frame later.
                        // Absence of coverage is not a permanent silent onset.
                        if (pendingHead || !covered) continue;
                        grade = Grade.JusticeCritical;
                    }
                    voice.Started = true;
                    voice.Cue = JudgementAudioPolicy.LoopCue(life.Family, grade);
                    if (!limiter.TryAccept(voice.Cue, life.StartMs)) { voice.Cue = 0; continue; }
                    if (PlayingCueCount(voice.Cue) >= JudgementAudioLimiter.MaxVoicesPerCue) {
                        voice.Cue = 0;
                        continue;
                    }
                }
                AudioClip clip = bank.Get(voice.Cue);
                if (clip == null) continue;
                if (voice.Source == null) {
                    // Reuse only sources released by a completed lifetime.
                    foreach (var source in loops) {
                        if (!longs.Exists(other => other.Source == source)) { voice.Source = source; break; }
                    }
                    if (voice.Source == null) { voice.Source = CreateSource(true); loops.Add(voice.Source); }
                }
                if (voice.Source.isPlaying) continue;
                voice.Source.clip = clip;
                // Pause/resume (or a late frame) preserves chart-relative phase;
                // it must not replay the waveform attack at every curve node.
                voice.Source.timeSamples = (int)(((now - life.StartMs) * clip.frequency / 1000.0) % clip.samples);
                voice.Source.Play();
            }
        }
        public void Silence() { foreach (var source in shots) if (source != null) source.Stop(); foreach (var source in loops) if (source != null) source.Stop(); }
        public void Clear()
        {
            if (judge != null) judge.Judged -= OnJudged;
            Silence(); foreach (var source in shots) if (source != null) Destroy(source); shots.Clear();
            foreach (var source in loops) if (source != null) Destroy(source); loops.Clear();
            longs.Clear(); longByNote.Clear(); limiter.Clear();
            chart = null; bank = null; judge = null; session = null; baked = false;
        }
        private void OnDestroy() => Clear();
    }
}
