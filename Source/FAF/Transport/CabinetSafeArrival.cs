// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Transport
{
    internal static class CabinetArrivalTarget
    {
        internal static bool BlocksFlight(WorldObject target)
        {
            return target != null && !target.Destroyed && target.Spawned
                && (!target.def.validLaunchTarget || target.RequiresSignalJammerToReach);
        }
        internal static string Reason(WorldObject target, PlanetTile tile)
        {
            if (target == null) return null;
            return target.Destroyed || !target.Spawned || target.Tile != tile || !target.def.validLaunchTarget
                || target.RequiresSignalJammerToReach ? "FAF_InvalidDestination".Translate().ToString() : null;
        }
    }
    // Keep the native arrival action, but never enter its null-action destructive fallback.
    // 保留原生抵达动作，但不进入空动作的破坏性后备分支。
    public sealed class CabinetSafeArrival : TransportersArrivalAction
    {
        private TransportersArrivalAction intended;
        private PlanetTile origin = PlanetTile.Invalid;
        private WorldObject receiver;
        private bool receiverRequired;
        public PlanetTile Origin { get { return origin; } }
        public CabinetSafeArrival() { }
        internal CabinetSafeArrival(TransportersArrivalAction intended) { this.intended = intended; }
        internal CabinetSafeArrival(TransportersArrivalAction intended, PlanetTile origin) { this.intended = intended; this.origin = origin; }
        internal CabinetSafeArrival(TransportersArrivalAction intended, PlanetTile origin, WorldObject receiver)
        {
            this.intended = intended; this.origin = origin; this.receiver = receiver;
            receiverRequired = receiver != null;
        }
        public override bool GeneratesMap { get { return intended?.GeneratesMap == true; } }
        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile tile) { return true; }
        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            // Queueing is harmless if the target disappears before the queued action runs; Arrived rechecks it.
            // 排队后目标消失也不丢物；Arrived 会重新检查目标。
            return GeneratesMap;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref intended, "intendedArrival");
            Scribe_Values.Look(ref origin, "fafOriginTile");
            Scribe_References.Look(ref receiver, "fafArrivalReceiver");
            Scribe_Values.Look(ref receiverRequired, "fafArrivalReceiverRequired", false);
        }
        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (intended is CabinetSettlementArrival settlementArrival)
            { settlementArrival.Arrived(transporters, tile); return; }
            // Register the same native infos before calling third-party/native arrival code.
            // 执行第三方或原生抵达代码前，先将同一批原生 info 登记到可保存的安全持有者。
            bool blocked = CabinetArrivalTarget.BlocksFlight(receiver);
            CabinetRecoverySite recovery = CabinetRecoverySite.Create(blocked && origin.Valid ? origin : tile, transporters);
            try
            {
                using (new CabinetDeliveryScope(transporters))
                {
                    TransportersArrivalAction action = blocked ? null : Valid(intended, transporters, tile) ? intended : Fallback(transporters, tile);
                    action?.Arrived(transporters, tile);
                }
            }
            catch (Exception exception)
            {
                Log.Error("[F.A.F.] Cabinet arrival failed; undelivered native payload remains at the recovery point.\n" + exception);
            }
            finally { recovery.FinishDelivery(); }
        }
        private bool Valid(TransportersArrivalAction action, IEnumerable<IThingHolder> pods, PlanetTile tile)
        {
            if (!AutonomousDestinationValid(pods, tile)) return false;
            if (receiverRequired && receiver == null) return false;
            // Older saved wrappers did not store the receiver. Resolve their existing map target without changing the route.
            // 旧存包装没有保存接收对象；仅解析其原有地图目标，不重选路线。
            WorldObject target = receiver ?? (action is TransportersArrivalAction_FormCaravan ? null : Find.WorldObjects.MapParentAt(tile));
            if (CabinetArrivalTarget.Reason(target, tile) != null) return false;
            return action != null && !(action is TransportersArrivalAction_GiveGift) && action.StillValid(pods, tile);
        }
        private static bool AutonomousDestinationValid(IEnumerable<IThingHolder> pods, PlanetTile tile)
        {
            if (!pods.OfType<ActiveTransporterInfo>().Any(info => CabinetPayload.CabinetIn(info)?.GetComp<CompCabinetTransport>().AutonomousMission == true)) return true;
            return Find.Maps.Any(map => map.Tile == tile && map.Parent.Faction?.HostileTo(Faction.OfPlayer) != true);
        }
        private static TransportersArrivalAction Fallback(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            if (!AutonomousDestinationValid(pods, tile)) return null;
            foreach (Map map in Find.Maps)
                if (map.Tile == tile && CabinetArrivalTarget.Reason(map.Parent, tile) == null
                    && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, map.Parent))
                    return new TransportersArrivalAction_LandInSpecificCell(map.Parent, DropCellFinder.RandomDropSpot(map));
            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, tile))
                return new TransportersArrivalAction_FormCaravan();
            Caravan caravan = Find.WorldObjects.Caravans.FirstOrDefault(candidate => candidate.Tile == tile
                && CabinetArrivalTarget.Reason(candidate, tile) == null
                && TransportersArrivalAction_GiveToCaravan.CanGiveTo(pods, candidate));
            return caravan == null ? null : new TransportersArrivalAction_GiveToCaravan(caravan);
        }
    }

    // A synchronous purpose scope, not an inventory: the dictionary only remembers original native owners.
    // 同步用途作用域，不是库存；字典只记住实体原有的原生 owner。
    internal sealed class CabinetDeliveryScope : IDisposable
    {
        [ThreadStatic] internal static CabinetDeliveryScope Current;
        private readonly CabinetDeliveryScope previous;
        private readonly Dictionary<Thing, ActiveTransporterInfo> originals = new Dictionary<Thing, ActiveTransporterInfo>();
        private readonly HashSet<Thing> terminal = new HashSet<Thing>();
        internal void Register(Thing thing, ActiveTransporterInfo original) { if (!originals.ContainsKey(thing)) originals.Add(thing, original); }
        internal void MarkTerminal(Thing thing) { terminal.Add(thing); }
        internal CabinetDeliveryScope(IEnumerable<ActiveTransporterInfo> pods)
        {
            foreach (ActiveTransporterInfo pod in pods)
                foreach (Thing thing in pod.innerContainer) originals.Add(thing, pod);
            previous = Current;
            Current = this;
        }
        internal bool GiveTo(Caravan caravan, Thing thing)
        {
            if (thing == null || !originals.TryGetValue(thing, out ActiveTransporterInfo pod)) return false;
            // Native Form/Give actions remove before GiveThing. Restore that exact object before safe transfer.
            // 原生组队/交队动作会先移除再 GiveThing；安全转移前先将该实体放回真实原仓。
            if (!thing.Destroyed && !thing.Spawned && thing.holdingOwner == null)
                pod.innerContainer.TryAdd(thing, canMergeWithExistingStacks: false);
            if (!pod.innerContainer.Contains(thing)) return true;
            if (caravan == null || !caravan.Spawned || !caravan.IsPlayerControlled) return true;
            Pawn recipient = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thing, caravan.PawnsListForReading, null);
            if (recipient?.inventory != null)
                pod.innerContainer.TryTransferToContainer(thing, recipient.inventory.innerContainer, thing.stackCount,
                    canMergeWithExistingStacks: true);
            return true;
        }
        public void Dispose()
        {
            Current = previous;
            // An arrival exception may occur after Remove and before Add. Reclaim only genuinely unowned entities.
            // 抵达可在 Remove 与 Add 之间异常；只收回真正无主的实体，不夺回已经成功交付的对象。
            foreach (KeyValuePair<Thing, ActiveTransporterInfo> pair in originals)
                if (!terminal.Contains(pair.Key) && !pair.Key.Destroyed && !pair.Key.Spawned && pair.Key.holdingOwner == null)
                    if (!pair.Value.innerContainer.TryAdd(pair.Key, canMergeWithExistingStacks: false))
                        Log.Error("[F.A.F.] Native recovery holder refused its original entity: " + pair.Key);
        }
    }

    [HarmonyPatch(typeof(CaravanInventoryUtility), nameof(CaravanInventoryUtility.GiveThing))]
    internal static class CabinetArrivalInventoryTransfer
    {
        private static bool Prefix(Caravan caravan, Thing thing)
        {
            return CabinetDeliveryScope.Current?.GiveTo(caravan, thing) != true;
        }
    }
}
