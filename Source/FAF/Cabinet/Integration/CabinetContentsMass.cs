// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using RimWorld;
using FAF.Transport;
using Verse;
using System.Collections.Generic;
using System.Reflection;

namespace FAF.Cabinet.Integration
{
    /// <summary>Only the hull receives TOFU lift; add both real compartments afterwards. / TOFU只减轻柜壳，再加入两仓真实内物；包裹原生继承一次。</summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    internal static class CabinetContentsMassPatch
    {
        private static void Postfix(StatRequest req, bool applyPostProcess, StatDef ___stat, ref float __result)
        {
            if (___stat != StatDefOf.Mass) return;
            Building_FlyingFridgeCabinet cabinet = req.Thing as Building_FlyingFridgeCabinet;
            if (cabinet != null) __result = __result * cabinet.UpgradeProfile.HullMassFactor + cabinet.GetHeldMass(applyPostProcess);
        }
    }

    /// <summary>Native bay notifications also invalidate the carried cabinet's stat and caravan state. / 原生运输舱变动也使所携冷柜的属性与远行队状态失效。</summary>
    [HarmonyPatch]
    internal static class CabinetBayMassChanged
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompTransporter), nameof(CompTransporter.Notify_ThingAdded));
            yield return AccessTools.Method(typeof(CompTransporter), nameof(CompTransporter.Notify_ThingRemoved));
            yield return AccessTools.Method(typeof(CompTransporter), nameof(CompTransporter.Notify_ThingAddedAndMergedWith));
        }
        private static void Postfix(CompTransporter __instance)
        {
            if (__instance is CompCabinetTransport bay) bay.Cabinet.NotifyContentsMassChanged();
        }
    }
}
