// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Cabinet.Integration
{
    public sealed class CompProperties_CabinetCoolant : CompProperties_Refuelable
    {
        public CompProperties_CabinetCoolant() { compClass = typeof(CompFridgeCabinetCoolant); }
        // References such as the fixed fuel filter remain read-only; only this copy's capacity changes.
        // 固定燃料筛选器等引用保持只读；仅修改此副本的容量。
        internal CompProperties_CabinetCoolant CopyForInstance() { return (CompProperties_CabinetCoolant)MemberwiseClone(); }
    }

    /// <summary>Scope native eligibility checks without changing global refuelling switches. / 限定原生资格检查作用域，不改全局补液开关。</summary>
    internal static class CabinetLaunchRefuelRequest
    {
        [ThreadStatic] private static Building_FlyingFridgeCabinet current;
        internal static bool IsFor(Building_FlyingFridgeCabinet cabinet) { return current == cabinet; }
        internal static bool CanRefuelNative(Pawn pawn, Building_FlyingFridgeCabinet cabinet)
        {
            Building_FlyingFridgeCabinet previous = current;
            current = cabinet;
            try { return RefuelWorkGiverUtility.CanRefuel(pawn, cabinet, true); }
            finally { current = previous; }
        }
        internal static Job CreateJob(Pawn pawn, Building_FlyingFridgeCabinet cabinet)
        {
            Building_FlyingFridgeCabinet previous = current;
            current = cabinet;
            try
            {
                if (!RefuelWorkGiverUtility.CanRefuel(pawn, cabinet, true)) return null;
                Job job = RefuelWorkGiverUtility.RefuelJob(pawn, cabinet, true, CabinetDefOf.FAF_RefuelCabinetForLaunch);
                job.playerForced = true;
                return job;
            }
            finally { current = previous; }
        }
    }

    /// <summary>The cabinet's single native coolant tank and shared refill configuration. / 本柜唯一原生液仓及统一补液配置。</summary>
    public sealed class CompFridgeCabinetCoolant : CompRefuelable
    {
        private static readonly AccessTools.FieldRef<CompRefuelable, float> ConfiguredTarget =
            AccessTools.FieldRefAccess<CompRefuelable, float>("configuredTargetFuelLevel");
        private CompProperties_CabinetCoolant instanceSettings;
        private bool automaticSupplyEnabled;
        private float legacyAutomaticSupplyTargetUnits = -1f;
        private bool unifiedTargetMigrated = true;
        internal bool AutomaticSupplyEnabled { get { return automaticSupplyEnabled; } }
        // The native configured target is the sole authority; an unset native target follows capacity.
        // 原生配置目标为唯一权威；未设定的原生目标随容量变化。
        internal float SupplyTargetUnits
        {
            get { return UnityEngine.Mathf.Clamp(TargetFuelLevel, 0f, Props.fuelCapacity); }
        }
        internal void SetAutomaticSupplyEnabled(bool enabled) { automaticSupplyEnabled = enabled; }
        internal void SetSupplyTargetUnits(float units)
        {
            if (!float.IsNaN(units) && !float.IsInfinity(units))
                TargetFuelLevel = UnityEngine.Mathf.Clamp(units, 0f, Props.fuelCapacity);
        }
        public override void Initialize(CompProperties properties)
        {
            if (instanceSettings == null) instanceSettings = ((CompProperties_CabinetCoolant)properties).CopyForInstance();
            base.Initialize(instanceSettings);
        }
        internal void RefreshInstanceCapacity(CompProperties_Refuelable settings)
        {
            // No fuel is added, removed or re-scribed. The profile never reads this comp/Props.
            // 不增加、删除或另行保存燃料；profile也不读取此组件或Props，因此没有递归环。
            settings.fuelCapacity = Research.CabinetResearch.Current.CoolantCapacityUnits;
        }
        public override void PostExposeData()
        {
            if (!Scribe.EnterNode("fafCoolantReserve")) return;
            try
            {
                base.PostExposeData();
                Scribe_Values.Look(ref automaticSupplyEnabled, "automaticSupplyEnabled", false);
                Scribe_Values.Look(ref unifiedTargetMigrated, "unifiedTargetMigrated", false);
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                    Scribe_Values.Look(ref legacyAutomaticSupplyTargetUnits, "automaticSupplyTargetUnits", -1f);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && !unifiedTargetMigrated)
                {
                    // Merge configuration once; no fuel, bottles, power or switches change here.
                    // 只合并一次配置；不改变液量、瓶装物、电量或任何开关。
                    float native = ConfiguredTarget(this);
                    bool hasNative = IsExplicitTarget(native);
                    bool hasAutomatic = IsExplicitTarget(legacyAutomaticSupplyTargetUnits);
                    float chosen = automaticSupplyEnabled && hasAutomatic ? legacyAutomaticSupplyTargetUnits
                        : hasNative ? native : hasAutomatic ? legacyAutomaticSupplyTargetUnits : -1f;
                    if (chosen >= 0f) TargetFuelLevel = chosen;
                    else ConfiguredTarget(this) = -1f;
                    unifiedTargetMigrated = true;
                    legacyAutomaticSupplyTargetUnits = -1f;
                }
            }
            finally { Scribe.ExitNode(); }
        }
        private static bool IsExplicitTarget(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
        public override void PostDraw() { /* Optional reserve has no out-of-fuel overlay. / 可选备用仓不显示缺燃料覆盖层。 */ }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Prefs.DevMode) yield break;
            Building_FlyingFridgeCabinet cabinet = (Building_FlyingFridgeCabinet)parent;
            yield return new Command_Action { defaultLabel = "FAF_DevFillCoolant".Translate(), action = cabinet.DevFillCoolant };
            yield return new Command_Action { defaultLabel = "FAF_DevEmptyCoolant".Translate(), action = cabinet.DevEmptyCoolant };
        }
        public override string CompInspectStringExtra()
        {
            return CabinetDefOf.Nivarian_CryoCoolant.LabelCap + ": " + Fuel.ToString("0.##") + " / " + Props.fuelCapacity.ToString("0.##");
        }
    }

    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.Props), MethodType.Getter)]
    internal static class CabinetCoolantCapacityPatch
    {
        private static void Postfix(CompRefuelable __instance, CompProperties_Refuelable __result)
        {
            if (__instance is CompFridgeCabinetCoolant tank) tank.RefreshInstanceCapacity(__result);
        }
    }

    [HarmonyPatch(typeof(RefuelWorkGiverUtility), nameof(RefuelWorkGiverUtility.CanRefuel))]
    internal static class CabinetCoolantRefuelEligibility
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            Building_FlyingFridgeCabinet cabinet = t as Building_FlyingFridgeCabinet;
            if (cabinet == null || CabinetLaunchRefuelRequest.IsFor(cabinet)) return true;
            if (!cabinet.CanRefuelForLaunch) { __result = false; return false; }
            if (!forced && cabinet.AutomaticCoolantSupplyActive)
            {
                __result = cabinet.CanOfferAutomaticExternalCoolant() && pawn.CanReserve(cabinet)
                    && CabinetLaunchRefuelRequest.CanRefuelNative(pawn, cabinet);
                return false;
            }
            if (cabinet.CoolantBackupEnabled) return true;
            JobFailReason.Is("FAF_CoolantBackupDisabled".Translate());
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(RefuelWorkGiverUtility), nameof(RefuelWorkGiverUtility.RefuelJob))]
    internal static class CabinetCoolantRefuelJob
    {
        private static void Postfix(Thing t, bool forced, ref Job __result)
        {
            if (!(t is Building_FlyingFridgeCabinet cabinet) || __result == null
                || __result.def == CabinetDefOf.FAF_RefuelCabinetForLaunch) return;
            __result.def = !forced && cabinet.AutomaticCoolantSupplyActive
                ? CabinetDefOf.FAF_RefuelCabinetAutomatically : CabinetDefOf.FAF_RefuelCabinetManually;
        }
    }

    [HarmonyPatch(typeof(JobDriver_Refuel), "MakeNewToils")]
    internal static class CabinetCoolantRefuelCancellation
    {
        private static void Prefix(JobDriver_Refuel __instance)
        {
            Building_FlyingFridgeCabinet cabinet = __instance.job.targetA.Thing as Building_FlyingFridgeCabinet;
            // Also covers a player-forced refill already queued before backup was disabled.
            // 同时覆盖关闭备用之前已排队的玩家强制补充任务。
            if (cabinet == null) return;
            __instance.AddFailCondition(() => !cabinet.CanContinueExternalCoolant(__instance.pawn, __instance.job));
            __instance.AddFinishAction(condition => JobDriver_RefuelCabinet.DropRemainingBottle(__instance.pawn, __instance.job));
        }

        private static void Postfix(JobDriver_Refuel __instance, ref IEnumerable<Toil> __result)
        {
            if (__instance.job.targetA.Thing is Building_FlyingFridgeCabinet cabinet)
                __result = BoundNativeToils(__instance, cabinet, __result);
        }

        private static IEnumerable<Toil> BoundNativeToils(JobDriver_Refuel driver,
            Building_FlyingFridgeCabinet cabinet, IEnumerable<Toil> nativeToils)
        {
            foreach (Toil toil in nativeToils)
            {
                Action native = toil.initAction;
                toil.initAction = () =>
                {
                    Thing fuel = driver.job.targetB.Thing;
                    if (!cabinet.CanContinueExternalCoolant(driver.pawn, driver.job) || !cabinet.IsCoolantBottle(fuel)
                        || fuel.Spawned && fuel.Map != driver.pawn.Map)
                    {
                        driver.EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    // Keep native toil indices while bounding the count again immediately before physical pickup.
                    // 保留原生toil索引，在真实取料前再次限制瓶数。
                    if (fuel.Spawned) driver.job.count = Math.Min(fuel.stackCount, cabinet.CoolantBottleBudget());
                    native?.Invoke();
                };
                yield return toil;
            }
        }
    }

    // Old saved native drivers keep their toil layout but adopt the same final whole-bottle admission.
    // 旧存原生driver保留toil布局，但最终加注采用同一整瓶接纳。
    [HarmonyPatch(typeof(Toils_Refuel), nameof(Toils_Refuel.FinalizeRefueling))]
    internal static class CabinetCoolantNativeFinalization
    {
        private static void Postfix(TargetIndex refuelableInd, ref Toil __result)
        {
            Toil toil = __result;
            Action native = toil.initAction;
            toil.initAction = () =>
            {
                Pawn worker = toil.actor;
                Job request = worker.CurJob;
                if (request.GetTarget(refuelableInd).Thing is Building_FlyingFridgeCabinet cabinet)
                    cabinet.RefuelExternalCarriedCoolant(worker, request);
                else native();
            };
        }
    }
}
