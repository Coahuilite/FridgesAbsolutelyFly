// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using NivarianSleepInFridges;
using FAF.Cabinet;
using RimWorld;
using Verse;

namespace FAF.Rendering
{
    /// <summary>
    /// Project only our cabinet's sleeper onto its raised top, preserving native size and cached graphics.
    /// 仅将本扩展柜内的睡眠者投影到抬高顶面，保留原生尺寸和缓存绘制。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    internal static class CabinetSleeperProjection
    {
        [ThreadStatic] internal static bool Active;

        private static void Prefix(Pawn ___pawn, out bool __state)
        {
            __state = Active;
            Building_FridgeBedProxy bed = ___pawn.GetPosture() == PawnPosture.Standing ? null
                : ___pawn.CurrentBed() as Building_FridgeBedProxy;
            Active = bed != null && bed.ParentFridge is Building_FlyingFridgeCabinet;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            Active = __state;
            return __exception;
        }
    }
}
