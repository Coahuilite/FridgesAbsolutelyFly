// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using NivarianSleepInFridges;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet.Integration
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.AmbientTemperature), MethodType.Getter)]
    internal static class CabinetOccupantTemperaturePatch
    {
        private static void Postfix(Thing __instance, ref float __result)
        {
            if (__instance is Pawn pawn && Building_FlyingFridgeCabinet.TryGetCabinEnvironment(pawn,
                out float temperature, out _)) __result = temperature;
        }
    }

    /// <summary>Replace only the environment-dependent native leaf, at its original pawn tick call site. / 仅替换依赖环境的原生叶子，仍从角色原tick调用。</summary>
    [HarmonyPatch(typeof(VacuumUtility), nameof(VacuumUtility.PawnVacuumTickInterval))]
    internal static class CabinetOccupantVacuumExposurePatch
    {
        private static bool Prefix(Pawn pawn, int delta)
        {
            if (!Building_FlyingFridgeCabinet.TryGetCabinEnvironment(pawn, out _, out float vacuum)) return true;
            if (ModsConfig.OdysseyActive && pawn.IsHashIntervalTick(60, delta) && pawn.HarmedByVacuum
                && vacuum >= VacuumUtility.MinVacuumForDamage)
            {
                // This is the native 1.6/1.7 exposure formula, not a second health simulation.
                // 这是1.6/1.7原生暴露公式，并非第二份健康模拟。
                float severity = 0.02f * vacuum * Mathf.Max(1f - pawn.GetStatValue(StatDefOf.VacuumResistance), 0f);
                if (severity > 0f) HealthUtility.AdjustSeverity(pawn, HediffDefOf.VacuumExposure, severity);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffComp_VacuumExposure), nameof(HediffComp_VacuumExposure.CompPostTickInterval))]
    internal static class CabinetOccupantVacuumRecoveryPatch
    {
        private static bool Prefix(HediffComp_VacuumExposure __instance, ref float severityAdjustment, int delta)
        {
            if (!Building_FlyingFridgeCabinet.TryGetCabinEnvironment(__instance.Pawn, out _, out float vacuum)) return true;
            if (ModsConfig.OdysseyActive && __instance.Pawn.IsHashIntervalTick(60, delta))
            {
                // A held pawn is not automatically unexposed: use the same cabin pressure as damage.
                // 被持有不等于未暴露；恢复与伤害读取同一柜内压力。
                if (vacuum < VacuumUtility.MinVacuumForDamage
                    || __instance.Pawn.GetStatValue(StatDefOf.VacuumResistance) >= 1f)
                    severityAdjustment += __instance.Props.severityPerSecondUnexposed;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffGiver_VacuumBurn), nameof(HediffGiver_VacuumBurn.OnIntervalPassed))]
    internal static class CabinetOccupantVacuumBurnPatch
    {
        // Reuse native balance and the pawn's native burn timestamp; do not invent another injury clock.
        // 复用原生平衡与角色原生烧伤时间戳，不另建伤害时钟。
        private static readonly SimpleCurve burnRate = (SimpleCurve)AccessTools.Field(
            typeof(HediffGiver_VacuumBurn), "VacuumSecondsBurnRate").GetValue(null);
        private static readonly IntRange burnDamage = (IntRange)AccessTools.Field(
            typeof(HediffGiver_VacuumBurn), "BurnDamageRange").GetValue(null);
        private static bool Prefix(Pawn pawn)
        {
            if (!Building_FlyingFridgeCabinet.TryGetCabinEnvironment(pawn, out _, out float vacuum)) return true;
            if (ModsConfig.OdysseyActive && pawn.HarmedByVacuum && vacuum >= VacuumUtility.MinVacuumForDamage
                && GenTicks.TicksGame - pawn.lastVacuumBurntTick >= burnRate.Evaluate(vacuum).SecondsToTicks())
            {
                pawn.lastVacuumBurntTick = GenTicks.TicksGame;
                if (VacuumUtility.TryGetVacuumBurnablePart(pawn, out BodyPartRecord part))
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.VacuumBurn, burnDamage.RandomInRange, 999f, -1f, null, part));
            }
            return false;
        }
    }

    // Bed eligibility still runs every native/upstream check. Only its one target-cell vacuum query is scoped.
    // 床位资格仍执行全部原生/前置检查；仅将目标床格的真空查询限制到本柜气压。
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedNow))]
    internal static class CabinetBedVacuumScope
    {
        [ThreadStatic] internal static Building_FridgeBedProxy Current;
        [ThreadStatic] private static Building_FridgeBedProxy pressurePreparationBed;
        internal static bool CheckingPressurePreparation(Building_FridgeBedProxy bed)
        {
            return bed != null && bed == pressurePreparationBed;
        }
        internal static bool CanUseAfterPressurePreparation(Building_FridgeBedProxy bed, Pawn pawn)
        {
            if (bed == null || pawn == null || !(bed.ParentFridge is Building_FlyingFridgeCabinet)) return false;
            Building_FridgeBedProxy previous = pressurePreparationBed;
            pressurePreparationBed = bed;
            try
            {
                // Preflight only: every native/upstream condition still runs, apart from this exact bed's pressure query.
                // 仅作准备资格预检：除这张床的压力查询外，全部原生/前置条件仍照常执行。
                return RestUtility.CanUseBedNow(bed, pawn, false);
            }
            finally { pressurePreparationBed = previous; }
        }
        internal static float ExteriorVacuumAt(IntVec3 cell, Map map)
        {
            Building_FridgeBedProxy previous = Current;
            Current = null;
            try { return cell.GetVacuum(map); }
            finally { Current = previous; }
        }
        private static void Prefix(Thing bedThing, out Building_FridgeBedProxy __state)
        {
            __state = Current;
            Building_FridgeBedProxy bed = bedThing as Building_FridgeBedProxy;
            Current = bed?.ParentFridge is Building_FlyingFridgeCabinet ? bed : null;
        }
        private static Exception Finalizer(Building_FridgeBedProxy __state, Exception __exception)
        {
            Current = __state;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(VacuumUtility), nameof(VacuumUtility.GetVacuum))]
    internal static class CabinetBedCellVacuumPatch
    {
        private static bool Prefix(IntVec3 cell, Map map, ref float __result)
        {
            Building_FridgeBedProxy bed = CabinetBedVacuumScope.Current;
            if (bed == null || !bed.Spawned || bed.Map != map || bed.Position != cell) return true;
            __result = CabinetBedVacuumScope.CheckingPressurePreparation(bed)
                ? 0f : ((Building_FlyingFridgeCabinet)bed.ParentFridge).InternalVacuum;
            return false;
        }
    }
}
