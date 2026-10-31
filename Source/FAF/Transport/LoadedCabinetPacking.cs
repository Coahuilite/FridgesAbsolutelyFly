// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    internal static class LoadedCabinetPacking
    {
        internal static bool Blocked(Thing thing)
        {
            Building_FlyingFridgeCabinet cabinet = thing as Building_FlyingFridgeCabinet;
            CompCabinetTransport bay = cabinet?.GetComp<CompCabinetTransport>();
            return bay != null && !bay.Transferring && (bay.innerContainer.Any || bay.AnythingLeftToLoad || bay.ResidentRestPawn != null);
        }
    }

    [HarmonyPatch(typeof(Designator_Uninstall), nameof(Designator_Uninstall.CanDesignateThing))]
    internal static class LoadedCabinetUninstallDesignation
    {
        private static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (LoadedCabinetPacking.Blocked(t)) __result = "FAF_UnloadBeforePacking".Translate();
        }
    }
    [HarmonyPatch(typeof(Designator_Install), nameof(Designator_Install.CanDesignateCell))]
    internal static class LoadedCabinetReinstallDesignation
    {
        private static void Postfix(ref AcceptanceReport __result)
        {
            if (LoadedCabinetPacking.Blocked(Find.Selector.SingleSelectedThing)) __result = "FAF_UnloadBeforePacking".Translate();
        }
    }
    [HarmonyPatch(typeof(JobDriver_RemoveBuilding), "MakeNewToils")]
    internal static class LoadedCabinetUninstallJob
    {
        private static void Prefix(JobDriver_RemoveBuilding __instance)
        {
            if (__instance is JobDriver_Uninstall)
                __instance.AddEndCondition(() => LoadedCabinetPacking.Blocked(__instance.job.targetA.Thing)
                    ? JobCondition.Incompletable : JobCondition.Ongoing);
        }
    }
    [HarmonyPatch(typeof(Toils_Construct), nameof(Toils_Construct.UninstallIfMinifiable))]
    internal static class LoadedCabinetPackingToil
    {
        private static void Postfix(TargetIndex thingInd, Toil __result)
        {
            // Reinstallation and caravan/portal hauling share this real native uninstall toil.
            // 重新安装及远行队/传送门搬运共用此真实原生卸下toil。
            __result.FailOn(() => LoadedCabinetPacking.Blocked(__result.actor.CurJob.GetTarget(thingInd).Thing));
        }
    }
}
