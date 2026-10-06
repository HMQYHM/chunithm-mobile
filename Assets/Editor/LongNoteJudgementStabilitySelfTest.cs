using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using ChunithmMobile.Timing;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>
    /// Samples a straight HOLD while its head crosses the judgement plane.
    /// The front edge must stay on one lane-space X coordinate; a changing
    /// mesh section must not make the note appear to jump sideways.
    /// </summary>
    public static class LongNoteJudgementStabilitySelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            const string source = "RESOLUTION\t384\nBPM_DEF\t120\n" +
                "SLD\t0\t384\t4\t2\t384\t8\t2\n";
            ParsedChart chart;
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(source)))
                chart = C2sParser.Parse(stream);

            var root = new GameObject("Long note judgement stability regression");
            try
            {
                var renderer = root.AddComponent<ChartPlaybackRenderer>();
                if (((IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("headMaterials", Private).GetValue(renderer)).Count == 0)
                    typeof(ChartPlaybackRenderer).GetMethod("Awake", Private).Invoke(renderer, null);
                renderer.Bind(chart, new DspChartClock());
                MethodInfo render = typeof(ChartPlaybackRenderer).GetMethod("RenderFrame", Private);
                var active = (IDictionary)typeof(ChartPlaybackRenderer)
                    .GetField("active", Private).GetValue(renderer);
                ChartNote note = chart.Notes[0];
                double[] samples = { note.TimeMs - 2.0, note.TimeMs - .5, note.TimeMs,
                    note.TimeMs + .5, note.TimeMs + 2.0 };
                var frontXs = new List<float>();
                var frontZs = new List<float>();
                foreach (double now in samples)
                {
                    render.Invoke(renderer, new object[] { now });
                    if (!active.Contains(0)) throw new InvalidOperationException("long note was culled at judgement");
                    object visual = active[0];
                    var body = (GameObject)visual.GetType().GetField("Body").GetValue(visual);
                    Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
                    if (!body.activeSelf || mesh == null || mesh.vertexCount == 0)
                        throw new InvalidOperationException("long note body disappeared at judgement");
                    float near = ChartSpace.JudgementZ;
                    float nearest = float.PositiveInfinity;
                    foreach (Vector3 vertex in mesh.vertices) nearest = Mathf.Min(nearest, Mathf.Abs(vertex.z - near));
                    float sum = 0f; int count = 0;
                    foreach (Vector3 vertex in mesh.vertices)
                        if (Mathf.Abs(vertex.z - near) <= nearest + .002f) { sum += vertex.x; count++; }
                    if (count == 0) {
                        var bounds = mesh.bounds;
                        Debug.Log($"stability sample now={now:F3}, vertices={mesh.vertexCount}, zRange={bounds.min.z:F4}..{bounds.max.z:F4}, nearest={nearest:F4}");
                        throw new InvalidOperationException("long note has no stable judgement-line edge");
                    }
                    frontXs.Add(sum / count);
                    frontZs.Add(nearest);
                }
                float expected = ChartSpace.NoteCenterX(note.Lane, note.Width, ChartSpace.JudgementZ);
                foreach (float x in frontXs)
                    if (Mathf.Abs(x - expected) > .02f)
                        throw new InvalidOperationException($"judgement-line edge moved to x={x:F4}, expected {expected:F4}");
                for (int i = 1; i < frontXs.Count; i++)
                    if (Mathf.Abs(frontXs[i] - frontXs[i - 1]) > .02f)
                        throw new InvalidOperationException($"judgement-line edge jumped {frontXs[i - 1]:F4}->{frontXs[i]:F4}");
                if (frontZs[2] > .002f || frontZs[3] > .002f || frontZs[4] > .002f)
                    throw new InvalidOperationException("post-judgement body edge was not anchored to the judgement plane");

                // The judgement callback hides the head independently of the
                // body. The body must keep its exact line anchor through that
                // transition instead of exposing the old epsilon-offset edge.
                renderer.MarkJudged(0, 0);
                render.Invoke(renderer, new object[] { note.TimeMs + 4.0 });
                if (!active.Contains(0)) throw new InvalidOperationException("long note was recycled immediately after head judgement");
                object judgedVisual = active[0];
                var judgedBody = (GameObject)judgedVisual.GetType().GetField("Body").GetValue(judgedVisual);
                var judgedHead = (GameObject)judgedVisual.GetType().GetField("Head").GetValue(judgedVisual);
                if (judgedHead.activeSelf) throw new InvalidOperationException("judged long-note head remained visible");
                Mesh judgedMesh = judgedBody.GetComponent<MeshFilter>().sharedMesh;
                float judgedNearest = float.PositiveInfinity;
                float judgedSum = 0f; int judgedCount = 0;
                foreach (Vector3 vertex in judgedMesh.vertices)
                    judgedNearest = Mathf.Min(judgedNearest, Mathf.Abs(vertex.z - ChartSpace.JudgementZ));
                foreach (Vector3 vertex in judgedMesh.vertices)
                    if (Mathf.Abs(vertex.z - ChartSpace.JudgementZ) <= judgedNearest + .002f) {
                        judgedSum += vertex.x; judgedCount++;
                    }
                if (judgedCount == 0 || Mathf.Abs(judgedSum / judgedCount - expected) > .02f)
                    throw new InvalidOperationException("body edge moved when judged head was hidden");

                // The tail/body may become empty as its final segment crosses
                // the near plane, but the pooled visual must not be recycled
                // on that exact sample. A tiny timing/profile fluctuation on
                // the next frame must therefore keep the same visual alive.
                object identity = active[0];
                render.Invoke(renderer, new object[] { note.EndTimeMs + .1 });
                if (!active.Contains(0) || !ReferenceEquals(identity, active[0]))
                    throw new InvalidOperationException("long-note visual was recycled at the tail removal boundary");
                render.Invoke(renderer, new object[] { note.EndTimeMs + 259.0 });
                if (!active.Contains(0))
                    throw new InvalidOperationException("long-note visual was removed before its visual grace period ended");
                render.Invoke(renderer, new object[] { note.EndTimeMs + 261.0 });
                if (active.Contains(0))
                    throw new InvalidOperationException("long-note visual remained after its visual grace period");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("Long-note judgement stability regression passed: front edge stayed fixed through the judgement plane.");
        }
    }
}
