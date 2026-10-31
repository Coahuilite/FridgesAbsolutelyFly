// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Command icons use image proportions, never the world's draw quad. / 指令图标使用图像比例，而非世界绘制面尺寸。</summary>
    public static class CabinetCommandIcons
    {
        public static void Apply(Command command, Texture texture)
        {
            command.icon = texture;
            command.iconProportions = texture == null ? Vector2.one : new Vector2(texture.width, texture.height);
        }
        public static void ApplyCabinet(Command command, Building_FlyingFridgeCabinet cabinet)
        {
            Apply(command, CabinetVisualRenderer.IconFor(cabinet));
        }
    }

    [HarmonyPatch(typeof(Designator_Build), MethodType.Constructor, new[] { typeof(BuildableDef) })]
    internal static class CabinetBuildDesignatorProportions
    {
        private static void Postfix(Designator_Build __instance, BuildableDef entDef)
        {
            if (entDef == CabinetDefOf.FAF_FlyingFridgeCabinet)
                CabinetCommandIcons.Apply(__instance, entDef.uiIcon);
        }
    }

    [HarmonyPatch(typeof(BuildCopyCommandUtility), nameof(BuildCopyCommandUtility.BuildCommand))]
    internal static class CabinetCopyCommandProportions
    {
        private static void Postfix(BuildableDef buildable, Command __result)
        {
            if (__result != null && buildable == CabinetDefOf.FAF_FlyingFridgeCabinet)
                CabinetCommandIcons.Apply(__result, CabinetVisualRenderer.CurrentIcon);
        }
    }
}
