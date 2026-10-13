using System;
using System.IO;
using System.Linq;
using System.Text;
using ChunithmMobile.Chart;
using ChunithmMobile.Rendering;
using ChunithmMobile.Runtime;
using UnityEngine;

namespace ChunithmMobile.Editor
{
    /// <summary>Checks the original AirLadder models and textures required by the renderer.</summary>
    public static class AirLadderAssetSelfTest
    {
        public static void Run()
        {
            GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
            for (int width = 1; width <= 16; width++) {
                string suffix = width.ToString("00");
                foreach (string kind in new[] { "airlad", "airladtop" })
                    foreach (string part in new[] { "bar", "frame" }) {
                        string name = $"ntt_{kind}_{suffix}_{part}";
                        GameObject model = Resources.Load<GameObject>("OriginalMeshes/" + name);
                        if (model == null || model.GetComponentInChildren<MeshFilter>()?.sharedMesh == null)
                            throw new InvalidOperationException("Missing built-in AirLadder mesh: " + name);
                    }
            }
            foreach (string texture in new[] { "airlad_bar", "airlad_waku", "airlad_effEx", "airladtop_bar" })
                if (Resources.Load<Texture2D>("OriginalNotes/field/" + texture) == null)
                    throw new InvalidOperationException("Missing built-in AirLadder texture: " + texture);
            if (Resources.Load<Shader>("AirLadder") == null)
                throw new InvalidOperationException("Missing AirLadder shader resource");

            const string source = "RESOLUTION\t384\nBPM_DEF\t166\nBPM\t0\t0\t166\n" +
                "ALD\t44\t383\t3\t4\t384\t1\t1\t3\t4\t1\n" +
                "ALD\t44\t383\t3\t4\t384\t2\t1\t3\t4\t1\n" +
                "ALD\t44\t383\t3\t4\t384\t3\t1\t3\t4\t1\n" +
                "ALD\t44\t383\t3\t4\t384\t4\t1\t3\t4\t1\n" +
                "ALD\t44\t383\t3\t4\t384\t5\t1\t3\t4\t1\n" +
                "ALD\t44\t383\t7\t4\t384\t3\t1\t7\t4\t1\n" +
                "ALD\t44\t383\t8\t4\t384\t2\t1\t8\t4\t1\n" +
                "ALD\t44\t383\t8\t4\t384\t4\t1\t8\t4\t1\n" +
                "ALD\t44\t383\t9\t4\t384\t1\t1\t9\t4\t1\n" +
                "ALD\t44\t383\t9\t4\t384\t5\t1\t9\t4\t1\n";
            ParsedChart chart;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source))) chart = C2sParser.Parse(stream);
            Require(chart.GuidePaths.Count == 10, "KING ALD records did not survive chart parsing");
            Require(chart.GuidePaths.All(AirLadderVisuals.HasPlates), "KING spaced ALD was not classified as a plate");
            Require(chart.GuidePaths.All(path => path.StyleInterval == 384 && path.StyleIntervalMilliseconds > 0),
                "KING ALD cadence was not converted to chart milliseconds");
            Require(chart.Notes.Count == 10 && chart.Notes.All(n => n.OriginalType == "ALD" && n.JudgePointCount == 1),
                "AirLadder plate/checker ownership was lost");
            Debug.Log("AirLadder asset regression passed: 128 meshes, 4 field textures, shader and 10 KING ALD plates.");
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
