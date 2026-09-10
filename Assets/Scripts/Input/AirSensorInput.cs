using System.Collections.Generic;
using UnityEngine;

namespace ChunithmMobile.Input
{
    /// <summary>
    /// Mobile emulation of the cabinet's six vertical AIR sensors. Horizontal
    /// note position and arrow direction are intentionally ignored.
    /// </summary>
    public sealed class AirSensorInput : MonoBehaviour
    {
        public const int SensorCount = 6;
        public const int DisplacementLogCount = 15;
        public const int DisplacementLimit = 14;
        public const double InputReferenceFrameMs = 1000.0 / 60.0;
        public const double MotionReferenceMs = 6.0 * InputReferenceFrameMs;
        public const float MotionHeightThreshold = 25f / 255f;
        [SerializeField] private Rect normalizedAirArea = new Rect(0f, .28f, 1f, .72f);
        private readonly Dictionary<int, Sample> previous = new Dictionary<int, Sample>();
        private readonly Dictionary<int, MotionHistory> motionHistories =
            new Dictionary<int, MotionHistory>();
        private readonly HashSet<int> seenPointers = new HashSet<int>();
        private readonly List<int> stalePointers = new List<int>();
        private ArcadeInputFrame frame;
        private double lastPresentTimeMs = double.NegativeInfinity;
        private double lastMotionTimeMs = double.NegativeInfinity;
        private int motionSequence;
        private ushort activeSensorMask;
        private ushort motionSensorMask;

        private struct Sample
        {
            public float Height;
            public int HeightBand;
            public double TimeMs;
            public bool InAir;
        }

        private sealed class MotionHistory
        {
            private readonly float[] heights = new float[DisplacementLogCount];
            private readonly double[] times = new double[DisplacementLogCount];
            private int count;
            private int next;

            public void Clear()
            {
                count = 0;
                next = 0;
            }

            public void Add(float height, double timeMs)
            {
                if (count > 0) {
                    int latest = (next - 1 + DisplacementLogCount) % DisplacementLogCount;
                    // Keep a 60 Hz-style logical input log. Without this gate,
                    // a 165/240 Hz display evicts 100 ms of history from the
                    // fixed 15-entry original buffer before LOG=6 can read it.
                    if (timeMs - times[latest] < InputReferenceFrameMs * .75) return;
                }
                heights[next] = height;
                times[next] = timeMs;
                next = (next + 1) % DisplacementLogCount;
                count = Mathf.Min(count + 1, DisplacementLogCount);
            }

            public bool TryGetAtOrBefore(double targetTimeMs, out float height)
            {
                // Search newest to oldest. The first sample at or before the
                // six-frame reference time is the original LOG=6 comparison,
                // expressed in elapsed time so 90/120 Hz does not shorten it.
                int inspected = Mathf.Min(count, DisplacementLimit + 1);
                for (int offset = 1; offset <= inspected; offset++) {
                    int index = (next - offset + DisplacementLogCount) % DisplacementLogCount;
                    if (times[index] > targetTimeMs) continue;
                    height = heights[index];
                    return true;
                }
                height = 0;
                return false;
            }
        }

        public void Bind(ArcadeInputFrame sharedFrame) { frame = sharedFrame; }

        public void ResetCaptureState()
        {
            previous.Clear();
            motionHistories.Clear();
            seenPointers.Clear();
            stalePointers.Clear();
            lastPresentTimeMs = double.NegativeInfinity;
            lastMotionTimeMs = double.NegativeInfinity;
            motionSequence = 0;
            activeSensorMask = 0;
            motionSensorMask = 0;
            if (frame != null) frame.Air = default;
        }

        public void SetAirArea(Rect area)
        {
            normalizedAirArea = new Rect(Mathf.Clamp01(area.x), Mathf.Clamp01(area.y),
                Mathf.Clamp01(area.width), Mathf.Clamp(area.height, .001f, 1f));
        }

        public void Capture(double now)
        {
            if (frame == null) return;
            seenPointers.Clear();
            int count = 0, low = SensorCount, high = -1;
            float heightSum = 0, xSum = 0, velocitySum = 0;
            int maxHeightBandDelta = 0, direction = 0;
            float maxMotionDisplacement = 0;
            bool displacementMotion = false;
            ushort presentSensorMask = 0, risingMask = 0, fallingMask = 0;
            for (int i = 0; i < UnityEngine.Input.touchCount; i++) {
                Touch touch = UnityEngine.Input.GetTouch(i);
                seenPointers.Add(touch.fingerId);
                Vector2 n = new Vector2(touch.position.x / Screen.width, touch.position.y / Screen.height);
                float height = Mathf.Clamp01((n.y - normalizedAirArea.yMin) / normalizedAirArea.height);
                // The cabinet's six photo sensors are height levels. Screen X
                // is retained only for the multi-hand indicator and never
                // selects an AIR sensor or judgement lane.
                int heightBand = SensorAtHeight(height);
                int sensor = heightBand;
                Sample old; float velocity = 0;
                bool had = previous.TryGetValue(touch.fingerId, out old);
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                bool inAir = normalizedAirArea.Contains(n) && !ended;
                if (had && inAir && old.InAir) {
                    velocity = (float)((height - old.Height) / System.Math.Max(.001, (now - old.TimeMs) / 1000.0));
                    int heightBandDelta = heightBand - old.HeightBand;
                    if (heightBandDelta > 0)
                        risingMask |= CrossedSensorMask(old.HeightBand, heightBand);
                    else if (heightBandDelta < 0)
                        fallingMask |= CrossedSensorMask(old.HeightBand, heightBand);
                    if (Mathf.Abs(heightBandDelta) > maxHeightBandDelta) {
                        maxHeightBandDelta = Mathf.Abs(heightBandDelta);
                        direction = heightBandDelta == 0 ? 0 : heightBandDelta > 0 ? 1 : -1;
                    }
                }
                if (inAir) {
                    if (!motionHistories.TryGetValue(touch.fingerId, out MotionHistory history)) {
                        history = new MotionHistory();
                        motionHistories.Add(touch.fingerId, history);
                    }
                    if (!had || !old.InAir) history.Clear();
                    if (history.TryGetAtOrBefore(now - MotionReferenceMs, out float referenceHeight)) {
                        float displacement = height - referenceHeight;
                        if (HasOriginalDisplacement(height, referenceHeight) &&
                            Mathf.Abs(displacement) >= maxMotionDisplacement) {
                            maxMotionDisplacement = Mathf.Abs(displacement);
                            displacementMotion = true;
                            direction = displacement > 0 ? 1 : displacement < 0 ? -1 : direction;
                        }
                    }
                    history.Add(height, now);
                    count++; low = Mathf.Min(low, sensor); high = Mathf.Max(high, sensor);
                    heightSum += height; xSum += n.x; velocitySum += velocity;
                    presentSensorMask |= (ushort)(1 << sensor);
                }
                // Keep the last position while a finger crosses the shared
                // ground/AIR boundary. Dropping it outside the AIR rectangle
                // loses the upward sensor edge that the cabinet receives.
                if (ended) {
                    previous.Remove(touch.fingerId);
                    motionHistories.Remove(touch.fingerId);
                }
                else previous[touch.fingerId] = new Sample {
                    Height = height, HeightBand = heightBand, TimeMs = now, InAir = inAir
                };
                if (!inAir) motionHistories.Remove(touch.fingerId);
            }
            stalePointers.Clear();
            foreach (var pair in previous)
                if (!seenPointers.Contains(pair.Key)) stalePointers.Add(pair.Key);
            foreach (int pointerId in stalePointers) {
                previous.Remove(pointerId);
                motionHistories.Remove(pointerId);
            }
            float meanVelocity = count > 0 ? velocitySum / count : 0;
            if (count > 0) lastPresentTimeMs = now;
            ushort enteredMask = EnteredSensorMask(activeSensorMask, presentSensorMask);
            ushort exitedMask = ExitedSensorMask(activeSensorMask, presentSensorMask);
            activeSensorMask = presentSensorMask;
            // Entering or leaving a cabinet beam is itself an AIR edge. The
            // previous mobile bridge only emitted movement after a finger had
            // already moved between two height bands, making the first AIR in
            // a phrase require an extra gesture.
            ushort currentMotionMask = (ushort)(risingMask | fallingMask | enteredMask | exitedMask);
            bool movementSample = currentMotionMask != 0 || displacementMotion;
            if (movementSample) {
                lastMotionTimeMs = now;
                motionSequence++;
                motionSensorMask = currentMotionMask != 0
                    ? currentMotionMask
                    : (ushort)(count > 0 ? 1 << SensorAtHeight(heightSum / count) : 0);
            }
            frame.Air = new AirState {
                TimeMs = now, Present = count > 0, LastPresentTimeMs = lastPresentTimeMs,
                 LastMotionTimeMs = lastMotionTimeMs, LowestSensor = count > 0 ? low : -1,
                MotionSequence = motionSequence, ActiveSensorMask = activeSensorMask,
                MotionSensorMask = motionSensorMask, EnteredSensorMask = enteredMask,
                ExitedSensorMask = exitedMask,
                HighestSensor = high, MeanHeight = count > 0 ? heightSum / count : 0,
                MeanX = count > 0 ? xSum / count : .5f, ContactCount = count,
                VerticalVelocity = meanVelocity, HasVerticalMotion = displacementMotion || maxHeightBandDelta > 0,
                MotionSensorDelta = maxHeightBandDelta, MotionDirection = direction,
                RisingSensorMask = risingMask, FallingSensorMask = fallingMask,
                HasRisingMotion = risingMask != 0, HasFallingMotion = fallingMask != 0
            };
        }

        public static int SensorAtHeight(float normalizedHeight) =>
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(normalizedHeight) * SensorCount),
                0, SensorCount - 1);

        public static ushort CrossedSensorMask(int fromSensor, int toSensor)
        {
            int first = Mathf.Clamp(Mathf.Min(fromSensor, toSensor), 0, SensorCount - 1);
            int last = Mathf.Clamp(Mathf.Max(fromSensor, toSensor), 0, SensorCount - 1);
            ushort mask = 0;
            for (int sensor = first; sensor <= last; sensor++) mask |= (ushort)(1 << sensor);
            return mask;
        }

        public static ushort EnteredSensorMask(ushort previousMask, ushort currentMask) =>
            (ushort)(currentMask & ~previousMask);

        public static ushort ExitedSensorMask(ushort previousMask, ushort currentMask) =>
            (ushort)(previousMask & ~currentMask);

        public static bool HasOriginalDisplacement(float currentHeight, float referenceHeight) =>
            Mathf.Abs(currentHeight - referenceHeight) >= MotionHeightThreshold;

    }
}
