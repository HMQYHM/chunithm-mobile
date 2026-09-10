using System.Collections.Generic;
using UnityEngine;

namespace ChunithmMobile.Rendering
{
    // Only rectangular ground-note mappings are adjusted. Geometry, vertical
    // UVs and complex authored mesh mappings remain unchanged.
    public sealed class NoteSkinUvCache
    {
        private readonly Dictionary<(Mesh, Texture2D), Mesh> meshes = new();
        private readonly Dictionary<Texture2D, Vector2> contentRanges = new();
        private readonly List<Mesh> owned = new();

        public Mesh Get(Mesh source, Texture2D texture)
        {
            if (source == null || texture == null || !source.isReadable || !texture.isReadable) return source;
            var key = (source, texture);
            if (meshes.TryGetValue(key, out Mesh cached)) return cached;
            Mesh result = source;
            Vector2[] uv = source.uv;
            if (uv.Length > 0) {
                float min = 1, max = 0;
                foreach (Vector2 point in uv) { min = Mathf.Min(min, point.x); max = Mathf.Max(max, point.x); }
                bool rectangular = max - min > .0001f;
                foreach (Vector2 point in uv)
                    if ((Mathf.Abs(point.x - min) > .0001f && Mathf.Abs(point.x - max) > .0001f) ||
                        (Mathf.Abs(point.y) > .0001f && Mathf.Abs(point.y - 1) > .0001f)) rectangular = false;
                if (rectangular) {
                    Vector2 content = ContentRange(texture);
                    float tolerance = 4f / texture.width;
                    if (Mathf.Abs(content.x - min) > tolerance || Mathf.Abs(content.y - max) > tolerance) {
                        for (int i = 0; i < uv.Length; i++)
                            uv[i].x = Mathf.Lerp(content.x, content.y, (uv[i].x - min) / (max - min));
                        result = Object.Instantiate(source);
                        result.name = source.name + " (skin UV)";
                        result.uv = uv;
                        owned.Add(result);
                    }
                }
            }
            meshes[key] = result;
            return result;
        }

        private Vector2 ContentRange(Texture2D texture)
        {
            if (contentRanges.TryGetValue(texture, out Vector2 range)) return range;
            Color32[] pixels = texture.GetPixels32();
            int min = texture.width, max = -1;
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                    if (pixels[y * texture.width + x].a != 0) { min = Mathf.Min(min, x); max = Mathf.Max(max, x); }
            range = max < min ? new Vector2(0, 1) :
                new Vector2(Mathf.Max(0, min - 2) / (float)texture.width,
                    Mathf.Min(texture.width, max + 3) / (float)texture.width);
            contentRanges[texture] = range;
            return range;
        }

        public void Clear()
        {
            foreach (Mesh mesh in owned)
                if (mesh != null) {
                    if (Application.isPlaying) Object.Destroy(mesh);
                    else Object.DestroyImmediate(mesh);
                }
            owned.Clear();
            meshes.Clear();
            contentRanges.Clear();
        }
    }
}
