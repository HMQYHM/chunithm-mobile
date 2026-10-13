using System.IO;
using System.Linq;
using System.Text;
using System;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using UnityEditor;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Checks the renderer-facing note plan without creating Unity meshes.</summary>
    public static class OriginalNotePrecalculationSelfTest
    {
        [MenuItem("CHUNITHM Mobile/Verify original note pre-calculation")]
        public static void Run()
        {
            const string source =
                "RESOLUTION\t384\n" +
                "BPM_DEF\t120\n" +
                "BPM\t0\t0\t120\n" +
                "T_JUDGE_TAP\t1\n" +
                "T_JUDGE_HLD\t2\n" +
                "TAP\t1\t0\t4\t2\n" +
                "HLD\t2\t0\t4\t2\t384\t4\t2\n" +
                "HLD\t3\t0\t4\t2\t384\t4\t2\n" +
                "AIR\t4\t0\t4\t2\tTAP\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            OriginalNotePrecalculation plan = OriginalNotePrecalculation.Build(chart);
            Require(plan.Tracks.Count == chart.Notes.Count, "pre-calculation changed note count");
            Require(plan.Tracks.Any(t => t.VisualKind == OriginalNoteVisualKind.Tap && t.HeadResource == "tap"),
                "tap resource was not planned");
            Require(plan.Tracks.Any(t => t.VisualKind == OriginalNoteVisualKind.Hold && t.BodyResource == "hold_joint"),
                "hold body was not planned");
            Require(plan.Tracks.Any(t => t.VisualKind == OriginalNoteVisualKind.Air && t.HeadResource.StartsWith("air_")),
                "AIR head resource was not planned");

            // Two records may carry the same parser chain id while describing
            // a fork/overlap. The renderer keeps those branches independent;
            // pre-calculation must therefore leave both body owners active.
            var branchNotes = new System.Collections.Generic.List<ChartNote> {
                new ChartNote {
                    Kind = NoteKind.Hold, TimeMs = 0, EndTimeMs = 100,
                    Lane = 0, Width = 4, EndLane = 0, EndWidth = 4,
                    LongChainId = 7, IsLongChainHead = true, IsLongChainTail = true,
                    VisibleHead = true, HasVisualHead = true, VisibleTail = true
                },
                new ChartNote {
                    Kind = NoteKind.Hold, TimeMs = 100, EndTimeMs = 200,
                    Lane = 8, Width = 2, EndLane = 8, EndWidth = 2,
                    LongChainId = 7, IsLongChainHead = false, IsLongChainTail = true,
                    VisibleHead = false, HasVisualHead = false, VisibleTail = true
                }
            };
            var branchChart = new ParsedChart(branchNotes, 300, 120, 2,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var branchPlan = OriginalNotePrecalculation.Build(branchChart);
            Require(branchPlan[0].IsBodyOwner && branchPlan[1].IsBodyOwner,
                "a non-contiguous branch was incorrectly assigned one shared body owner");

            var linearNotes = new System.Collections.Generic.List<ChartNote> {
                new ChartNote {
                    Kind = NoteKind.Hold, TimeMs = 0, EndTimeMs = 100,
                    Lane = 0, Width = 4, EndLane = 0, EndWidth = 4,
                    LongChainId = 8, IsLongChainHead = true, IsLongChainTail = false,
                    VisibleHead = true, HasVisualHead = true, VisibleTail = false
                },
                new ChartNote {
                    Kind = NoteKind.Hold, TimeMs = 100, EndTimeMs = 200,
                    Lane = 0, Width = 4, EndLane = 0, EndWidth = 4,
                    LongChainId = 8, IsLongChainHead = false, IsLongChainTail = true,
                    VisibleHead = false, HasVisualHead = false, VisibleTail = true
                }
            };
            var linearChart = new ParsedChart(linearNotes, 300, 120, 2,
                new System.Collections.Generic.List<ScrollEvent>(),
                new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ScrollEvent>>(),
                new System.Collections.Generic.List<ScrollAssignment>());
            var linearPlan = OriginalNotePrecalculation.Build(linearChart);
            Require(linearPlan[0].IsBodyOwner && !linearPlan[0].IsTailOwner &&
                !linearPlan[1].IsBodyOwner && linearPlan[1].IsTailOwner,
                "a welded long chain did not split body and tail ownership");

            // Movement and width changes inside a continuous curve are still
            // one original body. Only the terminal segment owns the tail.
            linearNotes[1].Lane = 2;
            linearNotes[1].Width = 2;
            linearNotes[1].EndLane = 4;
            linearNotes[1].EndWidth = 2;
            var curvedPlan = OriginalNotePrecalculation.Build(linearChart);
            Require(curvedPlan[0].IsBodyOwner && !curvedPlan[1].IsBodyOwner && curvedPlan[1].IsTailOwner,
                "a continuous moving/width-changing curve was split into independent bodies");

            // The Windows charts intentionally allow a ground HOLD and a
            // SLIDE to occupy the same timing/lane range. They are two
            // authored visual objects, not one merged long note.
            var overlap = Parse(
                "HLD\t0\t0\t5\t3\t384\n" +
                "SLD\t0\t0\t5\t3\t384\t5\t3\n" +
                "TAP\t0\t384\t5\t3\n" +
                "AIR\t0\t384\t5\t3\tTAP\n");
            var overlapPlan = OriginalNotePrecalculation.Build(overlap);
            int holdIndex = overlap.Notes.FindIndex(n => n.OriginalType == "HLD");
            int slideIndex = overlap.Notes.FindIndex(n => n.OriginalType == "SLD");
            int tapIndex = overlap.Notes.FindIndex(n => n.OriginalType == "TAP");
            int airIndex = overlap.Notes.FindIndex(n => n.OriginalType == "AIR");
            Require(holdIndex >= 0 && slideIndex >= 0 && overlapPlan[holdIndex].IsBodyOwner &&
                overlapPlan[slideIndex].IsBodyOwner && overlapPlan[holdIndex].ChainOwnerIndex == holdIndex &&
                overlapPlan[slideIndex].ChainOwnerIndex == slideIndex,
                "overlapping HOLD and SLIDE were merged into one body");
            Require(overlapPlan[tapIndex].IsHeadOwner && overlapPlan[airIndex].IsHeadOwner,
                "a same-time TAP/AIR chord lost one of its visual heads");

            // SLC/SXC are curve controls. Only the terminal SLD/SXD owns the
            // visible slide tail; control records must never add a duplicate
            // cap at a seam.
            var curve = Parse(
                "SLC\t0\t0\t2\t3\t384\t4\t3\n" +
                "SXC\t1\t0\t4\t3\t384\t6\t3\n" +
                "SLD\t2\t0\t6\t3\t384\t8\t3\n");
            Require(curve.Notes.Where(n => n.OriginalType is "SLC" or "SXC").All(n => !n.VisibleTail) &&
                curve.Notes.Any(n => n.OriginalType == "SLD" && n.VisibleTail),
                "slide control records incorrectly owned a tail cap");

            // Smoke-test one real Windows chart as well as the synthetic
            // fixtures above. This protects the parser-to-renderer contract
            // against a future change that only works on simplified records.
            string originalPath = Environment.GetEnvironmentVariable("CHUNITHM_CHART_PATH");
            if (string.IsNullOrWhiteSpace(originalPath)) {
                string workspaceRoot = Path.GetFullPath("..");
                originalPath = Path.Combine(workspaceRoot, "Original-Game-Transfer-20261008", "原版Windows游戏",
                    "bin", "option", "A300", "music", "music2078", "2078_03.c2s");
            }
            if (File.Exists(originalPath)) {
                ParsedChart original = C2sParser.Parse(originalPath);
                Require(original.Notes.Any(n => n.OriginalType == "HLD") &&
                    original.Notes.Any(n => n.OriginalType == "SLD"),
                    "reference Windows chart did not contain HOLD and SLIDE records");
                var samePlane = original.Notes.GroupBy(n => n.TimeMs)
                    .SelectMany(group => group.GroupBy(n => n.Lane + ":" + n.Width))
                    .Any(group => group.Any(n => n.OriginalType == "HLD") &&
                                  group.Any(n => n.OriginalType == "SLD"));
                Require(samePlane, "reference Windows chart lost an overlapping HOLD/SLIDE chord");
                var originalPlan = OriginalNotePrecalculation.Build(original);
                foreach (var group in original.Notes.GroupBy(n => n.TimeMs)
                    .SelectMany(group => group.GroupBy(n => n.Lane + ":" + n.Width))) {
                    var hold = group.FirstOrDefault(n => n.OriginalType == "HLD");
                    var slide = group.FirstOrDefault(n => n.OriginalType == "SLD");
                    if (hold == null || slide == null) continue;
                    int originalHoldIndex = original.Notes.IndexOf(hold);
                    int originalSlideIndex = original.Notes.IndexOf(slide);
                    Require(originalPlan[originalHoldIndex].IsBodyOwner && originalPlan[originalSlideIndex].IsBodyOwner,
                        "reference Windows HOLD/SLIDE chord was merged");
                    break;
                }
            }
            Debug.Log("Original note pre-calculation self-test passed: semantic notes have stable visual ownership and resources.");
        }

        private static ParsedChart Parse(string records)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                "RESOLUTION\t384\nBPM_DEF\t120\n" + records));
            return C2sParser.Parse(stream);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new System.Exception(message);
        }
    }
}
