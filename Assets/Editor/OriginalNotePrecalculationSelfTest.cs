using System.IO;
using System.Linq;
using System.Text;
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
            Debug.Log("Original note pre-calculation self-test passed: semantic notes have stable visual ownership and resources.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new System.Exception(message);
        }
    }
}
