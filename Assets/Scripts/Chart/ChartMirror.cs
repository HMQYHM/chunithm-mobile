using System;

namespace ChunithmMobile.Chart
{
    /// <summary>
    /// Mirrors authored chart geometry for the score-counting MIRROR MOD.
    /// The source C2S remains untouched; a freshly parsed runtime chart is
    /// transformed once before rendering, judging, and answer-sound mixing.
    /// </summary>
    public static class ChartMirror
    {
        private const int LaneCount = 16;

        public static void Apply(ParsedChart chart)
        {
            if (chart == null) return;
            for (int index = 0; index < chart.Notes.Count; index++)
            {
                ChartNote note = chart.Notes[index];
                note.Lane = MirrorLane(note.Lane, note.Width);
                note.EndLane = MirrorLane(note.EndLane, note.EndWidth);
                note.AirDirection = MirrorDirection(note.AirDirection);
                if (string.Equals(note.SourceType, "L", StringComparison.OrdinalIgnoreCase))
                    note.SourceType = "R";
                else if (string.Equals(note.SourceType, "R", StringComparison.OrdinalIgnoreCase))
                    note.SourceType = "L";
            }

            for (int index = 0; index < chart.ScrollAssignments.Count; index++)
            {
                ScrollAssignment assignment = chart.ScrollAssignments[index];
                assignment.Lane = MirrorLane(assignment.Lane, assignment.Width);
                chart.ScrollAssignments[index] = assignment;
            }
        }

        private static int MirrorLane(int lane, int width)
        {
            int safeWidth = Math.Max(1, Math.Min(LaneCount, width));
            return Math.Max(0, Math.Min(LaneCount - safeWidth, LaneCount - (lane + safeWidth)));
        }

        private static AirDirection MirrorDirection(AirDirection direction)
        {
            switch (direction)
            {
                case AirDirection.UpLeft: return AirDirection.UpRight;
                case AirDirection.UpRight: return AirDirection.UpLeft;
                case AirDirection.DownLeft: return AirDirection.DownRight;
                case AirDirection.DownRight: return AirDirection.DownLeft;
                default: return direction;
            }
        }
    }
}
