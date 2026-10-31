// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Transport
{
    // A stationary, persistent crash/recovery point. Native infos remain the only cargo owners.
    // 静止且可保存的迫降回收点；货物仍只由原来的原生 info 持有。
    public sealed class CabinetRecoverySite : WorldObject, IThingHolder
    {
        private List<ActiveTransporterInfo> pods = new List<ActiveTransporterInfo>();
        private bool notified;
        private bool giftPickup, giftDeliveryFailed;
        public bool IsGiftPickup { get { return giftPickup; } }
        public bool GiftDeliveryFailed { get { return giftDeliveryFailed; } }
        internal void SetGiftOutcome(bool failed) { giftPickup = true; giftDeliveryFailed = failed; }
        internal void RetainAdditional(ActiveTransporterInfo info) { if (!pods.Contains(info)) pods.Add(info); Retain(info); }
        public bool Owns(ActiveTransporterInfo info) { return info != null && info.parent == this && pods.Contains(info); }
        internal static CabinetRecoverySite Create(PlanetTile tile, IEnumerable<ActiveTransporterInfo> infos)
        {
            CabinetRecoverySite site = (CabinetRecoverySite)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("FAF_CabinetRecoverySite"));
            List<ActiveTransporterInfo> actual = infos.ToList();
            site.Tile = CabinetMapRetention.RecoveryTile(tile, actual);
            site.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(site);
            site.pods.AddRange(actual);
            foreach (ActiveTransporterInfo info in site.pods) site.Retain(info);
            return site;
        }
        internal bool Contains(MinifiedThing wrapper)
        {
            return Spawned && !Destroyed && pods.Any(info => info.parent == this && info.innerContainer.Contains(wrapper));
        }
        private void Retain(ActiveTransporterInfo info)
        {
            info.parent = this;
            info.savePawnsWithReferenceMode = true;
            foreach (Pawn pawn in info.innerContainer.OfType<Pawn>())
                if (!pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(pawn);
        }
        internal void FinishDelivery()
        {
            for (int index = pods.Count - 1; index >= 0; index--)
            {
                ActiveTransporterInfo info = pods[index];
                ActiveTransporter active = info.parent as ActiveTransporter;
                bool onMap = active != null && !active.Destroyed && active.MapHeld != null;
                bool otherRecovery = info.parent is CabinetRecoverySite site && site != this;
                if (onMap || otherRecovery || !info.innerContainer.Any)
                {
                    if (onMap) info.savePawnsWithReferenceMode = false;
                    pods.RemoveAt(index);
                }
                else Retain(info);
            }
            if (pods.Count == 0) { Destroy(); return; }
            if (!notified)
            {
                notified = true;
                Messages.Message((giftPickup ? giftDeliveryFailed ? "FAF_GiftDeliveryPartial" : "FAF_GiftPickupCreated" : "FAF_RecoveryCreated").Translate(),
                    this, giftPickup && !giftDeliveryFailed ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.NegativeEvent);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pods, "cabinetPayloads", LookMode.Deep);
            Scribe_Values.Look(ref notified, "recoveryNotified", false);
            Scribe_Values.Look(ref giftPickup, "fafGiftPickup", false);
            Scribe_Values.Look(ref giftDeliveryFailed, "fafGiftDeliveryFailed", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (ActiveTransporterInfo info in pods) info.parent = this;
        }
        public ThingOwner GetDirectlyHeldThings() { return null; }
        public void GetChildHolders(List<IThingHolder> outChildren) { outChildren.AddRange(pods); }
        public override string GetInspectString()
        {
            return (giftPickup ? "FAF_GiftPickupInspect".Translate().ToString() : base.GetInspectString()) + "\n" + "FAF_RecoveryContents".Translate(
                pods.Sum(info => info.innerContainer.Count), string.Join(", ", pods.SelectMany(info => info.innerContainer).Select(thing => thing.LabelCap)));
        }
        internal bool CanRecover(Caravan caravan)
        {
            return Spawned && !Destroyed && caravan != null && caravan.Spawned && caravan.IsPlayerControlled
                && caravan.PawnsListForReading.Any(pawn => CaravanUtility.IsOwner(pawn, Faction.OfPlayer));
        }
        internal void Recover(Caravan caravan)
        {
            if (!CanRecover(caravan) || caravan.Tile != Tile) return;
            try
            {
                // The same scoped safe transfer serves ordinary arrivals and later caravan recovery.
                // 正常抵达与随后远行队回收共用同一个安全转移入口。
                using (new CabinetDeliveryScope(pods))
                    new TransportersArrivalAction_GiveToCaravan(caravan).Arrived(pods.ToList(), Tile);
            }
            catch (Exception exception)
            {
                Log.Error("[F.A.F.] Recovery transfer failed; remaining native payload stays at its recovery point.\n" + exception);
            }
            finally
            {
                caravan.RecacheInventory();
                FinishDelivery();
            }
            if (!Destroyed) Messages.Message("FAF_RecoveryPartial".Translate(), this, MessageTypeDefOf.RejectInput);
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(caravan)) yield return option;
            foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanRecover(caravan), () => new CaravanArrivalAction_RecoverCabinet(this),
                "FAF_RecoverCabinets".Translate(), caravan, Tile, this)) yield return option;
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            foreach (ActiveTransporterInfo info in pods) yield return CabinetCargoUI.PayloadCommandFor(info);
            foreach (MinifiedThing wrapper in pods.SelectMany(info => info.innerContainer).OfType<MinifiedThing>())
            {
                Building_FlyingFridgeCabinet cabinet = wrapper.InnerThing as Building_FlyingFridgeCabinet;
                if (cabinet == null) continue;
                Command_Action control = new Command_Action {
                    defaultLabel = cabinet.LabelCap + " #" + cabinet.thingIDNumber,
                    defaultDesc = "FAF_RecoveryCabinetDesc".Translate(), icon = CabinetVisualRenderer.IconFor(cabinet),
                    action = () => Window_CabinetThermostat.OpenFor(cabinet)
                };
                CabinetCommandIcons.ApplyCabinet(control, cabinet); yield return control;
            }
        }
    }

    public sealed class CaravanArrivalAction_RecoverCabinet : CaravanArrivalAction
    {
        private CabinetRecoverySite site;
        public CaravanArrivalAction_RecoverCabinet() { }
        internal CaravanArrivalAction_RecoverCabinet(CabinetRecoverySite site) { this.site = site; }
        public override string Label { get { return "FAF_RecoverCabinets".Translate(); } }
        public override string ReportString { get { return "FAF_ApproachingRecovery".Translate(); } }
        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            return base.StillValid(caravan, destinationTile) && site?.CanRecover(caravan) == true && site.Tile == destinationTile;
        }
        public override void Arrived(Caravan caravan) { site?.Recover(caravan); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "recoverySite");
        }
    }
}
