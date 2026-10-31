// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using NivarianSleepInFridges;
using RimWorld;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Read the native field: no second damage counter or prediction. / 读取原生字段，不另建损坏计数或预测。</summary>
    internal static class NativeTemperatureDamage
    {
        // Both supported native versions expose Ruined but keep intermediate progress protected.
        // 两个支持版本均公开Ruined，但将中间进度保留为protected字段。
        private static readonly AccessTools.FieldRef<CompTemperatureRuinable, float> progress =
            AccessTools.FieldRefAccess<CompTemperatureRuinable, float>("ruinedPercent");
        internal static float Progress(CompTemperatureRuinable component) { return progress(component); }
    }

    /// <summary>Version-shared game seams, isolated from the thermal policy. / 与温控规则分离的跨版本游戏接缝。</summary>
    [HarmonyPatch(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.TryGetFixedTemperature))]
    internal static class CabinetHeldTemperaturePatch
    {
        private static void Postfix(IThingHolder holder, Thing forThing, ref float temperature, ref bool __result)
        {
            Building_FlyingFridgeCabinet cabinet = holder as Building_FlyingFridgeCabinet
                ?? (holder as FAF.Transport.CompCabinetTransport)?.parent as Building_FlyingFridgeCabinet;
            if (cabinet == null && holder is ActiveTransporterInfo payload)
            {
                cabinet = FAF.Transport.CabinetPayload.CabinetIn(payload);
                // The cabinet's own ambient query must still reach the exterior, not its own internal temperature.
                // 柜体自身的环境温度查询必须仍指向外界，不能反馈成自己的内温。
                if (cabinet != null && forThing?.GetInnerIfMinified() == cabinet) return;
            }
            if (cabinet == null || cabinet.Destroyed) return;
            temperature = cabinet.InternalTemperature;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.TryGetQuality))]
    internal static class CabinetBedQualityPatch
    {
        private static void Postfix(Thing t, ref QualityCategory qc, ref bool __result)
        {
            Building_FridgeBedProxy bed = t as Building_FridgeBedProxy;
            Building_FlyingFridgeCabinet cabinet = bed == null ? null : bed.ParentFridge as Building_FlyingFridgeCabinet;
            Building_CabinetMattress mattress = cabinet == null ? null : cabinet.Mattress;
            if (mattress == null) return;
            __result = mattress.TryGetQuality(out qc);
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    internal static class CabinetBedStatsPatch
    {
        private static void Postfix(StatRequest req, StatDef ___stat, ref float __result)
        {
            Building_FridgeBedProxy bed = req.Thing as Building_FridgeBedProxy;
            Building_FlyingFridgeCabinet cabinet = bed == null ? null : bed.ParentFridge as Building_FlyingFridgeCabinet;
            if (cabinet == null || !cabinet.HasMattress) return;
            // Quality is finalized by vanilla after these base improvements. / 原版会在这些基础加成后应用床垫品质。
            if (___stat == StatDefOf.BedRestEffectiveness) __result += 0.25f;
            else if (___stat == StatDefOf.ImmunityGainSpeedFactor) __result *= 1.15f;
            else if (___stat == StatDefOf.SurgerySuccessChanceFactor) __result *= 1.10f;
            else if (___stat == StatDefOf.MedicalTendQualityOffset) __result += 0.05f;
        }
    }
}
