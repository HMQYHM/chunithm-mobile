using System;
using System.Collections.Generic;

namespace ChunithmMobile.Chart
{
    public enum NoteKind { Tap, ExTap, Flick, Hold, HeavenHold, Slide, Air, AirHold, AirAction, AirLadder, AirLine, Mine }
    public enum AirDirection { None, Up, UpLeft, UpRight, Down, DownLeft, DownRight }
    public enum Judgement { JusticeCritical, Justice, Attack, Miss }

    [Serializable]
    public sealed class ChartNote
    {
        public NoteKind Kind;
        public double TimeMs;
        public double EndTimeMs;
        public int Lane;
        public int Width;
        public int EndLane;
        public int EndWidth;
        public AirDirection AirDirection;
        public float AirHeight = 5f;
        public float EndAirHeight = 5f;
        public bool VisibleHead = true;
        // Visual ownership is separate from checkpoint allocation: a curve
        // can start with an SLC record that has no standalone head judgement.
        public bool HasVisualHead = true;
        public bool IsContinuation;
        // A connected SLD bend can retain its authored visual head and timing
        // allocation while using coverage-only judgement at the junction.
        public bool IsSlideCheckpoint;
        public int LongChainId = -1;
        public bool IsLongChainHead;
        public bool IsLongChainTail;
        public bool VisibleTail;
        public bool AirFlick;
        // Preserve the authored opcode because several C2S records share a
        // normalized runtime shape but have different head/body semantics.
        public string OriginalType = "";
        public string SourceType = "";
        public string AirLineColor = "DEF";
        // ALD column 5 is the authored AIR.LADDER sample interval in chart
        // ticks. Zero identifies a non-scoring decorative AIR line.
        public int AirLadderStyle;
        public string AirLadderSecondaryColor = "DEF";
        public int JudgePointCount = 1;
        // AIR.LADDER points use a chain-wide tick grid and cannot be rebuilt
        // by evenly splitting every individual ALD geometry segment.
        public List<double> ExplicitJudgeTimesMs;
        public int ScrollProfileId;
    }

    [Serializable]
    public struct ScrollEvent
    {
        public double StartMs;
        public double EndMs;
        public double Multiplier;

        public ScrollEvent(double startMs, double endMs, double multiplier)
        {
            StartMs = startMs;
            EndMs = endMs;
            Multiplier = multiplier;
        }
    }

    public struct ScrollAssignment
    {
        public double StartMs;
        public double EndMs;
        public int Lane;
        public int Width;
        public int ScrollProfileId;

        public ScrollAssignment(double startMs, double endMs, int lane, int width, int scrollProfileId)
        {
            StartMs = startMs; EndMs = endMs; Lane = lane; Width = width; ScrollProfileId = scrollProfileId;
        }
    }

    public sealed class ParsedChart
    {
        public readonly List<ChartNote> Notes;
        public readonly double DurationMs;
        public readonly double Bpm;
        public readonly int OriginalJudgeTotal;
        public readonly List<ScrollEvent> ScrollEvents;
        public readonly Dictionary<int, List<ScrollEvent>> FieldScrollProfiles;
        public readonly List<ScrollAssignment> ScrollAssignments;

        public ParsedChart(List<ChartNote> notes, double durationMs, double bpm,
            int originalJudgeTotal, List<ScrollEvent> scrollEvents,
            Dictionary<int, List<ScrollEvent>> fieldScrollProfiles, List<ScrollAssignment> scrollAssignments)
        {
            Notes = notes;
            DurationMs = durationMs;
            Bpm = bpm;
            OriginalJudgeTotal = originalJudgeTotal;
            ScrollEvents = scrollEvents;
            FieldScrollProfiles = fieldScrollProfiles;
            ScrollAssignments = scrollAssignments;
        }
    }
}
