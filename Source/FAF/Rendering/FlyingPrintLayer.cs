// SPDX-License-Identifier: MPL-2.0
using UnityEngine;
using Verse;

namespace FAF.Rendering
{
    /// <summary>Instance-owned mesh made by the building's original Print contract. / 由建筑原始 Print 契约生成的实例专属网格。</summary>
    internal sealed class FlyingPrintLayer
    {
        private readonly ThingWithComps building;
        private readonly SectionLayer_ThingsGeneral layer;
        internal float DrawRadius { get; private set; }

        internal FlyingPrintLayer(Section section, ThingWithComps building)
        {
            this.building = building;
            // EN: Compose the native layer; Section auto-discovers every subclass, so a private capture must not be one.
            // 中：组合原生绘制层；Section 会自动发现所有子类，私有捕获对象不能成为分区子类。
            layer = new SectionLayer_ThingsGeneral(section);
        }

        internal void Regenerate()
        {
            DrawRadius = 0f;
            foreach (LayerSubMesh mesh in layer.subMeshes) mesh.Clear(MeshParts.All);
            // EN: Do not substitute Graphic.Draw: Print preserves directional geometry, atlases and every material layer.
            // 中：不能换成 Graphic.Draw；原 Print 会保留四向几何、图集以及全部材质层。
            SectionLayer previousCapture = GroundPowerWires.CapturingLayer;
            try
            {
                GroundPowerWires.CapturingLayer = layer;
                building.Print(layer);
            }
            finally { GroundPowerWires.CapturingLayer = previousCapture; }
            foreach (LayerSubMesh mesh in layer.subMeshes)
            {
                if (mesh.verts.Count == 0) continue;
                mesh.FinalizeMesh(MeshParts.All);
                // Actual Print geometry includes third-party layers and offsets, not just the logical footprint.
                // 真实 Print 顶点包含第三方附加层和偏移，不能只以逻辑占地推断边界。
                Bounds bounds = new Bounds(mesh.verts[0], Vector3.zero);
                foreach (Vector3 vertex in mesh.verts) bounds.Encapsulate(vertex);
                mesh.mesh.bounds = bounds;
                Vector3 pivot = building.DrawPos;
                float x = Mathf.Max(Mathf.Abs(bounds.min.x - pivot.x), Mathf.Abs(bounds.max.x - pivot.x));
                float z = Mathf.Max(Mathf.Abs(bounds.min.z - pivot.z), Mathf.Abs(bounds.max.z - pivot.z));
                DrawRadius = Mathf.Max(DrawRadius, Mathf.Sqrt(x * x + z * z));
            }
        }

        internal void DrawLayer() { layer.DrawLayer(); }
        internal void Dispose() { layer.Dispose(); }
    }
}
