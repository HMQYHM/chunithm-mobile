using System;
using System.Collections.Generic;
using System.Linq;
using ChunithmMobile.Chart;

namespace ChunithmMobile.Timing
{
    /// <summary>Integrates authored scroll velocity. Negative SFL moves notes backwards; STP holds them still.</summary>
    public sealed class ScrollPositionMapper
    {
        private readonly List<Segment> segments = new();
        public bool HasNonPositiveSpeed { get; }
        public double MinimumPositiveSpeed { get; }
        private struct Segment
        {
            public double Start, End, Speed, DistanceAtStart;
            public Segment(double start, double end, double speed, double distanceAtStart)
            { Start = start; End = end; Speed = speed; DistanceAtStart = distanceAtStart; }
        }

        public ScrollPositionMapper(double chartDurationMs, IReadOnlyList<ScrollEvent> events)
        {
            var boundaries = new SortedSet<double> { 0, chartDurationMs };
            foreach (var e in events) { boundaries.Add(e.StartMs); boundaries.Add(e.EndMs); }
            var times = boundaries.ToArray();
            double distance = 0;
            bool hasNonPositiveSpeed = false;
            double minimumPositiveSpeed = double.PositiveInfinity;
            for (int i = 0; i + 1 < times.Length; i++) {
                double start = times[i], end = times[i + 1];
                double speed = 1;
                // Last authored overlapping event wins, matching chart command order at a boundary.
                foreach (var e in events)
                    if (start >= e.StartMs && start < e.EndMs) speed = e.Multiplier;
                segments.Add(new Segment(start, end, speed, distance));
                if (speed <= 0) hasNonPositiveSpeed = true;
                else minimumPositiveSpeed = Math.Min(minimumPositiveSpeed, speed);
                distance += (end - start) * speed;
            }
            HasNonPositiveSpeed = hasNonPositiveSpeed;
            MinimumPositiveSpeed = double.IsPositiveInfinity(minimumPositiveSpeed) ? 1 : minimumPositiveSpeed;
        }

        public double DistanceAt(double timeMs)
        {
            if (segments.Count == 0) return timeMs;
            int lo = 0, hi = segments.Count - 1;
            while (lo <= hi) {
                int mid = (lo + hi) >> 1; var s = segments[mid];
                if (timeMs < s.Start) hi = mid - 1;
                else if (timeMs >= s.End) lo = mid + 1;
                else return s.DistanceAtStart + (timeMs - s.Start) * s.Speed;
            }
            var edge = timeMs < segments[0].Start ? segments[0] : segments[^1];
            return edge.DistanceAtStart + (timeMs - edge.Start) * edge.Speed;
        }

        public double RelativeDistance(double noteTimeMs, double songTimeMs) => DistanceAt(noteTimeMs) - DistanceAt(songTimeMs);
    }
}
