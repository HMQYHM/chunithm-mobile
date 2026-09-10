using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Runtime
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TouchContactOverlay : MaskableGraphic
    {
        private const int Segments = 18;
        private bool visible = true;
        private float airBoundary = .17f;
        private float opacity = 1f;
        private int previousTouchCount = -1;

        public void Configure(bool show, float boundary, float effectOpacity = 1f)
        {
            visible = show;
            airBoundary = Mathf.Clamp01(boundary);
            opacity = Mathf.Clamp01(effectOpacity);
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        private void Update()
        {
            int count = visible ? UnityEngine.Input.touchCount : 0;
            if (count > 0 || count != previousTouchCount) SetVerticesDirty();
            previousTouchCount = count;
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (!visible || Screen.width <= 0 || Screen.height <= 0) return;
            Rect bounds = rectTransform.rect;
            for (int index = 0; index < UnityEngine.Input.touchCount; index++)
            {
                Touch touch = UnityEngine.Input.GetTouch(index);
                if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled) continue;
                Vector2 normalized = new Vector2(touch.position.x / Screen.width, touch.position.y / Screen.height);
                Vector2 point = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, normalized.x),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, normalized.y));
                Color tint = normalized.y <= airBoundary
                    ? new Color(.14f, .94f, 1f, 1f)
                    : new Color(1f, .38f, .78f, 1f);
                AddDisc(vertices, point, 38f, WithAlpha(tint, .20f * opacity), WithAlpha(tint, 0f));
                AddRing(vertices, point, 14f, 19f, WithAlpha(Color.white, .96f * opacity), WithAlpha(tint, .68f * opacity));
                AddDisc(vertices, point, 11f, WithAlpha(Color.white, .86f * opacity), WithAlpha(tint, .70f * opacity));
            }
        }

        private static void AddDisc(VertexHelper vertices, Vector2 center, float radius,
            Color centerColor, Color edgeColor)
        {
            int first = vertices.currentVertCount;
            AddVertex(vertices, center, centerColor);
            for (int index = 0; index <= Segments; index++)
            {
                float radians = index / (float)Segments * Mathf.PI * 2f;
                AddVertex(vertices, center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius, edgeColor);
                if (index > 0) vertices.AddTriangle(first, first + index, first + index + 1);
            }
        }

        private static void AddRing(VertexHelper vertices, Vector2 center, float innerRadius, float outerRadius,
            Color innerColor, Color outerColor)
        {
            int first = vertices.currentVertCount;
            for (int index = 0; index <= Segments; index++)
            {
                float radians = index / (float)Segments * Mathf.PI * 2f;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                AddVertex(vertices, center + direction * innerRadius, innerColor);
                AddVertex(vertices, center + direction * outerRadius, outerColor);
                if (index == Segments) continue;
                int offset = first + index * 2;
                vertices.AddTriangle(offset, offset + 1, offset + 2);
                vertices.AddTriangle(offset + 1, offset + 3, offset + 2);
            }
        }

        private static void AddVertex(VertexHelper vertices, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            vertices.AddVert(vertex);
        }

        private static Color WithAlpha(Color value, float alpha)
        {
            value.a = alpha;
            return value;
        }
    }
}
