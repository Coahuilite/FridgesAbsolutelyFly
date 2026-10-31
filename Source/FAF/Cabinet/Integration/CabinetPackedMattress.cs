// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Only the packed presentation changes; the native wrapper and inner bed remain intact. / 只改变打包显示；保留原生包裹和其中的床实体。</summary>
    [StaticConstructorOnStartup]
    internal static class CabinetPackedMattress
    {
        internal const string TexturePath = "FAF/Things/PortableSleepingPadPacked";
        internal static readonly Texture2D Texture = ContentFinder<Texture2D>.Get(TexturePath);
        internal static readonly Graphic ClearCrate = GraphicDatabase.Get<Graphic_Single>(
            BaseContent.ClearTex, ShaderDatabase.Cutout, Vector2.one, Color.white, 0);

        internal static Building_CabinetMattress MattressInside(MinifiedThing package)
        {
            Building_CabinetMattress mattress = package.InnerThing as Building_CabinetMattress;
            return mattress != null && mattress.def == CabinetDefOf.FAF_CabinetMattress ? mattress : null;
        }

        internal static bool IsPacked(Building_CabinetMattress mattress)
        {
            MinifiedThing package = mattress.ParentHolder as MinifiedThing;
            if (package == null || MattressInside(package) != mattress) return false;
            // The cabinet deliberately retains the wrapper, but its fitted pad is unfolded in the bed domain.
            // 柜体有意保留包裹；但在床的领域状态中，已装入的睡垫已经铺开。
            Building_FlyingFridgeCabinet cabinet = package.ParentHolder as Building_FlyingFridgeCabinet;
            return cabinet == null || cabinet.Mattress != mattress;
        }

        internal static Graphic GraphicFor(Building_CabinetMattress mattress)
        {
            // Native material caching keeps the original stuff/paint colors without storing another state.
            // 原生材质缓存保留原材料或染色颜色，不另存一份状态。
            return GraphicDatabase.Get<Graphic_Single>(TexturePath, ShaderDatabase.Cutout, Vector2.one,
                mattress.DrawColor, mattress.DrawColorTwo);
        }
    }

    [HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.Graphic), MethodType.Getter)]
    internal static class PackedMattressGraphicPatch
    {
        private static void Postfix(MinifiedThing __instance, ref Graphic __result)
        {
            Building_CabinetMattress mattress = CabinetPackedMattress.MattressInside(__instance);
            if (mattress != null) __result = CabinetPackedMattress.GraphicFor(mattress);
        }
    }

    [HarmonyPatch(typeof(MinifiedThing), "LoadCrateFrontGraphic")]
    internal static class PackedMattressCratePatch
    {
        private static void Postfix(MinifiedThing __instance, ref Graphic __result)
        {
            // The rolled pad is already a complete item sprite; omit only its native wooden crate front.
            // 卷好的睡垫本身就是完整物品图，只省略它自己的原生木箱前板。
            if (CabinetPackedMattress.MattressInside(__instance) != null)
                __result = CabinetPackedMattress.ClearCrate;
        }
    }

    [HarmonyPatch(typeof(Widgets), "ThingIconWorker")]
    internal static class CabinetItemIconProportionsPatch
    {
        private static bool Prefix(Rect rect, ThingDef thingDef, Texture resolvedIcon,
            float resolvedIconAngle, float scale, Material mat, float alpha)
        {
            // World projections are not UI aspect ratios: the pad is 1x2, while the freezer uses a 4x4 canvas.
            // 世界投影尺寸不等于UI比例：睡垫为1x2，冷柜则使用4x4画布。
            // Match only known cabinet-stage and packed-pad icons; other Defs and unfolded pads remain native.
            // 仅匹配已知冷柜阶段图和打包睡垫；其他Def及铺开的睡垫仍使用原生行为。
            bool packedPad = thingDef?.thingClass == typeof(Building_CabinetMattress)
                && thingDef == CabinetDefOf.FAF_CabinetMattress
                && ReferenceEquals(resolvedIcon, CabinetPackedMattress.Texture);
            bool freezer = thingDef?.thingClass == typeof(Building_FlyingFridgeCabinet)
                && thingDef == CabinetDefOf.FAF_FlyingFridgeCabinet
                && thingDef.uiIconPath == "FAF/UI/FlyingCabinet"
                && CabinetVisualRenderer.IsCabinetIcon(resolvedIcon);
            if (!packedPad && !freezer) return true;
            if (freezer) resolvedIcon = CabinetVisualRenderer.CurrentIcon;

            // Keep native color/grayscale/offset preparation and native fitted-draw scale, angle, material and alpha.
            // 保留原生颜色、灰阶、偏移准备，以及适配绘制的缩放、角度、材质与透明度。
            Widgets.DrawTextureFitted(rect, resolvedIcon, scale,
                new Vector2(resolvedIcon.width, resolvedIcon.height), new Rect(0f, 0f, 1f, 1f),
                resolvedIconAngle, mat, alpha);
            return false;
        }
    }
}
