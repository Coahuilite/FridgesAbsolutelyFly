// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    // Native hauling has no container-purpose argument. This scope exists only during an actual native load job.
    // 原生搬运没有容器用途参数；此作用域仅存在于真实原生运输装载Job的调用期间。
    internal static class CabinetTransportLoading
    {
        [ThreadStatic] internal static JobDriver_HaulToTransporter Current;
        internal static bool IsFor(Building_FlyingFridgeCabinet cabinet)
        {
            return Current != null && Current.pawn?.jobs?.curDriver == Current
                && Current.Container == cabinet && cabinet.UpgradeProfile.WorldLaunchUnlocked;
        }
        internal static bool Locked(Thing destination)
        {
            Building_FlyingFridgeCabinet cabinet = destination as Building_FlyingFridgeCabinet;
            return cabinet != null && !cabinet.UpgradeProfile.WorldLaunchUnlocked;
        }
    }

    [HarmonyPatch]
    internal static class CabinetNativeLoadingScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Native global/toil failure checks and all init actions (including actual deposit and queue advance).
            // 覆盖原生全局/toil失败检查及全部initAction（包括实际入舱及转到下一目标）。
            yield return AccessTools.Method(typeof(JobDriver), "CheckCurrentToilEndOrFail");
            yield return AccessTools.Method(typeof(JobDriver), "TryActuallyStartNextToil");
        }
        private static void Prefix(JobDriver __instance, out JobDriver_HaulToTransporter __state)
        {
            __state = CabinetTransportLoading.Current;
            CabinetTransportLoading.Current = __instance as JobDriver_HaulToTransporter;
        }
        private static Exception Finalizer(Exception __exception, JobDriver_HaulToTransporter __state)
        {
            CabinetTransportLoading.Current = __state;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.TryGetInnerInteractableThingOwner))]
    internal static class CabinetNativeLoadingOwner
    {
        private static bool Prefix(Thing thing, ref ThingOwner __result)
        {
            Building_FlyingFridgeCabinet cabinet = thing as Building_FlyingFridgeCabinet;
            if (cabinet == null || !CabinetTransportLoading.IsFor(cabinet)) return true;
            __result = cabinet.GetComp<CompCabinetTransport>().innerContainer;
            return false;
        }
    }
    [HarmonyPatch(typeof(FloatMenuOptionProvider_CarryingPawn), "CarryToTransporter")]
    internal static class LockedCabinetCarryMenu
    {
        private static bool Prefix(Thing clickedThing, Pawn carriedPawn, ref FloatMenuOption option, ref bool __result)
        {
            Building_FlyingFridgeCabinet cab = clickedThing as Building_FlyingFridgeCabinet;
            if (cab == null) return true;
            AcceptanceReport report = CabinetOccupancy.CanAdmit(cab, carriedPawn);
            if (report) return true;
            option = new FloatMenuOption("CannotPlaceIn".Translate(carriedPawn, clickedThing) + ": " + report.Reason, null); __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(JobDriver_EnterTransporter), "MakeNewToils")]
    internal static class LockedCabinetBoardingJob
    {
        private static void Prefix(JobDriver_EnterTransporter __instance)
        {
            __instance.AddEndCondition(() => CabinetTransportLoading.Locked(__instance.job.targetA.Thing)
                ? JobCondition.Incompletable : JobCondition.Ongoing);
        }
    }
    [HarmonyPatch(typeof(JobDriver_HaulToContainer), "MakeNewToils")]
    internal static class LockedCabinetLoadingJob
    {
        private static void Prefix(JobDriver_HaulToContainer __instance)
        {
            if (__instance is JobDriver_HaulToTransporter)
                __instance.AddEndCondition(() => CabinetTransportLoading.Locked(__instance.Container)
                    || (__instance.Container is Building_FlyingFridgeCabinet cab && __instance.ThingToCarry is Pawn pawn
                        && !CabinetOccupancy.CanAdmit(cab, pawn))
                    || (__instance.Container is Building_FlyingFridgeCabinet cargoCab && !(__instance.ThingToCarry is Pawn)
                        && !CabinetBiologicalPayload.CanAcceptCargo(cargoCab, __instance.ThingToCarry))
                    ? JobCondition.Incompletable : JobCondition.Ongoing);
        }
    }
}
