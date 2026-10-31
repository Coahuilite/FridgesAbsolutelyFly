// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    /// <summary>Reuse vanilla uninstall work, then carry the same mattress into the cabinet. / 复用原版卸下工作，再把同一个床垫搬入柜内。</summary>
    public sealed class JobDriver_InstallCabinetMattress : JobDriver_RemoveBuilding
    {
        private bool usesMattressPreparation = true;
        private bool legacyClearingConfirmed;
        private Building_FlyingFridgeCabinet Cabinet { get { return job.targetB.Thing as Building_FlyingFridgeCabinet; } }
        protected override DesignationDef Designation { get { return null; } }
        protected override float TotalNeededWork { get { return Building.def.building.uninstallWork; } }
        protected override EffecterDef WorkEffecter { get { return null; } }

        public override void ExposeData()
        {
            // Job is bound after LoadingVars; preserve positive old consent before native toil reconstruction.
            // job在LoadingVars后才绑定；在原生toil重建前迁移旧存中的正确认意图。
            if (Scribe.mode == LoadSaveMode.PostLoadInit && legacyClearingConfirmed)
            {
                Building_FlyingFridgeCabinet.AuthorizeMattressClearing(job);
                legacyClearingConfirmed = false;
            }
            base.ExposeData();
            // Preserve native progress and toil indices for jobs saved before the clearing workflow.
            // 清仓流程之前保存的任务仍保留原生进度与toil索引。
            Scribe_Values.Look(ref usesMattressPreparation, "fafUsesMattressPreparation", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref legacyClearingConfirmed, "fafMattressClearingConfirmed", false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed)
                && base.TryMakePreToilReservations(errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnForbidden(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => Cabinet == null || !Cabinet.Spawned);
            AddFinishAction(condition =>
            {
                if (pawn.Spawned && pawn.carryTracker.CarriedThing != null
                    && pawn.carryTracker.CarriedThing == job.targetA.Thing)
                {
                    Thing dropped;
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
                }
            });

            if (usesMattressPreparation)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
                yield return Toils_General.Do(() =>
                {
                    if (!Cabinet.PrepareMattressInstallation(pawn, job)) EndJobWith(JobCondition.Incompletable);
                });
            }

            // Vanilla owns work timing, progress, de-registration and designation cleanup. / 原版负责工作耗时、进度、注销和指令清理。
            foreach (Toil uninstall in base.MakeNewToils())
                yield return uninstall.FailOn(() => Cabinet.GetDirectlyHeldThings().Any);

            yield return Toils_General.Do(() =>
            {
                MinifiedThing packed = job.targetC.Thing as MinifiedThing;
                if (packed == null || !packed.Spawned)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                // Target A was switched atomically in FinishedRemoving. / 目标A已在卸下完成边界原子切换。
            });
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDestroyedNullOrForbidden(TargetIndex.A)
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

        protected override void FinishedRemoving()
        {
            // Uninstall wraps the original Thing; quality, stuff, hit points and identity survive. / 卸下只包裹原Thing，保留品质、材料、耐久与对象身份。
            Thing installed = Target;
            Map map = pawn.Map;
            MinifiedThing packed = Building.Uninstall();
            // The base toil cleans Target after this callback; remove the old Thing's designations first.
            // 基类会在此回调后清理Target；切换目标前，先清理原建筑的指令。
            map.designationManager.RemoveAllDesignationsOn(installed);
            job.SetTarget(TargetIndex.C, packed);
            job.SetTarget(TargetIndex.A, packed);
            pawn.records.Increment(RecordDefOf.ThingsUninstalled);
        }
    }
}
