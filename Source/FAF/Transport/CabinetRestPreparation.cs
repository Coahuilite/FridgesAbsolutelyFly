// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet;
using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;

namespace FAF.Transport
{
    // One explicit rest arrangement, not an occupant or a free pressurisation switch.
    // 唯一明确休息安排，不是住客登记或免费加压开关。
    internal static class CabinetRestPreparation
    {
        [System.ThreadStatic] private static Building_FlyingFridgeCabinet checkingCabinet;
        [System.ThreadStatic] private static Pawn checkingPawn;
        internal static bool BedAvailableAfterPreparation(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            Building_FlyingFridgeCabinet previousCabinet = checkingCabinet;
            Pawn previousPawn = checkingPawn;
            try
            {
                checkingCabinet = cab; checkingPawn = pawn;
                return Cabinet.Integration.CabinetBedVacuumScope.CanUseAfterPressurePreparation(CabinetOccupancy.ProxyFor(cab), pawn);
            }
            finally { checkingCabinet = previousCabinet; checkingPawn = previousPawn; }
        }
        internal static bool IsValid(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !cab.Spawned || !cab.UpgradeProfile.HeatingUnlocked
                || !CabinetSleepTransit.IsEligible(pawn) || pawn.MapHeld != cab.Map) return false;
            var bed = CabinetOccupancy.ProxyFor(cab);
            if (bed?.Spawned != true || !bed.IsActive) return false;
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            if (CabinetBiologicalPayload.Read(CabinetBiologicalPayload.Roots(cab)).Corpses.Count > 0
                || CabinetBiologicalPayload.PendingCorpses(bay) || CabinetBiologicalPayload.Read(new[] { pawn }).Corpses.Count > 0) return false;
            if (bay.innerContainer.OfType<Pawn>().Any(p => p != pawn) || bed.CurOccupants.Any(p => p != pawn)) return false;
            if (bay.leftToLoad?.Any(entry => entry.CountToTransfer > 0 && entry.things.OfType<Pawn>().Any(p => p != pawn)) == true) return false;
            bool arranged = bay.innerContainer.Contains(pawn) || pawn.CurJob?.def == DefDatabase<JobDef>.GetNamed("FAF_PrepareCabinetRest")
                && pawn.CurJob.targetA.Thing == cab;
            // The native berth postfix reads this same live view; only that exact synchronous query may re-enter.
            // 原生席位后置补丁会回读同一实时视图；只放行这个确切同步资格查询的重入。
            return arranged && (checkingCabinet == cab && checkingPawn == pawn || BedAvailableAfterPreparation(cab, pawn));
        }
        internal static void Clear(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (cab?.GetComp<CompCabinetTransport>().RestPreparationTarget == pawn)
                cab.GetComp<CompCabinetTransport>().RestPreparationTarget = null;
        }
        internal static void TickHeld(CompCabinetTransport bay)
        {
            Pawn pawn = bay.RestPreparationTarget;
            if (pawn == null) return;
            if (!IsValid(bay.Cabinet, pawn)) { Clear(bay.Cabinet, pawn); return; }
            if (!bay.innerContainer.Contains(pawn) || bay.Cabinet.InternalVacuum >= 0.5f && pawn.HarmedByVacuum) return;
            Clear(bay.Cabinet, pawn);
            CabinetOccupancy.RequestRest(bay.Cabinet, pawn);
        }
    }
    public sealed class JobDriver_PrepareCabinetRest : JobDriver
    {
        private Building_FlyingFridgeCabinet Cabinet { get { return job.targetA.Thing as Building_FlyingFridgeCabinet; } }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Cabinet == null || !CabinetRestPreparation.IsValid(Cabinet, pawn));
            AddFinishAction(condition => CabinetRestPreparation.Clear(Cabinet, pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = ToilMaker.MakeToil("WaitForCabinetAtmosphere");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.tickAction = () => {
                if (!Cabinet.OccupiedRect().ExpandedBy(1).Contains(pawn.Position)) { EndJobWith(JobCondition.Incompletable); return; }
                var bed = CabinetOccupancy.ProxyFor(Cabinet);
                if (pawn.HarmedByVacuum && Cabinet.InternalVacuum >= 0.5f) return;
                if (!RestUtility.CanUseBedNow(bed, pawn, false)) { EndJobWith(JobCondition.Incompletable); return; }
                CabinetRestPreparation.Clear(Cabinet, pawn);
                // Native tuck starts the actual bed job; the preparation job never sets CurrentBed itself.
                // 原生安置开始真实床任务；准备任务不自行设置CurrentBed。
                RestUtility.TuckIntoBed(bed, pawn, pawn, false);
            };
            yield return wait;
        }
    }
}
