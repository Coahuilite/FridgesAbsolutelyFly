// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using FAF.Cabinet;
using NivarianSleepInFridges;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    // Death keeps vanilla's corpse and side effects; only its actual cabinet association crosses cleanup.
    // 死亡保留原生遗体与副作用；仅让真实柜体关联跨过任务清理阶段。
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class CabinetDeathHandoff
    {
        [ThreadStatic] private static State current;
        internal sealed class State
        {
            internal Pawn Pawn;
            internal Building_FlyingFridgeCabinet Cabinet;
            internal Map Map;
            internal State Previous;
        }
        internal static bool IsReplacement(Building_FlyingFridgeCabinet cabinet, Thing item)
        {
            return current?.Cabinet == cabinet && item is Corpse corpse && corpse.InnerPawn == current.Pawn;
        }
        private static void Prefix(Pawn __instance, out State __state)
        {
            Building_FlyingFridgeCabinet cabinet = (__instance.ParentHolder as CompCabinetTransport)?.Cabinet;
            if (cabinet == null && __instance.ParentHolder is ActiveTransporterInfo info) cabinet = CabinetPayload.CabinetIn(info);
            if (cabinet == null && __instance.Spawned)
                cabinet = (__instance.CurrentBed() as Building_FridgeBedProxy)?.ParentFridge as Building_FlyingFridgeCabinet;
            __state = new State { Pawn = __instance, Cabinet = cabinet, Map = cabinet?.MapHeld, Previous = current };
            current = __state;
        }
        private static Exception Finalizer(State __state, Exception __exception)
        {
            try { Complete(__state); }
            catch (Exception exception) { Log.Error("[F.A.F.] Death handoff failed; native corpse retained at its actual location.\n" + exception); }
            finally { current = __state.Previous; }
            return __exception;
        }
        private static void Complete(State state)
        {
            Building_FlyingFridgeCabinet cabinet = state.Cabinet;
            if (cabinet == null || !state.Pawn.Dead) return;
            CompCabinetTransport bay = cabinet.GetComp<CompCabinetTransport>();
            if (bay.ResidentRestPawn == state.Pawn) bay.ResidentRestPawn = null;
            if (bay.RestPreparationTarget == state.Pawn) bay.RestPreparationTarget = null;
            if (bay.leftToLoad != null)
            {
                foreach (TransferableOneWay entry in bay.leftToLoad)
                    if (entry.things.Remove(state.Pawn)) entry.AdjustTo(Math.Max(0, entry.CountToTransfer - 1));
                bay.leftToLoad.RemoveAll(entry => entry.CountToTransfer == 0);
            }
            Corpse corpse = state.Pawn.Corpse;
            if (cabinet.Destroyed || corpse == null || corpse.Destroyed || corpse.holdingOwner == bay.innerContainer) return;
            if (corpse.ParentHolder is ActiveTransporterInfo info && CabinetPayload.CabinetIn(info) == cabinet) return;
            // Never claim another holder's corpse or pull a native corpse back into a destroyed/moved cabinet.
            // 不认领他方容器遗体，也不把原生遗体拉进已毁或已移走的柜体。
            if (!cabinet.Spawned || cabinet.Map != state.Map || !corpse.Spawned || corpse.Map != state.Map) return;
            IntVec3 cell = corpse.Position; Rot4 rotation = corpse.Rotation;
            corpse.DeSpawn();
            try
            {
                if (bay.innerContainer.TryAdd(corpse, false)) { bay.InvalidateCargoMass(); return; }
            }
            finally
            {
                if (!corpse.Destroyed && !corpse.Spawned && corpse.holdingOwner == null)
                    GenSpawn.Spawn(corpse, cell, state.Map, rotation);
            }
        }
    }
}
