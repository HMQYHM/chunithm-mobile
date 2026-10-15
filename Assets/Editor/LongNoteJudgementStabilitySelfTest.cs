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
            VerifyCachedCurvesAndClipping();
            Debug.Log("Long-note judgement stability regression passed: front edge stayed fixed through the judgement plane.");
        }
        private static void VerifyCachedCurvesAndClipping()
        {
            var clip=typeof(ChartPlaybackRenderer).GetMethod("ClipRibbonSegment",BindingFlags.Static|BindingFlags.NonPublic);
            // A completely passed, slanted strip used to be flattened into a
            // sideways triangle on the judgement line for the hysteresis band.
            object[] passed={new Vector3(-6,0,.20f),new Vector3(-4,0,.20f),0f,
                new Vector3(4,0,.22f),new Vector3(6,0,.22f),1f,.25f,44f};
            if((bool)clip.Invoke(null,passed))throw new InvalidOperationException("passed strip flattened onto judgement plane");
            foreach(bool reverse in new[]{false,true}) {
                var leftA=new Vector3(-8,1,reverse?50:-5);var leftB=new Vector3(8,7,reverse?-5:50);
                object[] args={leftA,leftA+Vector3.right*2,0f,leftB,leftB+Vector3.right*2,1f,.25f,44f};
                if(!(bool)clip.Invoke(null,args))throw new InvalidOperationException("cross-plane ribbon discarded");
                foreach(int index in new[]{0,3}) {
                    var point=(Vector3)args[index];float t=(point.z-leftA.z)/(leftB.z-leftA.z);
                    if(Vector3.Distance(point,Vector3.Lerp(leftA,leftB,t))>.0001f ||
                        Math.Abs((float)args[index+2]-t)>.0001f)
                        throw new InvalidOperationException("clipping changed authored line/UV, reverse="+reverse);
                }
            }
            foreach(NoteKind kind in new[]{NoteKind.Hold,NoteKind.HeavenHold,NoteKind.Slide,NoteKind.AirHold,NoteKind.AirAction}) {
                var notes=new List<ChartNote>();
                for(int i=0;i<3;i++) notes.Add(new ChartNote {Kind=kind,TimeMs=1000+i*400,EndTimeMs=1400+i*400,
                    Lane=i==1?9:2,EndLane=i==0?9:2,Width=i+1,EndWidth=i+2,AirHeight=3+i,EndAirHeight=4+i,
                    LongChainId=1,IsContinuation=i>0,IsSlideCheckpoint=i>0,IsLongChainHead=i==0,IsLongChainTail=i==2,
                    VisibleTail=i==2,JudgePointCount=4});
                notes.Add(new ChartNote {Kind=kind,TimeMs=1200,EndTimeMs=2100,Lane=5,EndLane=3,
                    Width=3,EndWidth=6,AirHeight=4,EndAirHeight=7,JudgePointCount=5});
                var chart=new ParsedChart(notes,3000,120,17,new List<ScrollEvent>(),new Dictionary<int,List<ScrollEvent>>(),new List<ScrollAssignment>());
                var root=new GameObject("Curve stability "+kind);
                try {
                    var renderer=root.AddComponent<ChartPlaybackRenderer>();
                    if(((IDictionary)typeof(ChartPlaybackRenderer).GetField("headMaterials",Private).GetValue(renderer)).Count==0)
                        typeof(ChartPlaybackRenderer).GetMethod("Awake",Private).Invoke(renderer,null);
                    renderer.Bind(chart,new DspChartClock());
                    var geometry=(Array)typeof(ChartPlaybackRenderer).GetField("geometry",Private).GetValue(renderer);
                    var before=GeometrySignature(renderer,geometry);
                    var render=typeof(ChartPlaybackRenderer).GetMethod("RenderFrame",Private);
                    foreach(double now in new[]{999.9,1000.0,1000.1,1399.9,1400.0,1400.1,1799.9,1800.0,1800.1,2199.9,2200.1}) {
                        render.Invoke(renderer,new object[]{now});
                        renderer.MarkJudged(now<1400?0:now<1800?1:2,0);
                        if(!ReferenceEquals(geometry,typeof(ChartPlaybackRenderer).GetField("geometry",Private).GetValue(renderer)) ||
                            before!=GeometrySignature(renderer,geometry))throw new InvalidOperationException("remaining curve resampled at node: "+kind);
                        var active=(IDictionary)typeof(ChartPlaybackRenderer).GetField("active",Private).GetValue(renderer);
                        foreach(object visual in active.Values) {
                            foreach(string part in new[]{"Body","Line"}) {
                                var body=(GameObject)visual.GetType().GetField(part).GetValue(visual);
                                if(!body.activeSelf)continue;
                                var mesh=body.GetComponent<MeshFilter>().sharedMesh;
                                foreach(var vertex in mesh.vertices)
                                    if(float.IsNaN(vertex.x)||float.IsInfinity(vertex.x)||vertex.z<ChartSpace.JudgementZ-.0001f)
                                        throw new InvalidOperationException("invalid/passed curve vertex: "+kind);
                            }
                        }
                    }
                } finally {UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        private static string GeometrySignature(ChartPlaybackRenderer renderer,Array geometry)
        {
            var text=new StringBuilder();
            Action<object> append=value=>{
                foreach(var field in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
                    if(field.GetValue(value) is Array data)
                        foreach(object item in data)text.Append(item).Append(',');
                text.Append(';');
            };
            foreach(object value in geometry)append(value);
            // Welded chain ribbons are separate cached geometry, and must stay
            // unchanged when their constituent note heads leave the screen.
            var chains=(IDictionary)typeof(ChartPlaybackRenderer).GetField("longChainGeometry",Private).GetValue(renderer);
            foreach(object chain in chains.Values)append(chain.GetType().GetField("Ribbon").GetValue(chain));
            return text.ToString();
        }
    }
}
