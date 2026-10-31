// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NivarianSleepInFridges;
using RimWorld;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Replace only upstream's local powered-fridge gate, not its recovery rules or need writer. / 仅替换前置局部供电门槛，保留其恢复规则与需求写入。</summary>
    [HarmonyPatch]
    internal static class CabinetUpstreamRecoveryPatch
    {
        private const string CoreType = "NivarianSleepInFridges.FridgeIcyCoreUtility";
        private const string TailType = "NivarianSleepInFridges.IcecreamTailCompatibility";
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Assembly upstream = typeof(CompFridgeSleep).Assembly;
            MethodInfo core = AccessTools.Method(upstream.GetType(CoreType, true), "RecoverOccupants",
                new[] { typeof(Building_FridgeBedProxy) });
            if (core == null) throw new InvalidOperationException("[F.A.F.] Required upstream icy-core recovery method is unavailable.");
            yield return core;
            // The optional tail bridge must never prevent the required core bridge from loading.
            // 可选尾巴接缝缺失不能阻止必需冰核接缝加载。
            Type tailType = upstream.GetType(TailType, false);
            MethodInfo tail = tailType == null ? null : AccessTools.Method(tailType, "RecoverySpeedFactorPostfix");
            if (tail != null) yield return tail;
            else Log.Warning("[F.A.F.] Optional upstream Icecream Tail bridge is unavailable; its temperature gate was not installed.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo powerOn = AccessTools.PropertyGetter(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn));
            int matches = 0;
            foreach (CodeInstruction instruction in code) if (instruction.Calls(powerOn)) matches++;
            if (matches != 1)
            {
                string message = "[F.A.F.] Upstream recovery power gate changed: " + __originalMethod.Name + ".";
                if (__originalMethod.DeclaringType.FullName == CoreType) throw new InvalidOperationException(message);
                Log.Warning(message + " Optional Icecream Tail bridge remains unchanged.");
                return code;
            }
            MethodInfo localGate = AccessTools.Method(typeof(CabinetUpstreamRecoveryPatch), nameof(RecoveryAllowed));
            foreach (CodeInstruction instruction in code)
                if (instruction.Calls(powerOn))
                {
                    // Same stack input/result; keep labels, branches, settings, proxy and pawn checks intact.
                    // 栈输入与输出相同；保留标签、分支、设置、代理床和角色检查。
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = localGate;
                }
            return code;
        }

        private static bool RecoveryAllowed(CompPowerTrader power)
        {
            Building_FlyingFridgeCabinet cabinet = power.parent as Building_FlyingFridgeCabinet;
            return cabinet == null ? power.PowerOn : cabinet.AllowsTemperatureBasedRecovery;
        }
    }
}
