// SPDX-License-Identifier: MPL-2.0
using FAF.Transport;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        internal bool IsCoolantBottle(Thing item)
        {
            return item != null && !item.Destroyed && item.stackCount > 0 && item.def == CabinetDefOf.Nivarian_CryoCoolant
                && CoolantTank.Props.fuelFilter.Allows(item);
        }

        internal bool CanOfferAutomaticExternalCoolant()
        {
            return AutomaticCoolantSupplyActive && Spawned && CanManageThermostat() && CoolantBottleBudget() > 0
                && !GetComp<CompCabinetTransport>().Transferring && !HasOwnCoolantBottles();
        }

        internal bool CanContinueExternalCoolant(Pawn worker, Job request)
        {
            if (worker == null || !worker.Spawned || !Spawned || worker.Map != Map || Faction != worker.Faction
                || !CanManageThermostat() || CoolantBottleBudget() <= 0
                || GetComp<CompCabinetTransport>().Transferring) return false;
            if (request.def == CabinetDefOf.FAF_RefuelCabinetAutomatically)
                return AutomaticCoolantSupplyActive && !HasOwnCoolantBottles();
            if (request.def == CabinetDefOf.FAF_RefuelCabinetForLaunch) return true;
            // A legacy unforced native job joins the new local-first policy while DILL is active.
            // DILL启用时，旧非强制原生任务也须遵守柜内库存优先。
            if (!request.playerForced && AutomaticCoolantSupplyActive) return !HasOwnCoolantBottles();
            return CoolantBackupEnabled && (request.playerForced
                || CoolantTank.allowAutoRefuel && CoolantTank.ShouldAutoRefuelNowIgnoringFuelPct);
        }

        internal int RefuelExternalCarriedCoolant(Pawn worker, Job request)
        {
            if (!CanContinueExternalCoolant(worker, request)) return 0;
            Thing item = worker.carryTracker.CarriedThing;
            ThingOwner owner = worker.carryTracker.GetDirectlyHeldThings();
            if (item != request.targetB.Thing || !IsCoolantBottle(item) || !owner.Contains(item)) return 0;
            return ConsumeCoolantBottles(owner, item, Mathf.Min(item.stackCount, CoolantBottleBudget()));
        }
    }
}
