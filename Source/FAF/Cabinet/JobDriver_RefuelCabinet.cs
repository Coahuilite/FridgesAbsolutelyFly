// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    /// <summary>Native physical hauling with the cabinet's shared whole-bottle target. / 原生实体搬运，采用本柜统一整瓶目标。</summary>
    public sealed class JobDriver_RefuelCabinet : JobDriver
    {
        private Building_FlyingFridgeCabinet Cabinet { get { return job.targetA.Thing as Building_FlyingFridgeCabinet; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Cabinet != null && Cabinet.CanContinueExternalCoolant(pawn, job)
                && pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            AddFailCondition(() => Cabinet == null || !Cabinet.CanContinueExternalCoolant(pawn, job));
            AddFinishAction(condition => DropRemainingBottle(pawn, job));

            yield return Toils_Reserve.Reserve(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_General.DoAtomic(() =>
            {
                Thing fuel = job.targetB.Thing;
                if (!Cabinet.CanContinueExternalCoolant(pawn, job) || !Cabinet.IsCoolantBottle(fuel)
                    || !fuel.Spawned || fuel.Map != pawn.Map)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                job.count = System.Math.Min(fuel.stackCount, Cabinet.CoolantBottleBudget());
            });
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: false, subtractNumTakenFromJobCount: true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(JobDriver_Refuel.RefuelingDuration)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                if (Cabinet.RefuelExternalCarriedCoolant(pawn, job) <= 0) EndJobWith(JobCondition.Incompletable);
            });
        }

        internal static void DropRemainingBottle(Pawn worker, Job request)
        {
            if (worker.Spawned && worker.carryTracker.CarriedThing != null
                && worker.carryTracker.CarriedThing == request.targetB.Thing)
                worker.carryTracker.TryDropCarriedThing(worker.Position, ThingPlaceMode.Near, out _);
        }
    }
}
