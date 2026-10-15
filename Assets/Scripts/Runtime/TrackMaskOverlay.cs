using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    // 轨道遮罩和边界显示层：绘制触控板、触控范围线、AIR 分界线及相关透明度。
    // 显示开关只影响 Graphic，不影响输入读取和判定。
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrackMaskOverlay : MaskableGraphic
    {
        private float opacity = .9f;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Configure(bool enabled, int heightPercent, int opacityPercent)
        {
            gameObject.SetActive(enabled);
            if (!enabled) return;

            float height = Mathf.Clamp(heightPercent, 4, 45) / 100f;
            rectTransform.anchorMin = new Vector2(0f, 1f - height);
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            opacity = Mathf.Clamp(opacityPercent, 20, 100) / 100f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect rect = rectTransform.rect;
            Color dark = new Color(.004f, .008f, .014f, opacity);
            // This layer is a complete cover over the selected track region.
            // The old lower transparent strip only darkened one section, so
            // notes farther behind it remained visible when the handle moved.
            AddStrip(vertices, rect.xMin, rect.xMax, rect.yMin, rect.yMax, dark, dark);
        }

        private static void AddStrip(VertexHelper vertices, float left, float right, float bottom, float top,
            Color bottomColor, Color topColor)
        {
            int first = vertices.currentVertCount;
            AddVertex(vertices, new Vector2(left, bottom), bottomColor);
            AddVertex(vertices, new Vector2(right, bottom), bottomColor);
            AddVertex(vertices, new Vector2(right, top), topColor);
            AddVertex(vertices, new Vector2(left, top), topColor);
            vertices.AddTriangle(first, first + 2, first + 1);
            vertices.AddTriangle(first, first + 3, first + 2);
        }

        private static void AddVertex(VertexHelper vertices, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertices.AddVert(vertex);
        }
    }
}
