// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Transport
{
    public enum CabinetArrivalPurpose { OrdinaryArrival, GiftContentsAndLeaveCabinet, RelinquishAtHostileSettlement }

    // A selected settlement purpose, not a generic permission to give away transport payloads.
    // 明确选择的聚落用途，不是赠送任意运输载荷的全局许可。
    public sealed class CabinetSettlementArrival : TransportersArrivalAction
    {
        private Settlement settlement;
        private Faction selectedFaction;
        private CabinetArrivalPurpose purpose;
        public CabinetArrivalPurpose Purpose { get { return purpose; } }
        public override bool GeneratesMap { get { return false; } }
        public CabinetSettlementArrival() { }
        internal CabinetSettlementArrival(Settlement settlement)
        {
            this.settlement = settlement; selectedFaction = settlement.Faction;
            purpose = settlement.Faction.HostileTo(Faction.OfPlayer) ? CabinetArrivalPurpose.RelinquishAtHostileSettlement
                : CabinetArrivalPurpose.GiftContentsAndLeaveCabinet;
        }
        public override void ExposeData()
        {
            Scribe_References.Look(ref settlement, "fafSettlement");
            Scribe_References.Look(ref selectedFaction, "fafSelectedFaction");
            Scribe_Values.Look(ref purpose, "fafArrivalPurpose", CabinetArrivalPurpose.OrdinaryArrival);
        }
        internal static bool CanSelect(Settlement target)
        {
            return target != null && target.Spawned && !target.Destroyed && target.Tile.LayerDef.SurfaceTiles
                && target.Faction != null && target.Faction != Faction.OfPlayer
                && CabinetArrivalTarget.Reason(target, target.Tile) == null;
        }
        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            if (!CanSelect(settlement) || settlement.Tile != destinationTile || settlement.Faction != selectedFaction
                || purpose == CabinetArrivalPurpose.OrdinaryArrival
                || selectedFaction.HostileTo(Faction.OfPlayer) != (purpose == CabinetArrivalPurpose.RelinquishAtHostileSettlement)) return false;
            List<Thing> roots = pods.SelectMany(p => p.GetDirectlyHeldThings() ?? Enumerable.Empty<Thing>()).ToList();
            CabinetBiologicalPayload biology = CabinetBiologicalPayload.Read(roots);
            return !pods.Any(p => CabinetBiologicalPayload.Read(p.GetDirectlyHeldThings() ?? Enumerable.Empty<Thing>()).Conflict) && CabinetResearchReady(roots)
                && biology.Living.All(p => p.RaceProps.Animal && p.Faction == Faction.OfPlayer && !p.IsQuestLodger());
        }
        private static bool CabinetResearchReady(List<Thing> roots)
        {
            List<Building_FlyingFridgeCabinet> cabinets = roots.OfType<MinifiedThing>()
                .Select(t => t.InnerThing).OfType<Building_FlyingFridgeCabinet>().ToList();
            // Before packing, launch validation owns cabinet/research checks and holders are the actual bays.
            // 打包前由提交方检查真实柜体和科研；此时holder仍是原运输舱。
            return cabinets.Count == 0 || cabinets.All(cab => cab.UpgradeProfile.AutonomyUnlocked);
        }
        public static IEnumerable<Thing> GiftCandidates(Building_FlyingFridgeCabinet cabinet, ActiveTransporterInfo payload = null)
        {
            if (cabinet == null) return Enumerable.Empty<Thing>();
            IEnumerable<Thing> cold = cabinet.HasMattress ? Enumerable.Empty<Thing>() : cabinet.Contents;
            IEnumerable<Thing> transport = cabinet.GetComp<CompCabinetTransport>().innerContainer;
            if (payload != null) transport = transport.Concat(payload.innerContainer.Where(t => t.GetInnerIfMinified() != cabinet));
            return cold.Concat(transport).Where(t => !t.Destroyed).Distinct();
        }
        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (!StillValid(transporters, tile)) { CabinetRecoverySite.Create(tile, transporters).FinishDelivery(); return; }
            if (purpose == CabinetArrivalPurpose.RelinquishAtHostileSettlement) { Relinquish(transporters, tile); return; }
            CabinetRecoverySite site = CabinetRecoverySite.Create(tile, transporters);
            bool failed = false; bool empty = true;
            List<GiftMove> moves = new List<GiftMove>();
            List<ActiveTransporterInfo> gifts = transporters.Select(_ => new ActiveTransporterInfo()).ToList();
            List<CompCabinetTransport> bays = transporters.Select(CabinetPayload.CabinetIn).Where(c => c != null)
                .Select(c => c.GetComp<CompCabinetTransport>()).ToList();
            foreach (CompCabinetTransport bay in bays) bay.Transferring = true;
            using (CabinetDeliveryScope scope = new CabinetDeliveryScope(transporters))
            {
                try
                {
                    for (int index = 0; index < transporters.Count; index++)
                    {
                        Building_FlyingFridgeCabinet cabinet = CabinetPayload.CabinetIn(transporters[index]);
                        if (cabinet == null) throw new InvalidOperationException("Settlement gift lost its original cabinet.");
                        foreach (Thing thing in GiftCandidates(cabinet, transporters[index]).ToList())
                        {
                            ThingOwner owner = thing.holdingOwner;
                            scope.Register(thing, transporters[index]);
                            GiftMove move = new GiftMove { Thing = thing, Owner = owner, Source = transporters[index], Gift = gifts[index] };
                            moves.Add(move);
                            if (owner == null || owner.TryTransferToContainer(thing, gifts[index].innerContainer, thing.stackCount, false) != thing.stackCount)
                                throw new InvalidOperationException("Native owner refused gift staging.");
                        }
                    }
                    empty = !gifts.Any(g => g.innerContainer.Any);
                    // Exactly one native batch: do not change rounding/relationship curves with per-item calls.
                    // 仅调用一次原生批量赠礼，不以逐物调用改变取整或关系曲线。
                    if (!empty) new TransportersArrivalAction_GiveGift(settlement).Arrived(gifts, tile);
                }
                catch (Exception exception)
                {
                    failed = true;
                    Log.Error("[F.A.F.] Settlement gift stopped; only actual undelivered objects are retained. Native gift events will not be replayed.\n" + exception);
                }
                finally
                {
                    foreach (GiftMove move in moves)
                    {
                        try
                        {
                            Thing thing = move.Thing;
                            bool given = thing.Destroyed || thing is Pawn pawn && pawn.Faction == selectedFaction;
                            if (given) { scope.MarkTerminal(thing); continue; }
                            if (thing.Spawned || thing.holdingOwner != null && thing.holdingOwner != move.Gift.innerContainer) continue;
                            if (thing.holdingOwner == null) move.Gift.innerContainer.TryAdd(thing, false);
                            if (move.Gift.innerContainer.Contains(thing))
                            {
                                move.Gift.innerContainer.TryTransferToContainer(thing, move.Owner ?? move.Source.innerContainer, thing.stackCount, false);
                                if (move.Gift.innerContainer.Contains(thing))
                                    move.Gift.innerContainer.TryTransferToContainer(thing, move.Source.innerContainer, thing.stackCount, false);
                            }
                        }
                        catch (Exception exception)
                        {
                            failed = true;
                            Log.Error("[F.A.F.] Gift remainder transfer failed; original native owners retain the surviving payload.\n" + exception);
                        }
                    }
                    foreach (ActiveTransporterInfo gift in gifts) if (gift.innerContainer.Any) { failed = true; site.RetainAdditional(gift); }
                    foreach (CompCabinetTransport bay in bays) { bay.Transferring = false; bay.InvalidateCargoMass(); bay.Cabinet.NotifyContentsMassChanged(); }
                    site.SetGiftOutcome(failed);
                }
            }
            site.FinishDelivery();
            if (empty && !failed) Messages.Message("FAF_GiftEmptyDelivery".Translate(), site, MessageTypeDefOf.TaskCompletion);
        }
        private sealed class GiftMove
        {
            internal Thing Thing;
            internal ThingOwner Owner;
            internal ActiveTransporterInfo Source, Gift;
        }
        private void Relinquish(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            bool failed = false;
            using (CabinetDeliveryScope scope = new CabinetDeliveryScope(transporters))
            {
                try
                {
                    foreach (ActiveTransporterInfo info in transporters)
                    {
                        Building_FlyingFridgeCabinet cabinet = CabinetPayload.CabinetIn(info);
                        if (cabinet == null) throw new InvalidOperationException("Relinquishment lost its original cabinet.");
                        List<Thing> cargo = CabinetBiologicalPayload.Roots(cabinet, info).Distinct().ToList();
                        foreach (Thing thing in cargo)
                        {
                            if (thing is Pawn pawn)
                            {
                                // No caravan membership here: vanilla's caravan-only left-to-die kill branch cannot run.
                                // 此时无远行队成员关系，不进入原生仅远行队采用的弃置致死分支。
                                try { PawnBanishUtility.Banish(pawn, tile); }
                                finally
                                {
                                    if (pawn.Faction != Faction.OfPlayer && pawn.HostFaction != Faction.OfPlayer)
                                    {
                                        scope.MarkTerminal(pawn);
                                        pawn.holdingOwner?.Remove(pawn);
                                        pawn.DestroyOrPassToWorld();
                                    }
                                }
                                if (pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer)
                                    throw new InvalidOperationException("Native banishment did not relinquish the animal.");
                            }
                            else { thing.Notify_AbandonedAtTile(tile); thing.Destroy(); }
                            scope.MarkTerminal(thing);
                        }
                        foreach (Thing wrapper in info.innerContainer.ToList())
                        { wrapper.Destroy(); info.innerContainer.Remove(wrapper); scope.MarkTerminal(wrapper); }
                    }
                    Messages.Message("FAF_RelinquishedAtSettlement".Translate(), new GlobalTargetInfo(tile), MessageTypeDefOf.NegativeEvent);
                }
                catch (Exception exception)
                {
                    failed = true;
                    Log.Error("[F.A.F.] Relinquishment stopped; remaining original undelivered payload retained.\n" + exception);
                }
            }
            if (failed) CabinetRecoverySite.Create(tile, transporters).FinishDelivery();
        }
    }
}
