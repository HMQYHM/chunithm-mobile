using System;
using System.Collections.Generic;
using ChunithmMobile.Chart;
using ChunithmMobile.Judgement;

namespace ChunithmMobile.Rendering
{
    /// <summary>
    /// Immutable, renderer-facing description of one authored note.
    ///
    /// The Windows client has a distinct NotesPreCalcManager/NotesManager
    /// boundary.  This class recreates that boundary for Unity: chart parsing
    /// remains semantic, while all decisions that affect visibility, resource
    /// families and long-note ownership are made once before the first frame.
    /// It deliberately does not create GameObjects or meshes.
    /// </summary>
    public enum OriginalNoteVisualKind
    {
        Tap, ExTap, Flick, Mine, Hold, Slide, Air, AirHold, AirAction, HeavenHold
    }

    public sealed class OriginalNoteTrack
    {
        public readonly int Index;
        public readonly ChartNote Note;
        public readonly OriginalNoteVisualKind VisualKind;
        public readonly int ChainOwnerIndex;
        public readonly bool IsBodyOwner;
        public readonly bool IsHeadOwner;
        public readonly bool IsTailOwner;
        public readonly bool HasGroundHead;
        public readonly bool HasAirHead;
        public readonly string HeadResource;
        public readonly string BodyResource;
        public readonly string TailResource;
        public readonly double HeadVisibleUntilMs;
        public readonly double BodyVisibleUntilMs;
        public readonly double TailVisibleUntilMs;
        public readonly int HeadProfileId;
        public readonly int EndProfileId;

        internal OriginalNoteTrack(int index, ChartNote note, int chainOwnerIndex,
            bool isBodyOwner, bool isHeadOwner, bool isTailOwner,
            string headResource, string bodyResource, string tailResource,
            int headProfileId, int endProfileId)
        {
            Index = index;
            Note = note;
            VisualKind = ToVisualKind(note.Kind);
            ChainOwnerIndex = chainOwnerIndex;
            IsBodyOwner = isBodyOwner;
            IsHeadOwner = isHeadOwner;
            IsTailOwner = isTailOwner;
            HasGroundHead = note.Kind is NoteKind.Tap or NoteKind.ExTap or NoteKind.Flick or
                NoteKind.Mine or NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide;
            HasAirHead = note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction;
            HeadResource = headResource;
            BodyResource = bodyResource;
            TailResource = tailResource;
            // Keep the hard gameplay window as the renderer lifetime. The
            // selected MOD may widen judgement, but it must not make a head
            // disappear before the authoritative default miss boundary.
            HeadVisibleUntilMs = note.TimeMs + MissWindow(note, true);
            BodyVisibleUntilMs = note.EndTimeMs;
            TailVisibleUntilMs = note.EndTimeMs + 260.0;
            HeadProfileId = headProfileId;
            EndProfileId = endProfileId;
        }

        private static double MissWindow(ChartNote note, bool hard)
        {
            return note.Kind is NoteKind.Air or NoteKind.AirHold or NoteKind.AirAction
                ? JudgementEngine.MissWindowMs(note.Kind,
                    note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight,
                    note.AirFlick, hard)
                : JudgementEngine.MissWindowMs(note, hard);
        }

        private static OriginalNoteVisualKind ToVisualKind(NoteKind kind) => kind switch
        {
            NoteKind.ExTap => OriginalNoteVisualKind.ExTap,
            NoteKind.Flick => OriginalNoteVisualKind.Flick,
            NoteKind.Mine => OriginalNoteVisualKind.Mine,
            NoteKind.Hold => OriginalNoteVisualKind.Hold,
            NoteKind.Slide => OriginalNoteVisualKind.Slide,
            NoteKind.HeavenHold => OriginalNoteVisualKind.HeavenHold,
            NoteKind.Air => OriginalNoteVisualKind.Air,
            NoteKind.AirHold => OriginalNoteVisualKind.AirHold,
            NoteKind.AirAction => OriginalNoteVisualKind.AirAction,
            _ => OriginalNoteVisualKind.Tap
        };
    }

    public sealed class OriginalNotePrecalculation
    {
        private readonly OriginalNoteTrack[] tracks;
        private readonly Dictionary<int, int> chainOwners = new Dictionary<int, int>();

        public IReadOnlyList<OriginalNoteTrack> Tracks => tracks;

        private OriginalNotePrecalculation(OriginalNoteTrack[] tracks)
        {
            this.tracks = tracks;
        }

        public OriginalNoteTrack this[int index] => tracks[index];

        public static OriginalNotePrecalculation Build(ParsedChart chart)
        {
            if (chart == null || chart.Notes == null)
                throw new ArgumentNullException(nameof(chart));

            var ownerByChain = new Dictionary<int, int>();
            for (int i = 0; i < chart.Notes.Count; i++) {
                int chain = chart.Notes[i].LongChainId;
                if (chain < 0) continue;
                if (!ownerByChain.ContainsKey(chain)) ownerByChain[chain] = i;
            }

            var result = new OriginalNoteTrack[chart.Notes.Count];
            for (int i = 0; i < chart.Notes.Count; i++) {
                ChartNote note = chart.Notes[i];
                int owner = note.LongChainId >= 0 && ownerByChain.TryGetValue(note.LongChainId, out int value)
                    ? value : i;
                bool longBody = IsLong(note);
                bool bodyOwner = longBody && owner == i;
                bool headOwner = note.HasVisualHead && !note.IsContinuation;
                bool tailOwner = longBody && note.IsLongChainTail && note.VisibleTail;
                ResolveResources(note, out string head, out string body, out string tail);
                result[i] = new OriginalNoteTrack(i, note, owner, bodyOwner, headOwner, tailOwner,
                    head, body, tail, note.ScrollProfileId, note.ScrollProfileId);
            }
            return new OriginalNotePrecalculation(result);
        }

        public bool TryGetChainOwner(int index, out int owner)
        {
            if (index >= 0 && index < tracks.Length) {
                owner = tracks[index].ChainOwnerIndex;
                return true;
            }
            owner = -1;
            return false;
        }

        private static bool IsLong(ChartNote note) => note.EndTimeMs > note.TimeMs &&
            note.Kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide or
            NoteKind.AirHold or NoteKind.AirAction;

        private static void ResolveResources(ChartNote note, out string head, out string body, out string tail)
        {
            head = "tap"; body = ""; tail = "";
            switch (note.Kind) {
                case NoteKind.ExTap: head = "extap"; break;
                case NoteKind.Flick: head = "flick"; break;
                case NoteKind.Mine: head = "dmg"; break;
                case NoteKind.Hold:
                    head = string.Equals(note.OriginalType, "HXD", StringComparison.OrdinalIgnoreCase) ? "extap" : "hold";
                    body = "hold_joint"; tail = "holdend"; break;
                case NoteKind.Slide: head = "slide"; body = "slide_joint"; tail = "slideend"; break;
                case NoteKind.HeavenHold: head = "ahroof"; body = "heaven_roof"; tail = "ahroof"; break;
                case NoteKind.Air: head = "air_" + AirFamily(note.AirDirection); break;
                case NoteKind.AirHold: head = "ahtap"; body = "air_hold_joint"; tail = "ahtapend"; break;
                case NoteKind.AirAction: head = "ahact"; body = "air_action"; tail = "ahtapend"; break;
            }
        }

        private static string AirFamily(AirDirection direction) => direction switch
        {
            AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight => "down",
            _ => "up"
        };
    }
}
