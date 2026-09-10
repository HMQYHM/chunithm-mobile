using System.Collections.Generic;
using UnityEngine;

namespace ChunithmMobile.Input
{
    /// <summary>
    /// Maps the mobile touch surface to sixteen independent cabinet zones.
    /// Every finger has its own lane state; FirstLane/LastLane describe the
    /// discrete zones crossed since the previous input sample.
    /// </summary>
    public sealed class ArcadeTouchInput : MonoBehaviour
    {
        public const int LaneCount = 16;
        [SerializeField] private Rect normalizedPlayArea = new Rect(0f, 0f, 1f, .28f);
        [SerializeField] private float touchpadWidth = 1f;
        [SerializeField, Range(.5f, 3f)] private float contactRangeLanes = 1f;

        private readonly Dictionary<int, PointerHistory> history = new Dictionary<int, PointerHistory>();
        private readonly HashSet<int> seenPointers = new HashSet<int>();
        private readonly List<int> stalePointers = new List<int>();
        private ArcadeInputFrame current = new ArcadeInputFrame();
        private ushort previousActiveMask;
        public ArcadeInputFrame Current { get { return current; } }

        public void SetPlayArea(Rect area)
        {
            normalizedPlayArea = new Rect(Mathf.Clamp01(area.x), Mathf.Clamp01(area.y),
                Mathf.Clamp01(area.width), Mathf.Clamp(area.height, .001f, 1f));
        }

        public void ResetCaptureState()
        {
            history.Clear();
            seenPointers.Clear();
            stalePointers.Clear();
            previousActiveMask = 0;
            current.Ground.Clear();
        }

        public void SetTouchpadWidth(float width)
        {
            touchpadWidth = Mathf.Clamp(width, .6f, 1f);
            normalizedPlayArea = new Rect((1f - touchpadWidth) * .5f,
                normalizedPlayArea.y, touchpadWidth, normalizedPlayArea.height);
        }

        public void SetContactRangeLanes(float width)
        {
            contactRangeLanes = Mathf.Clamp(width, .5f, 3f);
        }

        private struct PointerHistory {
            public float Lane;
            public float StartLane;
            public double StartTimeMs;
            public double TimeMs;
            public ushort CoveredMask;
            public float FlickDistance;
            public int FlickDirection;
        }

        // Brokenithm exposes the physical button width and the gaps between
        // buttons. Mapping the whole surface to sixteen equal cells makes the
        // edge of a gap behave like a cabinet touch, which is especially bad
        // for dense chords and mines.
        private const float ButtonWidthToGap = 7.428571f;
        private const float GapCount = LaneCount - 1;

        public void Capture(double now)
        {
            CaptureSample(now, Screen.width, Screen.height, null);
        }

        private void CaptureSample(double now, int screenWidth, int screenHeight,
            IReadOnlyList<Touch> samples)
        {
            current.Ground.Clear();
            seenPointers.Clear();
            ushort activeMask = 0;
            ushort sweptMask = 0;
            int count = samples != null ? samples.Count : UnityEngine.Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                Touch touch = samples != null ? samples[i] : UnityEngine.Input.GetTouch(i);
                seenPointers.Add(touch.fingerId);
                Vector2 normalized = new Vector2(touch.position.x / screenWidth, touch.position.y / screenHeight);
                PointerHistory old;
                bool had = history.TryGetValue(touch.fingerId, out old);
                if (!normalizedPlayArea.Contains(normalized)) {
                    if (had) AddRelease(touch.fingerId, old.Lane, old.StartLane, now);
                    history.Remove(touch.fingerId);
                    continue;
                }
                float point = Mathf.Clamp01((normalized.x - normalizedPlayArea.xMin) / normalizedPlayArea.width);
                float lane = LaneAt(point);
                double seconds = had ? System.Math.Max(.001, (now - old.TimeMs) / 1000.0) : .001;
                float previous = had ? old.Lane : lane;
                int laneIndex = Mathf.Clamp(Mathf.FloorToInt(lane), 0, LaneCount - 1);
                int previousLaneIndex = Mathf.Clamp(Mathf.FloorToInt(previous), 0, LaneCount - 1);
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                GroundPhase phase = ended ? GroundPhase.Up : !had ? GroundPhase.Down :
                    touch.phase == TouchPhase.Stationary ? GroundPhase.Stationary : GroundPhase.Move;
                float horizontalDelta = lane - previous;
                float flickDistance = had ? old.FlickDistance : 0f;
                int flickDirection = had ? old.FlickDirection : 0;
                bool hasFlickMove = false;
                if (phase == GroundPhase.Move && Mathf.Abs(horizontalDelta) > .0001f)
                {
                    int direction = horizontalDelta < 0f ? -1 : 1;
                    if (flickDirection != 0 && flickDirection != direction) flickDistance = 0f;
                    flickDirection = direction;
                    flickDistance += Mathf.Abs(horizontalDelta);
                    if (flickDistance >= .5f)
                    {
                        hasFlickMove = true;
                        flickDistance %= .5f;
                    }
                }
                // Unity exposes the Android contact footprint as a pixel
                // radius, not Brokenithm's normalized size field.
                float touchSize = screenWidth > 0
                    ? Mathf.Max(0f, touch.radius) / screenWidth : 0f;
                ushort mask = BuildCoveredMask(lane);
                ushort pathMask = phase == GroundPhase.Move
                    ? RangeMask(previousLaneIndex, laneIndex) : mask;
                if (!ended) {
                    activeMask |= mask;
                    sweptMask |= (ushort)(pathMask | mask);
                }
                mask |= pathMask;
                current.Ground.Add(new GroundContact {
                    PointerId = touch.fingerId, Phase = phase, TimeMs = now,
                    StartTimeMs = had ? old.StartTimeMs : now, LanePosition = lane,
                    PreviousLanePosition = previous, LaneIndex = laneIndex, PreviousLaneIndex = previousLaneIndex,
                    VelocityLanesPerSecond = (float)((lane - previous) / seconds),
                    FirstLane = Mathf.Min(laneIndex, previousLaneIndex), LastLane = Mathf.Max(laneIndex, previousLaneIndex),
                    CoveredLaneMask = mask, PressedLaneMask = 0, TouchSize = touchSize,
                    FlickTravelLanes = lane - (had ? old.StartLane : lane),
                    HasFlickMove = hasFlickMove, FlickDirection = flickDirection
                });
                if (phase == GroundPhase.Up) history.Remove(touch.fingerId);
                else history[touch.fingerId] = new PointerHistory {
                    Lane = lane, StartLane = had ? old.StartLane : lane,
                    StartTimeMs = had ? old.StartTimeMs : now, TimeMs = now,
                    CoveredMask = mask, FlickDistance = flickDistance,
                    FlickDirection = flickDirection
                };
            }

            stalePointers.Clear();
            foreach (var pair in history)
                if (!seenPointers.Contains(pair.Key)) stalePointers.Add(pair.Key);
            foreach (int pointerId in stalePointers) {
                PointerHistory old = history[pointerId];
                AddRelease(pointerId, old.Lane, old.StartLane, now);
                history.Remove(pointerId);
            }

            // The cabinet reports one merged touch-cell state, not a separate
            // press edge for every finger. A lane is new only when it was not
            // occupied in the previous sample. The path mask still lets a
            // fast finger crossing several lanes expose each sampled lane, but
            // a second finger landing on an already occupied lane is ignored.
            ushort pressedMask = (ushort)(sweptMask & ~previousActiveMask);
            for (int index = 0; index < current.Ground.Count; index++) {
                GroundContact contact = current.Ground[index];
                if (contact.Phase != GroundPhase.Up)
                    contact.PressedLaneMask = (ushort)(pressedMask & contact.CoveredLaneMask);
                current.Ground[index] = contact;
            }
            previousActiveMask = activeMask;
        }

        private void AddRelease(int pointerId, float lane, float startLane, double now)
        {
            int laneIndex = Mathf.FloorToInt(lane);
            current.Ground.Add(new GroundContact {
                PointerId = pointerId, Phase = GroundPhase.Up, TimeMs = now, StartTimeMs = now,
                LanePosition = lane, PreviousLanePosition = lane,
                LaneIndex = laneIndex, PreviousLaneIndex = laneIndex,
                VelocityLanesPerSecond = 0, FirstLane = laneIndex, LastLane = laneIndex,
                CoveredLaneMask = BuildCoveredMask(lane), PressedLaneMask = 0,
                FlickTravelLanes = lane - startLane, HasFlickMove = false, FlickDirection = 0
            });
        }

        private ushort BuildCoveredMask(float lane)
        {
            float point = Mathf.Clamp(lane, 0f, LaneCount - .001f);
            int index = Mathf.Clamp(Mathf.FloorToInt(point), 0, LaneCount - 1);
            int maximumCells = Mathf.Clamp(Mathf.CeilToInt(contactRangeLanes), 1, 3);
            float halfWidth = contactRangeLanes * .5f;
            float left = point - halfWidth;
            float right = point + halfWidth;
            ushort mask = 0;
            // Select the nearest logical cells directly. This runs for every
            // finger on every input sample, so sorting a temporary List here
            // causes avoidable GC spikes on dense charts.
            for (int selected = 0; selected < maximumCells; selected++) {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int candidate = Mathf.Max(0, Mathf.FloorToInt(left));
                     candidate <= Mathf.Min(LaneCount - 1, Mathf.FloorToInt(right)); candidate++) {
                    if ((mask & (1 << candidate)) != 0 ||
                        candidate + 1f <= left || candidate >= right) continue;
                    float distance = Mathf.Abs((candidate + .5f) - point);
                    if (distance < bestDistance ||
                        (Mathf.Approximately(distance, bestDistance) && candidate == index)) {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
                if (best < 0) break;
                mask |= (ushort)(1 << best);
            }
            if ((mask & (1 << index)) == 0) mask |= (ushort)(1 << index);
            return mask;
        }

        private static float LaneAt(float normalizedX)
        {
            float totalUnits = LaneCount * ButtonWidthToGap + GapCount;
            // A lane coordinate is anchored at each physical button's centre;
            // the gap is assigned to the nearest side just as the Android
            // bridge does when it resolves a continuous touch position.
            float units = Mathf.Clamp01(normalizedX) * totalUnits;
            return Mathf.Clamp(units / (ButtonWidthToGap + 1f), 0f, LaneCount - .001f);
        }

        private static ushort RangeMask(int first, int last)
        {
            int low = Mathf.Clamp(Mathf.Min(first, last), 0, LaneCount - 1);
            int high = Mathf.Clamp(Mathf.Max(first, last), 0, LaneCount - 1);
            ushort mask = 0;
            for (int lane = low; lane <= high; lane++) mask |= (ushort)(1 << lane);
            return mask;
        }

        public bool IsFlick(GroundContact contact)
        {
            // A previous slash is not a fresh gesture while the finger rests,
            // even on devices that keep reporting Moved at identical positions.
            if (contact.Phase != GroundPhase.Move ||
                Mathf.Abs(contact.LanePosition - contact.PreviousLanePosition) <= .0001f)
                return false;
            return contact.HasFlickMove;
        }
    }
}
