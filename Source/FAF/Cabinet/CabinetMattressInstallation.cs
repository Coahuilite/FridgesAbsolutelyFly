// SPDX-License-Identifier: MPL-2.0
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        // Native current jobs and their reservations are the only installation intent; no saved lock.
        // 原生当前任务及其预约是唯一安装意图，不另存安装锁。
        private Job ReservedMattressInstallation()
        {
            if (!Spawned || !Map.reservationManager.TryGetReserver(this, Faction.OfPlayer, out Pawn worker)) return null;
            Job current = worker.CurJob;
            if (current == null || current.targetB.Thing != this
                || !Map.reservationManager.ReservedBy(this, worker, current)
                || current.def != CabinetDefOf.FAF_InstallCabinetMattress && current.def != CabinetDefOf.FAF_LoadCabinet
                    && !MattressClearingAuthorized(current))
                return null;
            Thing target = current.targetA.Thing;
            return target != null && !target.Destroyed && target.GetInnerIfMinified() is Building_CabinetMattress
                ? current : null;
        }

        internal bool MattressInstallationPending { get { return ReservedMattressInstallation() != null; } }

        internal bool IsPendingMattressTarget(Thing target)
        {
            Job current = ReservedMattressInstallation();
            return current != null && target != null && !target.Destroyed
                && current.targetA.Thing.GetInnerIfMinified() == target.GetInnerIfMinified();
        }

        internal bool CanSelectMattressForInstallation(Thing target)
        {
            if (!Spawned || HasMattress || MattressInstallationPending
                || target == null || target.Destroyed || !target.Spawned || target.Map != Map
                || target.IsForbidden(Faction.OfPlayer) || target.IsBurning()) return false;
            Building_CabinetMattress mattress = target.GetInnerIfMinified() as Building_CabinetMattress;
            return mattress != null && (target is MinifiedThing
                || mattress.Faction == Faction.OfPlayer && mattress.def.Minifiable)
                && CabinetPayloadCapacity.CanInstallAfterClearing(this, target);
        }

        internal void OrderConfirmedMattress(Thing target, bool clearingConfirmed = false)
        {
            if (contents.Any && !clearingConfirmed)
            {
                Messages.Message("FAF_MattressClearConfirmationRequired".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            TryOrderLoading(target, true, clearingConfirmed);
        }

        internal void ConfigureMattressInstallation(Job installation, Pawn worker, bool clearingConfirmed)
        {
            if (clearingConfirmed) AuthorizeMattressClearing(installation);
        }

        // StartJob creates a fresh driver. Authorization belongs to the native Job, whose Def is saved.
        // StartJob会新建driver；授权属于原生Job，其Def随任务保存。
        private static bool MattressClearingAuthorized(Job installation)
        {
            return installation != null && (installation.def == CabinetDefOf.FAF_InstallCabinetMattressClearingConfirmed
                || installation.def == CabinetDefOf.FAF_LoadCabinetMattressClearingConfirmed);
        }

        internal static void AuthorizeMattressClearing(Job installation)
        {
            if (installation == null) return;
            if (installation.def == CabinetDefOf.FAF_InstallCabinetMattress)
                installation.def = CabinetDefOf.FAF_InstallCabinetMattressClearingConfirmed;
            else if (installation.def == CabinetDefOf.FAF_LoadCabinet)
                installation.def = CabinetDefOf.FAF_LoadCabinetMattressClearingConfirmed;
        }

        internal bool PrepareMattressInstallation(Pawn worker, Job installation)
        {
            Thing target = installation.targetA.Thing;
            if (!Spawned || HasMattress || worker.CurJob != installation
                || !Map.reservationManager.ReservedBy(this, worker, installation)
                || !Map.reservationManager.ReservedBy(target, worker, installation)
                || !IsPendingMattressTarget(target) || target.Destroyed || !target.Spawned || target.Map != Map
                || target.IsForbidden(worker) || target.IsBurning()
                || !CabinetPayloadCapacity.CanInstallAfterClearing(this, target)) return false;
            if (target is Building_CabinetMattress mattress
                && (mattress.Faction != Faction.OfPlayer || !mattress.def.Minifiable)) return false;

            if (contents.Any && !MattressClearingAuthorized(installation))
            {
                Messages.Message("FAF_MattressClearConfirmationRequired".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Only drop the main compartment after reservations; independent transport cargo is untouched.
            // 仅在取得预约后移出主仓；独立运输货舱保持原样。
            for (int i = contents.Count - 1; i >= 0; i--)
                if (!Eject(contents[i], contents[i].stackCount))
                {
                    Messages.Message("FAF_MattressClearFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            return !contents.Any;
        }
    }
}
