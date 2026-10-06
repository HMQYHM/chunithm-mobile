using UnityEngine;

namespace ChunithmMobile.Rendering
{
    // 渲染资源工厂：创建共享材质、透明材质和基础 Mesh。
    // 资源所有权要明确：共享资源不应由单个 NoteVisual 随意销毁，动态资源才由池/渲染器释放。
    public static class MeshFactory
    {
        public static Mesh Quad(Vector3 nearLeft, Vector3 nearRight, Vector3 farRight, Vector3 farLeft)
        {
            var mesh = new Mesh { name = "Chart quad" };
            mesh.vertices = new[] { nearLeft, nearRight, farRight, farLeft };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        public static Material Material(Color color, bool transparent = false)
        {
            Shader shader = Shader.Find(transparent ? "Legacy Shaders/Transparent/Diffuse" : "Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = color };
            material.enableInstancing = true;
            if (transparent) material.renderQueue = 3000;
            return material;
        }

        public static Material Material(Texture texture, Color tint, bool transparent = false)
        {
            Shader shader = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = tint, mainTexture = texture };
            material.enableInstancing = true;
            if (transparent) material.renderQueue = 3000;
            return material;
        }
    }
}
