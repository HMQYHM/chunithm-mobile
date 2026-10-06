using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Checks that a note is allocated before it reaches the visible far edge.</summary>
    public static class NoteSpawnBoundarySelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            const string source = "RESOLUTION\t384\nBPM_DEF\t120\nTAP\t0\t384\t4\t2\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            var root = new GameObject("Note spawn boundary regression");
            try {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (((IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("headMaterials", Private).GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                renderer.Bind(chart, new DspChartClock());
                MethodInfo render = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private);
                var active = (IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("active", Private).GetValue(renderer);
                ChartNote note = chart.Notes[0];
                // At this sample the head is outside VisualFarZ but inside the
                // explicit spawn band. It must already own a pooled visual.
                render.Invoke(renderer, new object[] { note.TimeMs - 2600.0 });
                if (!active.Contains(0))
                    throw new InvalidOperationException("note was not spawned before the visible far edge");
                object visual = active[0];
                var head = (GameObject)visual.GetType().GetField("Head").GetValue(visual);
                if (!head.activeSelf)
                    throw new InvalidOperationException("pre-spawn note head was not prepared");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("Note spawn boundary regression passed: note was prepared outside the visible far edge.");
        }
    }
}
