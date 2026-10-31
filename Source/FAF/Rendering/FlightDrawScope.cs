// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using NivarianSleepInFridges;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Rendering
{
    internal static class FlightDrawScope
    {
        [ThreadStatic] internal static CompFridgeFlight Current;
        [ThreadStatic] internal static Matrix4x4? ManeuverTransform;

        internal static bool TryGetVisualTransform(out Matrix4x4 transform)
        {
            bool flying = Current != null && Current.Flying;
            bool projectSleeper = CabinetSleeperProjection.Active;
            transform = ManeuverTransform ?? (flying ? Current.VisualTransform : Matrix4x4.identity);
            // Project the fixed-camera cabinet top first, then rotate/lift the complete flying assembly.
            // 先投影到固定镜头柜顶，再让整个飞行组合共同旋转／上浮；第三方冰箱没有此位移。
            if (projectSleeper) transform = transform * Matrix4x4.Translate(Cabinet.CabinetVisualRenderer.TopSurfaceOffset);
            return flying || projectSleeper || ManeuverTransform.HasValue;
        }

        internal static CompFridgeFlight For(Thing thing)
        {
            ThingWithComps withComps = thing as ThingWithComps;
            CompFridgeFlight flight = withComps == null ? null : withComps.GetComp<CompFridgeFlight>();
            if (flight != null && flight.Flying) return flight;
            Pawn pawn = thing as Pawn;
            if (pawn == null || pawn.GetPosture() == PawnPosture.Standing) return null;
            Building_FridgeBedProxy bed = pawn.CurrentBed() as Building_FridgeBedProxy;
            flight = bed == null || bed.ParentFridge == null ? null : bed.ParentFridge.GetComp<CompFridgeFlight>();
            return flight != null && flight.Flying ? flight : null;
        }

        internal static void Enter(Thing thing, out CompFridgeFlight previous)
        {
            previous = Current;
            // EN: Nested content drawing inherits its container's transform, but each mesh is transformed only once.
            // 中：嵌套绘制的内容继承容器变换；每份提交的网格只变换一次。
            CompFridgeFlight own = For(thing);
            if (own != null) Current = own;
        }
    }

    [HarmonyPatch(typeof(Thing), "DynamicDrawPhase")]
    internal static class DynamicFlightDrawScope
    {
        private static void Prefix(Thing __instance, DrawPhase phase, out CompFridgeFlight __state)
        {
            __state = FlightDrawScope.Current;
            if (phase == DrawPhase.Draw) FlightDrawScope.Enter(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, CompFridgeFlight __state)
        {
            FlightDrawScope.Current = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Thing), "DrawNowAt")]
    internal static class ImmediateFlightDrawScope
    {
        private static void Prefix(Thing __instance, out CompFridgeFlight __state)
        {
            FlightDrawScope.Enter(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, CompFridgeFlight __state)
        {
            FlightDrawScope.Current = __state;
            return __exception;
        }
    }
}
