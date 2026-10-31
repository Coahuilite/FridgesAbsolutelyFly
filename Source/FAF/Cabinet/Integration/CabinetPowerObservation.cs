// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using RimWorld;

namespace FAF.Cabinet.Integration
{
    /// <summary>Observe the native successful settlement leaf; do not settle or draw energy again. / 观察原生成功结算叶子；不重新结算或扣电。</summary>
    [HarmonyPatch(typeof(PowerNet), "ChangeStoredEnergy")]
    internal static class CabinetNativePowerSettlementObservation
    {
        private static void Postfix(PowerNet __instance)
        {
            foreach (CompPowerTrader trader in __instance.powerComps)
                if (trader is CompFridgeCabinetPowerLoad cabinetLoad) cabinetLoad.ObserveNativeGridSettlement(__instance);
        }
    }
    [HarmonyPatch(typeof(PowerNet), nameof(PowerNet.PowerNetTick))]
    internal static class CabinetNativePowerTickObservation
    {
        private static void Prefix(PowerNet __instance)
        {
            // Quote on the native settlement clock, after the map's ordinary building updates.
            // 沿原生结算时钟报价，位于地图普通建筑更新之后。
            foreach (CompPowerTrader trader in __instance.powerComps)
                if (trader is CompFridgeCabinetPowerLoad cabinetLoad) cabinetLoad.BeginNativeGridTick(__instance);
        }
        private static void Postfix(PowerNet __instance)
        {
            // No allocation, new registry or whole-map search; inspect this net's existing native list only.
            // 不分配列表、不新增注册表或扫描地图；仅查看本电网既有原生负载列表。
            foreach (CompPowerTrader trader in __instance.powerComps)
                if (trader is CompFridgeCabinetPowerLoad cabinetLoad) cabinetLoad.CompleteNativeGridTick(__instance);
        }
    }
}
