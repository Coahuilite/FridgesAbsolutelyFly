// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    /// <summary>Uses normal hauling reservations and toils; never teleports items. / 使用普通搬运预留和流程，不传送物品。</summary>
    public sealed class JobDriver_LoadCabinet : JobDriver
    {
        private bool usesMattressPreparation = true;
        private bool legacyClearingConfirmed;
        private Building_FlyingFridgeCabinet Cabinet { get { return job.targetB.Thing as Building_FlyingFridgeCabinet; } }

        public override void ExposeData()
        {
            // Pawn_JobTracker binds job after LoadingVars; migrate before base rebuilds toils in PostLoadInit.
            // Pawn_JobTracker在LoadingVars后绑定job；须在PostLoadInit的基类重建toil前迁移。
            if (Scribe.mode == LoadSaveMode.PostLoadInit && legacyClearingConfirmed)
            {
                Building_FlyingFridgeCabinet.AuthorizeMattressClearing(job);
                legacyClearingConfirmed = false;
            }
            base.ExposeData();
            // Old saved jobs keep their original toil indices and never gain unconfirmed clearing.
            // 旧任务保留原toil索引，不因更新获得未经确认的清仓步骤。
            Scribe_Values.Look(ref usesMattressPreparation, "fafUsesMattressPreparation", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref legacyClearingConfirmed, "fafMattressClearingConfirmed", false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnForbidden(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => Cabinet == null || !Cabinet.Spawned);
            AddFinishAction(condition =>
            {
                if (pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing == job.targetA.Thing && pawn.Spawned)
                {
                    Thing dropped;
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
                }
            });
            if (usesMattressPreparation && job.targetA.Thing?.GetInnerIfMinified() is Building_CabinetMattress)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
                yield return Toils_General.Do(() =>
                {
                    if (!Cabinet.PrepareMattressInstallation(pawn, job)) EndJobWith(JobCondition.Incompletable);
                });
            }
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                .FailOn(() => !Cabinet.CanAcceptItem(job.targetA.Thing));
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
                .FailOn(() => !Cabinet.CanAcceptItem(pawn.carryTracker.CarriedThing));
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.B);
            yield return Toils_General.Do(() =>
            {
                if (!Cabinet.TryStoreCarried(pawn)) EndJobWith(JobCondition.Incompletable);
            });
        }
    }

    /// <summary>Container-only hauling because upstream requires the storage-building base class. / 上游要求储存建筑基类，因此为真正容器提供独立搬运入口。</summary>
    public sealed class WorkGiver_LoadCabinet : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest { get { return ThingRequest.ForDef(CabinetDefOf.FAF_FlyingFridgeCabinet); } }
        public override PathEndMode PathEndMode { get { return PathEndMode.Touch; } }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Building_FlyingFridgeCabinet cabinet = thing as Building_FlyingFridgeCabinet;
            if (cabinet == null || cabinet.Faction != pawn.Faction || !cabinet.AutomaticLoading
                || cabinet.IsForbidden(pawn) || !pawn.CanReserveAndReach(cabinet, PathEndMode.Touch, Danger.Some)) return null;
            StoragePriority priority = cabinet.GetStoreSettings().Priority;
            Thing item = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch,
                TraverseParms.For(pawn), 9999f, candidate =>
                    candidate.Spawned && !candidate.IsForbidden(pawn)
                    && !(candidate.GetInnerIfMinified() is Building_CabinetMattress)
                    && cabinet.CanAcceptItem(candidate)
                    && StoreUtility.CurrentStoragePriorityOf(candidate) < priority
                    && pawn.CanReserve(candidate));
            return item == null ? null : cabinet.MakeLoadJob(item);
        }
    }
}
