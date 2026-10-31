// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using NivarianSleepInFridges;
using FAF.Cabinet;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    // The saved reference records a real bay-to-bed handoff, not another occupant or a protected map location.
    // 保存引用只记录真实舱到床的交接意图，不是第二乘员或受保护的地图位置。
    internal static class CabinetRestHandoff
    {
        [ThreadStatic] internal static Pawn CleaningPawn;
        internal static Building_FlyingFridgeCabinet Capture(Pawn pawn)
        {
            Building_FridgeBedProxy bed = pawn?.CurJob?.targetA.Thing as Building_FridgeBedProxy;
            Building_FlyingFridgeCabinet cab = bed?.ParentFridge as Building_FlyingFridgeCabinet;
            if (cab == null || !cab.Spawned || !pawn.Spawned || pawn.Dead || pawn.Map != cab.Map
                || pawn.CurJob.def != JobDefOf.LayDown || !cab.OccupiedRect().Contains(pawn.Position)) return null;
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            return bay.ResidentRestPawn == pawn && !bay.Transferring ? cab : null;
        }
        internal static void ValidateIntent(CompCabinetTransport bay)
        {
            if (bay.ResidentRestPawn != null && Capture(bay.ResidentRestPawn) != bay.Cabinet) bay.ResidentRestPawn = null;
        }
        internal static void Complete(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (cab == null) return;
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            if (bay.ResidentRestPawn != pawn || bay.Transferring) return;
            if (!cab.Spawned || !pawn.Spawned || pawn.Dead || pawn.Map != cab.Map || !cab.OccupiedRect().Contains(pawn.Position))
            { bay.ResidentRestPawn = null; return; }
            IntVec3 cell = pawn.Position; bool previous = bay.Transferring;
            bay.Transferring = true;
            try
            {
                pawn.pather.StopDead(); pawn.DeSpawn();
                if (bay.innerContainer.TryAdd(pawn, false)) { bay.ResidentRestPawn = null; return; }
                GenSpawn.Spawn(pawn, cell, cab.Map);
                Log.Error("[F.A.F.] Native bay refused its resting resident; original pawn restored to the map, not destroyed.");
            }
            finally { bay.Transferring = previous; }
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    internal static class CabinetRestJobCleanup
    {
        private sealed class State
        {
            internal Pawn Previous;
            internal Building_FlyingFridgeCabinet Cabinet;
        }
        private static void Prefix(Pawn ___pawn, out State __state)
        {
            __state = new State { Previous = CabinetRestHandoff.CleaningPawn, Cabinet = CabinetRestHandoff.Capture(___pawn) };
            if (__state.Cabinet != null) CabinetRestHandoff.CleaningPawn = ___pawn;
        }
        private static Exception Finalizer(Pawn ___pawn, State __state, Exception __exception)
        {
            CabinetRestHandoff.CleaningPawn = __state.Previous;
            // Native cleanup has already released reservations and finished the real bed driver.
            // 原生清理已释放预约并结束真实床driver；不伪造CurrentBed，也不强制继续睡眠。
            if (__exception == null) CabinetRestHandoff.Complete(__state.Cabinet, ___pawn);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class CabinetRestEndJob
    {
        private static void Prefix(Pawn ___pawn, ref bool startNewJob)
        {
            if (CabinetRestHandoff.Capture(___pawn) != null) startNewJob = false;
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class CabinetRestReplacementJob
    {
        private static bool Prefix(Pawn_JobTracker __instance, Pawn ___pawn, Job newJob)
        {
            Building_FlyingFridgeCabinet cab = CabinetRestHandoff.Capture(___pawn);
            if (cab == null && CabinetRestHandoff.CleaningPawn != ___pawn) return true;
            if (CabinetRestHandoff.CleaningPawn != ___pawn)
                __instance.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            if (newJob != null) { ___pawn.ClearReservationsForJob(newJob); JobMaker.ReturnToPool(newJob); }
            return false;
        }
    }
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.DeSpawn))]
    internal static class CabinetResidentBedRemoved
    {
        private static void Prefix(Building_Bed __instance)
        {
            Building_FlyingFridgeCabinet cab = (__instance as Building_FridgeBedProxy)?.ParentFridge as Building_FlyingFridgeCabinet;
            Pawn resident = cab?.GetComp<CompCabinetTransport>().ResidentRestPawn;
            if (resident != null && CabinetRestHandoff.Capture(resident) == cab)
                resident.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }
    }
}
