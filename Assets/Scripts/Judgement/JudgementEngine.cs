using System;
using ChunithmMobile.Chart;

namespace ChunithmMobile.Judgement
{
    public struct JudgementResult
    {
        public Chart.Judgement Grade;
        public double DeltaMs;
        public JudgementResult(Chart.Judgement grade, double deltaMs) { Grade = grade; DeltaMs = deltaMs; }
    }

    public static class JudgementEngine
    {
        // The original INI expresses windows in 60 Hz reference frames. Keep
        // their exact duration in milliseconds; never count rendered frames.
        public const double ReferenceFrameMs = 1000.0 / 60.0;
        public const double TapCriticalFrames = 2.0;
        public const double TapJusticeFrames = 4.0;
        public const double TapEasyAttackFrames = 6.0;
        public const double TapHardAttackFrames = 5.0;
        public const double ExTapEasyFrames = 6.0;
        public const double ExTapHardFrames = 5.0;
        // Mobile FLICK uses the fresh half-lane gesture time, independent of
        // the initial touch. Expand the former simple window by 20ms per side.
        public const double FlickWindowMs = ReferenceFrameMs * 5.0 + 20.0;
        public const double SlideAreaFrames = 3.0;
        public const double SlideTriggerFrames = 9.0;
        public const double KeepCriticalFrames = 3.0;
        public const double KeepJusticeFrames = 6.0;
        public const double KeepAttackFrames = 9.0;
        // KEEP is evaluated half a reference frame before its authored tick in
        // the original checker. This is separate from the visible head time.
        public const double KeepOffsetFrames = -.5;
        public const double AirHoldAttackMs = ReferenceFrameMs * 36.0;
        // Bridge a sampled AIR boundary gap without turning an abandoned
        // sensor into a held note.
        public const double AirContactGraceFrames = 3.0;
        // MINE has its own asymmetric collision interval. IN/OUT are the
        // actual touch interval; NG is the later cleanup boundary from the
        // original timing table.
        public const double MineInFrames = -2.1;
        public const double MineOutFrames = 1.0;
        public const double MineNgFrames = 2.1;
        public const double MineInMs = ReferenceFrameMs * MineInFrames;
        public const double MineOutMs = ReferenceFrameMs * MineOutFrames;
        public const double MineNgMs = ReferenceFrameMs * MineNgFrames;
        public const double AnswerOffsetFrames = -1.0;

        public static JudgementResult JudgeGround(NoteKind kind, double inputTimeMs, double noteTimeMs,
            double playbackRate = 1.0, bool hard = true, bool longHead = false)
        {
            // The chart clock advances faster/slower with playback. Convert the
            // chart-space difference back to elapsed real milliseconds so the
            // original judgement windows remain unchanged at every song rate.
            double delta = (inputTimeMs - noteTimeMs) / Math.Max(.1, playbackRate);
            Chart.Judgement grade;
            if (kind == NoteKind.ExTap)
                grade = InWindow(delta, -Frames(hard ? ExTapHardFrames : ExTapEasyFrames),
                    Frames(hard ? ExTapHardFrames : ExTapEasyFrames))
                    ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss;
            else if (kind == NoteKind.Flick)
                grade = InWindow(delta, -FlickWindowMs, FlickWindowMs)
                    ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss;
            else if (!longHead && kind is (NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide))
                grade = InWindow(delta, -Frames(SlideTriggerFrames), Frames(SlideTriggerFrames))
                    ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss;
            else
                grade = InWindow(delta, -Frames(TapCriticalFrames), Frames(TapCriticalFrames)) ? Chart.Judgement.JusticeCritical
                    : InWindow(delta, -Frames(TapJusticeFrames), Frames(TapJusticeFrames)) ? Chart.Judgement.Justice
                    : InWindow(delta, -Frames(hard ? TapHardAttackFrames : TapEasyAttackFrames),
                        Frames(hard ? TapHardAttackFrames : TapEasyAttackFrames))
                        ? Chart.Judgement.Attack : Chart.Judgement.Miss;
            return new JudgementResult(grade, delta);
        }

        public static JudgementResult JudgeAir(double inputTimeMs, double noteTimeMs, bool down,
            bool hold, double playbackRate = 1.0, bool flick = false)
        {
            double delta = (inputTimeMs - noteTimeMs) / Math.Max(.1, playbackRate);
            if (hold) {
                double absolute = Math.Abs(delta);
                Chart.Judgement grade = absolute <= AirHoldAttackMs
                    ? Chart.Judgement.JusticeCritical : Chart.Judgement.Miss;
                return new JudgementResult(grade, delta);
            }

            // The original table has four independent AIR profiles. In
            // particular AIR.FLICKDOWN is not the same as AIR.TAPDOWN.
            double fastCritical = Frames(down ? -6.0 : 0.0);
            double lateCritical = Frames(flick ? (down ? 9.0 : 12.0) : (down ? 6.0 : 9.0));
            double fastJustice = Frames(down ? -11.0 : -5.0);
            double lateJustice = Frames(flick ? (down ? 14.0 : 17.0) : (down ? 11.0 : 14.0));
            double fastAttack = Frames(down ? -16.0 : -10.0);
            double lateAttack = Frames(flick ? (down ? 19.0 : 22.0) : (down ? 16.0 : 19.0));
            Chart.Judgement result = delta < fastAttack || delta > lateAttack
                ? Chart.Judgement.Miss : Chart.Judgement.JusticeCritical;
            return new JudgementResult(result, delta);
        }

        public static JudgementResult JudgeAirAction(double inputTimeMs, double noteTimeMs,
            double playbackRate = 1.0)
        {
            double delta = (inputTimeMs - noteTimeMs) / Math.Max(.1, playbackRate);
            Chart.Judgement result = delta < -Frames(16.0) || delta > Frames(16.0)
                ? Chart.Judgement.Miss : Chart.Judgement.JusticeCritical;
            return new JudgementResult(result, delta);
        }

        private static bool InWindow(double value, double fast, double late) => value >= fast && value <= late;

        public static double MissWindowMs(ChartNote note, bool hard = true)
        {
            NoteKind kind = note.Kind == NoteKind.Hold && note.OriginalType == "HXD"
                ? NoteKind.ExTap : note.Kind;
            bool down = note.AirDirection is AirDirection.Down or AirDirection.DownLeft or AirDirection.DownRight;
            return MissWindowMs(kind, down, note.AirFlick, hard);
        }

        public static double MissWindowMs(NoteKind kind, bool airDown = false, bool airFlick = false,
            bool hard = true)
        {
            if (kind == NoteKind.Flick) return FlickWindowMs;
            if (kind == NoteKind.AirHold) return AirHoldAttackMs;
            if (kind == NoteKind.AirAction) return Frames(16.0);
            if (kind == NoteKind.AirLadder) return Frames(16.0);
            if (kind == NoteKind.Air)
                return Frames(airFlick ? (airDown ? 19.0 : 22.0) : (airDown ? 16.0 : 19.0));
            if (kind == NoteKind.ExTap) return Frames(hard ? ExTapHardFrames : ExTapEasyFrames);
            if (kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide) return Frames(SlideTriggerFrames);
            return Frames(hard ? TapHardAttackFrames : TapEasyAttackFrames);
        }

        public static double GroundHeadMissWindowMs(NoteKind kind, bool hard = true)
        {
            return kind is NoteKind.Hold or NoteKind.HeavenHold or NoteKind.Slide
                ? Frames(hard ? TapHardAttackFrames : TapEasyAttackFrames)
                : MissWindowMs(kind, hard: hard);
        }

        public static JudgementResult JudgeFlick(double slashTimeMs,
            double noteTimeMs, double playbackRate = 1.0)
        {
            return JudgeGround(NoteKind.Flick, slashTimeMs, noteTimeMs, playbackRate);
        }

        public static bool IsMineTouchWindow(double deltaMs) =>
            deltaMs >= MineInMs && deltaMs <= MineOutMs;

        public static bool IsMineCleanupDue(double deltaMs) => deltaMs > MineNgMs;

        public static double Frames(double frames) => ReferenceFrameMs * frames;

        public static bool Overlaps(int touchLane, int touchWidth, ChartNote note) =>
            touchLane < note.Lane + note.Width && note.Lane < touchLane + touchWidth;

        // KEEP uses the distance from the checkpoint to the last valid panel
        // sample. This preserves the original short release tolerance while
        // still allowing a later checkpoint to reconnect after a miss.
        public static Chart.Judgement JudgeCheckpoint(bool covered, double distanceMs)
        {
            if (!covered) return Chart.Judgement.Miss;
            double frames = Math.Abs(distanceMs) / ReferenceFrameMs;
            return frames <= KeepAttackFrames ? Chart.Judgement.JusticeCritical
                : Chart.Judgement.Miss;
        }

        public static Chart.Judgement JudgeCheckpoint(bool covered) => JudgeCheckpoint(covered, 0);

        public static double CheckpointCoverageDelta(bool coveredNow, bool coveredOnPreviousSample,
            double lastCoverageChartTimeMs, double sampleChartTimeMs,
            double checkpointChartTimeMs, double playbackRate = 1.0)
        {
            double rate = Math.Max(.1, playbackRate);
            if (coveredNow) {
                // Coverage sampled on both sides of the checkpoint is a true
                // KEEP, even when a slow rendered frame crosses the timing
                // plane. A fresh contact after the plane must retain its late
                // offset instead of being promoted to an on-time JC.
                bool spansCheckpoint = coveredOnPreviousSample &&
                    lastCoverageChartTimeMs <= checkpointChartTimeMs &&
                    sampleChartTimeMs >= checkpointChartTimeMs;
                return spansCheckpoint ? 0 :
                    (sampleChartTimeMs - checkpointChartTimeMs) / rate;
            }
            return double.IsNegativeInfinity(lastCoverageChartTimeMs)
                ? double.PositiveInfinity
                : (lastCoverageChartTimeMs - checkpointChartTimeMs) / rate;
        }

        public static Chart.Judgement JudgeAirHoldCheckpoint(bool covered, double distanceMs)
        {
            if (!covered) return Chart.Judgement.Miss;
            double frames = Math.Abs(distanceMs) / ReferenceFrameMs;
            return frames <= 36.0 ? Chart.Judgement.JusticeCritical
                : Chart.Judgement.Miss;
        }

        public static Chart.Judgement JudgeAirActionCheckpoint(bool covered, double distanceMs)
        {
            if (!covered) return Chart.Judgement.Miss;
            return Math.Abs(distanceMs) <= Frames(16.0)
                ? Chart.Judgement.JusticeCritical
                : Chart.Judgement.Miss;
        }

        // AIR ignores horizontal note position: any valid hand in the AIR zone can trigger it.
        public static bool JudgeAir(bool handInAirZone, bool verticalMotionRequired, bool verticalMotion) =>
            handInAirZone && (!verticalMotionRequired || verticalMotion);
    }
}
