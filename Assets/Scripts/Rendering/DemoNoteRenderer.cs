using UnityEngine;

namespace ChunithmMobile.Rendering
{
    // 预览音符渲染器：只用于菜单/测试中的轻量展示，不参与正式游玩判定。
    /// <summary>Temporary Vulkan-safe geometry proving every object uses ChartSpace.</summary>
    public sealed class DemoNoteRenderer : MonoBehaviour
    {
        private Material tap, exTap, hold, slide, air;
        private void Awake()
        {
            tap = MeshFactory.Material(new Color(.95f, .08f, .12f, 1));
            exTap = MeshFactory.Material(new Color(1f, .72f, .02f, 1));
            hold = MeshFactory.Material(new Color(.1f, .55f, 1f, .72f), true);
            slide = MeshFactory.Material(new Color(1f, .48f, .02f, .72f), true);
            air = MeshFactory.Material(new Color(.1f, 1f, .22f, .78f), true);
            AddHead("TAP", 1, 3, 5, tap);
            AddHead("ExTAP", 10, 4, 9, exTap);
            AddRibbon("HOLD", 5, 3, .9f, 18, hold);
            AddRibbon("SLIDE", 12, 2, 3, 25, slide, 3, 4);
            AddAirArrow("AIR", 12, 2, 9, 5);
        }

        private void AddHead(string name, float lane, float width, float z, Material material)
        {
            float x = ChartSpace.NoteCenterX(lane, width, z), half = ChartSpace.NoteHalfWidth(width, z);
            AddQuad(name, x - half, x + half, z - .22f, x - half, x + half, z + .22f, .10f, material);
        }

        private void AddRibbon(string name, float lane, float width, float nearZ, float farZ, Material material,
            float endLane = -1, float endWidth = -1)
        {
            if (endLane < 0) endLane = lane; if (endWidth < 0) endWidth = width;
            float nx = ChartSpace.NoteCenterX(lane, width, nearZ), nw = ChartSpace.NoteHalfWidth(width, nearZ);
            float fx = ChartSpace.NoteCenterX(endLane, endWidth, farZ), fw = ChartSpace.NoteHalfWidth(endWidth, farZ);
            AddQuad(name, nx - nw, nx + nw, nearZ, fx - fw, fx + fw, farZ, .035f, material);
        }

        private void AddAirArrow(string name, float lane, float width, float z, float height)
        {
            float x = ChartSpace.NoteCenterX(lane, width, z), half = ChartSpace.NoteHalfWidth(width, z);
            float y = ChartSpace.AirY(height);
            AddQuad(name, x - half, x + half, z - .08f, x, x, z + .45f, y, air);
        }

        private void AddQuad(string name, float nl, float nr, float nz, float fl, float fr, float fz, float y, Material material)
        {
            var child = new GameObject(name); child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad(new Vector3(nl, y, nz), new Vector3(nr, y, nz),
                new Vector3(fr, y, fz), new Vector3(fl, y, fz));
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
