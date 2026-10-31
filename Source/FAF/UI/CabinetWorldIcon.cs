// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using FAF.Transport;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Fit this flight's existing texture without changing native world interaction bounds. / 原贴图等比适配本柜航程，不改变原生世界交互边界。</summary>
    [StaticConstructorOnStartup]
    public static class CabinetWorldIcon
    {
        private static readonly MaterialPropertyBlock FadeProperties = new MaterialPropertyBlock();

        public static void Draw(CabinetTravellingTransporters flight)
        {
            // Same surface/transition/altitude policy as WorldObject.Draw in both supported references.
            // 沿两版WorldObject.Draw原有的地表、切换淡出与高度规则，仅调整quad的宽高比。
            Material material = flight.Material;
            if (flight.Tile.LayerDef.isSpace || !material) return;
            float transition = ExpandableWorldObjectsUtility.RawTransitionPct;
            float altitude = flight.DrawAltitude + Rand.RangeSeeded(0f, 0.01f, flight.ID) + flight.def.drawAltitudeOffset;
            MaterialPropertyBlock properties = null;
            if (flight.def.expandingIcon && transition > 0f && !ExpandableWorldObjectsUtility.HiddenByRules(flight))
            {
                Color color = material.color;
                FadeProperties.SetColor(ShaderPropertyIDs.Color, new Color(color.r, color.g, color.b, color.a * (1f - transition)));
                properties = FadeProperties;
            }
            float size = 0.7f * flight.Tile.Layer.AverageTileSize;
            Vector2 fitted = FitSize(new Vector2(size, size), material.mainTexture);
            Vector3 normal = flight.DrawPos.normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(normal, Vector3.up), normal);
            Matrix4x4 matrix = Matrix4x4.TRS(flight.DrawPos + normal * altitude, rotation, new Vector3(fitted.x, 1f, fitted.y));
            // Keep the native tangential projection and draw layer; ordinary markers are not screen billboards.
            // 保留原生切平面投影与绘制层，不把普通标记改成屏幕朝向的公告板。
            if (properties != null) Graphics.DrawMesh(MeshPool.plane10, matrix, material, WorldCameraManager.WorldLayer, null, 0, properties);
            else Graphics.DrawMesh(MeshPool.plane10, matrix, material, WorldCameraManager.WorldLayer);
        }

        public static Rect ExpandedDrawRect(WorldObject worldObject, float factor)
        {
            Rect rect = ExpandableWorldObjectsUtility.ExpandedIconScreenRect(worldObject, factor);
            if (!(worldObject is CabinetTravellingTransporters)) return rect;
            Vector2 fitted = FitSize(rect.size, worldObject.ExpandingIcon);
            return new Rect(rect.center.x - fitted.x * 0.5f, rect.center.y - fitted.y * 0.5f, fitted.x, fitted.y);
        }

        private static Vector2 FitSize(Vector2 available, Texture texture)
        {
            float scale = Mathf.Min(available.x / texture.width, available.y / texture.height);
            return new Vector2(texture.width * scale, texture.height * scale);
        }
    }

    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
    internal static class CabinetExpandedWorldIconAspect
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            var original = AccessTools.Method(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandedIconScreenRect));
            int matches = 0;
            foreach (CodeInstruction instruction in code) if (instruction.Calls(original)) matches++;
            if (matches != 1)
            {
                Log.Error("[F.A.F.] Expanding world-icon draw anchor changed; freezer icon aspect correction is unavailable.");
                return code;
            }
            // Only the paint call changes. Native hit testing, selection brackets, flip, route rotation and tint stay intact.
            // 只替换绘制用矩形；原生命中、选择框、翻转、沿线旋转与着色仍走原路径。
            foreach (CodeInstruction instruction in code)
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CabinetWorldIcon), nameof(CabinetWorldIcon.ExpandedDrawRect));
                }
            return code;
        }
    }
}
