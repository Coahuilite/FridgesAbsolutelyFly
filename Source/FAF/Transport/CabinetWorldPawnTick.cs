// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Transport
{
    // Native WorldPawns ticks its live set before world-object owners. Only that exact set owns these pawn ticks.
    // 原生WorldPawns先更新活跃集合，再更新世界物体容器；仅该确切活跃集合负责这些角色的tick。
    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.DoTick))]
    internal static class CabinetWorldPawnTick
    {
        private static AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> livePawns;
        internal static bool Available;
        private static bool Prepare()
        {
            try { livePawns = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsAlive"); return true; }
            catch (Exception exception)
            {
                Log.Error("[F.A.F.] Native live-world-pawn binding changed; new freezer world flights are disabled.\n" + exception);
                return false;
            }
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            var tick = AccessTools.Method(typeof(Thing), nameof(Thing.DoTick));
            int matches = 0; foreach (CodeInstruction instruction in code) if (instruction.Calls(tick)) matches++;
            Available = matches == 1;
            if (!Available)
            {
                Log.Error("[F.A.F.] Native owner tick anchor changed; new freezer world flights are disabled.");
                return code;
            }
            List<CodeInstruction> result = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in code)
            {
                if (!instruction.Calls(tick)) { result.Add(instruction); continue; }
                var owner = new CodeInstruction(OpCodes.Ldarg_0); owner.MoveLabelsFrom(instruction);
                result.Add(owner); result.Add(CodeInstruction.Call(typeof(CabinetWorldPawnTick), nameof(TickNativeItem)));
            }
            return result;
        }
        private static void TickNativeItem(Thing thing, ThingOwner owner)
        {
            if (thing is Pawn pawn && owner.Owner is ActiveTransporterInfo info && IsOurWorldPayload(info)
                && Find.WorldPawns != null && livePawns(Find.WorldPawns).Contains(pawn)) return;
            // Mothballed/dead/unregistered pawns and every other mod retain the original owner path.
            // 冷藏休眠/死亡/未登记角色及所有其他模组仍走原生容器路径，不用IsWorldPawn冒充活跃集合。
            thing.DoTick();
        }
        private static bool IsOurWorldPayload(ActiveTransporterInfo info)
        {
            return info.parent is CabinetRecoverySite || info.parent is CabinetTravellingTransporters
                || (info.parent is TravellingTransporters && info.sentTransporterDef?.thingClass == typeof(Cabinet.Building_FlyingFridgeCabinet));
        }
    }
}
