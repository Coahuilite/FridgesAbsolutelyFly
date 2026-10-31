// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Private per-cabinet configuration, not a second energy store. / 每柜私有配置，不是第二份储能。</summary>
    public sealed class CompProperties_CabinetBattery : CompProperties_Battery
    {
        public CompProperties_CabinetBattery() { compClass = typeof(CompFridgeCabinetBattery); }
        internal CompProperties_CabinetBattery CopyForInstance() { return (CompProperties_CabinetBattery)MemberwiseClone(); }
    }

    [HarmonyPatch(typeof(CompPowerBattery), nameof(CompPowerBattery.Props), MethodType.Getter)]
    internal static class CabinetBatteryCapacityPatch
    {
        private static void Postfix(CompPowerBattery __instance, CompProperties_Battery __result)
        {
            if (__instance is CompFridgeCabinetBattery battery) battery.RefreshInstanceCapacity(__result);
        }
    }

    /// <summary>Keep native discharge and inspection, replacing only their fixed wattage for this cabinet. / 保留原生扣电与检查文字，仅替换本柜的固定自放电瓦数。</summary>
    [HarmonyPatch]
    internal static class CabinetBatterySelfDischargePatch
    {
        private const float NativeSelfDischargeWatts = 5f;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompPowerBattery), nameof(CompPowerBattery.CompTick));
            yield return AccessTools.Method(typeof(CompPowerBattery), nameof(CompPowerBattery.CompInspectStringExtra));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            // Both supported native methods contain one 5 W constant; fail visibly if that contract changes.
            // 两版原生方法均仅含一处5W常量；若该调用契约变化则明确报错，不静默改错位置。
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int matches = 0;
            foreach (CodeInstruction instruction in code)
                if (IsNativeWattage(instruction)) matches++;
            if (matches != 1)
                throw new InvalidOperationException("[F.A.F.] Native battery self-discharge changed in "
                    + __originalMethod.Name + "; expected exactly one wattage constant.");

            MethodInfo wattage = AccessTools.Method(typeof(CabinetBatterySelfDischargePatch), nameof(WattsFor));
            foreach (CodeInstruction instruction in code)
            {
                if (!IsNativeWattage(instruction)) { yield return instruction; continue; }
                // Reuse the original instruction's labels/blocks; never draw 5 W and refund the difference.
                // 复用原指令的标签与块标记；不先扣5W再退差额。
                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, wattage);
            }
        }

        private static bool IsNativeWattage(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float watts
                && watts == NativeSelfDischargeWatts;
        }

        private static float WattsFor(CompPowerBattery battery)
        {
            return battery is CompFridgeCabinetBattery cabinet ? cabinet.SelfDischargeWatts : NativeSelfDischargeWatts;
        }
    }
}
