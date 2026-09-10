using System.Collections.Generic;
using ChunithmMobile.Chart;

namespace ChunithmMobile.Timing
{
    /// <summary>
    /// Original-style playback separates the global note course from local
    /// field pieces. SFL and SLP profile 0 affect every note. SLP profile 1..16
    /// affects objects inside SLA-authored time/lane assignment rectangles.
    /// </summary>
    public sealed class OriginalPlaybackMap
    {
        private readonly ScrollPositionMapper global;
        private readonly Dictionary<int, ScrollPositionMapper> fieldProfiles = new Dictionary<int, ScrollPositionMapper>();
        private readonly List<ScrollAssignment> assignments;

        public OriginalPlaybackMap(ParsedChart chart)
        {
            global = new ScrollPositionMapper(chart.DurationMs, chart.ScrollEvents);
            assignments = chart.ScrollAssignments;
            foreach (var profile in chart.FieldScrollProfiles)
                fieldProfiles[profile.Key] = new ScrollPositionMapper(chart.DurationMs, profile.Value);
        }

        // A forward time window is exact only while every scroll profile is
        // monotonic. STP/SFL reverse sections use the original full scan path.
        public bool SupportsForwardTimeWindow
        {
            get
            {
                if (global.HasNonPositiveSpeed) return false;
                foreach (ScrollPositionMapper mapper in fieldProfiles.Values)
                    if (mapper.HasNonPositiveSpeed) return false;
                return true;
            }
        }

        public double MinimumPositiveSpeed
        {
            get
            {
                double result = global.MinimumPositiveSpeed;
                foreach (ScrollPositionMapper mapper in fieldProfiles.Values)
                    result = System.Math.Min(result, mapper.MinimumPositiveSpeed);
                return result;
            }
        }

        public double NoteDistance(ChartNote note, double songTimeMs)
        {
            return DistanceForProfile(note.ScrollProfileId, note.TimeMs, songTimeMs);
        }

        public double ObjectDistance(double authoredTimeMs, int lane, int width, double songTimeMs)
        {
            int profileId = FindProfile(authoredTimeMs, lane, width);
            return DistanceForProfile(profileId, authoredTimeMs, songTimeMs);
        }

        public double ProfileDistance(int profileId, double authoredTimeMs, double songTimeMs)
        {
            return DistanceForProfile(profileId, authoredTimeMs, songTimeMs);
        }

        private double DistanceForProfile(int profileId, double authoredTimeMs, double songTimeMs)
        {
            ScrollPositionMapper mapper;
            if (profileId > 0 && fieldProfiles.TryGetValue(profileId, out mapper))
                return mapper.RelativeDistance(authoredTimeMs, songTimeMs);
            return global.RelativeDistance(authoredTimeMs, songTimeMs);
        }

        public int FindProfile(double authoredTimeMs, int lane, int width)
        {
            int endLane = lane + width;
            for (int i = assignments.Count - 1; i >= 0; i--) {
                ScrollAssignment area = assignments[i];
                if (authoredTimeMs < area.StartMs || authoredTimeMs >= area.EndMs) continue;
                if (lane < area.Lane + area.Width && area.Lane < endLane) return area.ScrollProfileId;
            }
            return 0;
        }
    }
}
