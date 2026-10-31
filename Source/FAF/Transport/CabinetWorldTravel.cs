// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using FAF.Cabinet;
using FAF.Cabinet.Research;
using FAF.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    internal static class CabinetWorldTravel
    {
        internal static void ChooseFromMap(CompCabinetTransport lead)
        {
            if (MapPlan(lead, out string reason) == null) { CabinetMapJump.Reject(reason); return; }
            Choose(() => MapPlan(lead, out _), () => lead.parent.Tile,
                () => MapBays(lead)?.Cast<IThingHolder>().ToList(), (tile, action, target) => CommitMap(lead, tile, action, target));
        }
        internal static void ChooseFromCaravan(Caravan caravan)
        {
            if (CaravanPlan(caravan, out string reason) == null) { CabinetMapJump.Reject(reason); return; }
            Choose(() => CaravanPlan(caravan, out _), () => caravan.Tile,
                () => new List<IThingHolder> { caravan }, (tile, action, target) => CommitCaravan(caravan, tile, action, target));
        }
        private static List<CompCabinetTransport> MapBays(CompCabinetTransport lead)
        {
            if (lead == null || !lead.parent.Spawned) return null;
            if (lead.groupID < 0) return new List<CompCabinetTransport> { lead };
            List<CompTransporter> group = lead.TransportersInGroup(lead.parent.Map)?.ToList();
            if (group == null || group.Any(bay => !(bay is CompCabinetTransport))) return null;
            return group.Cast<CompCabinetTransport>().ToList();
        }
        internal static int MapCooldownRemainingTicks(CompCabinetTransport lead)
        {
            List<CompCabinetTransport> bays = MapBays(lead);
            return bays == null || bays.Count == 0 ? 0 : bays.Max(bay => bay.ManeuverCooldownRemainingTicks);
        }
        internal static int CaravanCooldownRemainingTicks(Caravan caravan)
        {
            if (caravan == null || !caravan.Spawned || !caravan.IsPlayerControlled) return 0;
            // Use the same participating cabinets as CaravanPlan, not every freezer in the world.
            // 只检查CaravanPlan实际参与的原柜，不扫描其他世界冷柜。
            return CabinetAccess.InInventory(caravan.PawnsListForReading).Distinct()
                .Select(cabinet => cabinet.GetComp<CompCabinetTransport>().ManeuverCooldownRemainingTicks).DefaultIfEmpty(0).Max();
        }
        private static CabinetManifest MapPlan(CompCabinetTransport lead, out string reason)
        {
            reason = "FAF_FinishLoading".Translate();
            List<CompCabinetTransport> bays = MapBays(lead);
            if (bays == null || bays.Count == 0) return null;
            List<Building_FlyingFridgeCabinet> cabinets = bays.Select(bay => bay.Cabinet).ToList();
            reason = CheckCabinets(cabinets, true);
            if (reason != null) return null;
            if (bays.Any(bay => bay.AnythingLeftToLoad) || cabinets.Any(cabinet => CabinetSleepTransit.Sleepers(cabinet).Count > 0))
            { reason = "FAF_FinishLoading".Translate(); return null; }
            List<Thing> payload = bays.SelectMany(bay => bay.innerContainer).ToList();
            if (!OwnerOrAutonomousPassengers(payload.OfType<Pawn>()))
            { reason = "FAF_NeedsCaravanOwner".Translate(); return null; }
            return CabinetManifest.Plan(cabinets, payload.OfType<Pawn>().ToList(), payload.Where(item => !(item is Pawn)).ToList(), false, out reason);
        }
        private static CabinetManifest CaravanPlan(Caravan caravan, out string reason)
        {
            reason = "FAF_InvalidCaravan".Translate();
            if (caravan == null || !caravan.Spawned || !caravan.IsPlayerControlled) return null;
            List<Building_FlyingFridgeCabinet> cabinets = CabinetAccess.InInventory(caravan.PawnsListForReading).Distinct().ToList();
            reason = CheckCabinets(cabinets, false);
            if (reason != null) return null;
            HashSet<Thing> wrappers = new HashSet<Thing>(cabinets.Select(cabinet => (Thing)cabinet.ParentHolder));
            List<Thing> cargo = caravan.PawnsListForReading.Where(pawn => pawn.inventory != null)
                .SelectMany(pawn => pawn.inventory.innerContainer).Where(item => !wrappers.Contains(item)).ToList();
            CabinetManifest plan = CabinetManifest.Plan(cabinets, caravan.PawnsListForReading.ToList(), cargo, true, out reason);
            if (plan != null && !OwnerOrAutonomousPassengers(plan.Passengers))
            { reason = "FAF_NeedsCaravanOwner".Translate(); return null; }
            return plan;
        }
        private static bool OwnerOrAutonomousPassengers(IEnumerable<Pawn> passengers)
        {
            List<Pawn> actual = passengers.ToList();
            return actual.Any(pawn => CaravanUtility.IsOwner(pawn, Faction.OfPlayer))
                || CabinetResearch.Current.AutonomyUnlocked && actual.All(pawn => pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer && !pawn.Dead && !pawn.Destroyed);
        }
        private static string CheckCabinets(List<Building_FlyingFridgeCabinet> cabinets, bool onMap)
        {
            if (cabinets.Count == 0) return "FAF_InvalidCabinet".Translate();
            foreach (Building_FlyingFridgeCabinet cabinet in cabinets)
            {
                if (!CabinetCruiseProgressPatch.Available || !CabinetWorldPawnTick.Available) return "FAF_CruiseUnavailable".Translate();
                if (!CabinetAccess.CanManage(cabinet) || !cabinet.UpgradeProfile.WorldLaunchUnlocked || cabinet.Spawned != onMap)
                    return "FAF_InvalidCabinet".Translate();
                if (CabinetPayloadCapacity.PayloadMass(cabinet) > CabinetPayloadCapacity.Limit) return "FAF_OverMass".Translate();
                CompCabinetTransport bay = cabinet.GetComp<CompCabinetTransport>();
                if (CabinetOccupancy.Read(cabinet).RestPreparationActive) return "FAF_FinishLoading".Translate();
                if (CabinetOccupancy.Read(cabinet).HasOccupancyConflict) return "FAF_LiveCorpseConflict".Translate();
                if (bay.CoolingDown) return "FAF_Cooldown".Translate();
                if (onMap && cabinet.OccupiedRect().Any(cell => cell.Roofed(cabinet.Map))) return "FAF_RoofBlocked".Translate();
            }
            return null;
        }
        private static float FuelCost(Building_FlyingFridgeCabinet cabinet, PlanetTile origin, PlanetTile destination)
        {
            CabinetTravelDef rule = CabinetTravelDef.Current;
            bool toSpace = destination.LayerDef.isSpace;
            float startup = toSpace && !origin.LayerDef.isSpace ? rule.orbitAscentBaseCoolant : rule.worldBaseCoolant;
            return cabinet.GetManeuverCoolantCost(ManeuverCost.BaseCost(Distance(origin, destination), startup,
                rule.worldCoolantPerTile * (toSpace ? rule.orbitDistanceFactor : 1f)));
        }
        private static int Distance(PlanetTile origin, PlanetTile destination)
        {
            if (origin.Layer != destination.Layer) origin = destination.Layer.GetClosestTile_NewTemp(origin);
            if (!origin.Valid) return int.MaxValue;
            return Find.WorldGrid.TraversalDistanceBetween(origin, destination, passImpassable: true,
                maxDist: int.MaxValue, canTraverseLayers: false);
        }
        private static string DestinationReason(PlanetTile origin, PlanetTile destination)
        {
            if (!origin.Valid || !destination.Valid || origin == destination
                || (!destination.LayerDef.SurfaceTiles && !(ModsConfig.OdysseyActive && destination.LayerDef.isSpace))
                || (destination.LayerDef.SurfaceTiles && Find.World.Impassable(destination))) return "FAF_InvalidDestination".Translate();
            int distance = Distance(origin, destination);
            int range = destination.LayerDef.isSpace ? CabinetTravelDef.Current.orbitRange : CabinetTravelDef.Current.worldRange;
            return distance < 0 || distance > range ? "FAF_OutOfRange".Translate().ToString() : null;
        }
        private static string ResourcesReason(CabinetManifest plan, PlanetTile origin, PlanetTile destination)
        {
            string invalid = DestinationReason(origin, destination);
            if (invalid != null) return invalid;
            if ((origin.LayerDef.isSpace || destination.LayerDef.isSpace) && plan.Cabinets.Any(c => !c.UpgradeProfile.HeatingUnlocked))
                return "FAF_AtmosphereRequired".Translate();
            return plan.Cabinets.Any(cabinet => cabinet.LaunchCoolantUnits + 0.000001f < FuelCost(cabinet, origin, destination))
                ? "FAF_NotEnoughCoolant".Translate().ToString() : null;
        }
        private static void Choose(Func<CabinetManifest> planGetter, Func<PlanetTile> originGetter,
            Func<List<IThingHolder>> holdersGetter, Action<PlanetTile, TransportersArrivalAction, WorldObject> launch)
        {
            CabinetManifest previewPlan = planGetter();
            if (previewPlan == null) { CabinetMapJump.Reject("FAF_LaunchNotReady".Translate()); return; }
            PlanetTile origin = originGetter();
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(origin)));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(target => {
                CabinetManifest plan = planGetter();
                if (plan == null) { CabinetMapJump.Reject("FAF_ManifestChanged".Translate()); return false; }
                List<IThingHolder> holders = holdersGetter();
                string reason = TargetReason(plan, originGetter(), target, holders);
                if (reason != null) { CabinetMapJump.Reject(reason); return false; }
                Action<PlanetTile, TransportersArrivalAction, WorldObject> confirm = (tile, action, receiver) => {
                    if (action is TransportersArrivalAction_GiveGift)
                    { CabinetMapJump.Reject("FAF_InvalidDestination".Translate()); return; }
                    float total = plan.Cabinets.Sum(cabinet => FuelCost(cabinet, originGetter(), tile));
                    StringBuilder preview = new StringBuilder("FAF_LaunchConfirm".Translate(plan.Passengers.Count, plan.Cabinets.Count, total.ToString("0.###")));
                    preview.Append("\n").Append("FAF_WorldFlightEstimate".Translate(
                        CabinetCruiseRoute.Quote(originGetter(), tile).DurationTicks.ToStringTicksToPeriod()));
                    for (int index = 0; index < plan.Cabinets.Count; index++)
                    {
                        Building_FlyingFridgeCabinet cabinet = plan.Cabinets[index];
                        Pawn passenger = plan.Pieces.Where(piece => piece.Slot == index).Select(piece => piece.Thing).OfType<Pawn>().FirstOrDefault();
                        preview.Append("\n\n").Append("FAF_UnitPreview".Translate(cabinet.thingIDNumber,
                            passenger == null ? "FAF_EmptyBerth".Translate().ToString() : passenger.LabelShortCap,
                            plan.PlannedMass(index).ToString("0.#"), CabinetTravelDef.Current.massCapacity,
                            FuelCost(cabinet, originGetter(), tile).ToString("0.###"), cabinet.LaunchCoolantUnits.ToString("0.###")));
                        string cargo = string.Join(", ", plan.Pieces.Where(piece => piece.Slot == index && !(piece.Thing is Pawn))
                            .Select(piece => piece.Thing.LabelNoCount + " ×" + piece.Count));
                        if (cargo.Length > 0) preview.Append("\n").Append("FAF_CargoPreview".Translate(cargo));
                    }
                    if (action is CabinetSettlementArrival mission && mission.Purpose == CabinetArrivalPurpose.GiftContentsAndLeaveCabinet)
                        preview.Append("\n\n").Append("FAF_GiftMissionPreview".Translate()).Append("\n").Append(SettlementCargoPreview(plan, false));
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(preview.ToString(), () => {
                        if (action is CabinetSettlementArrival hostile && hostile.Purpose == CabinetArrivalPurpose.RelinquishAtHostileSettlement)
                        {
                            CabinetManifest current = planGetter();
                            if (current == null) { CabinetMapJump.Reject("FAF_ManifestChanged".Translate()); return; }
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("FAF_RelinquishMissionConfirm".Translate(
                                SettlementCargoPreview(current, true)), () => launch(tile, action, receiver)));
                        }
                        else launch(tile, action, receiver);
                    }));
                };
                List<FloatMenuOption> options = ArrivalOptions(plan, holders, target.Tile, confirm);
                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }, canTargetTiles: true, mouseAttachment: ContentFinder<Texture2D>.Get("FAF/UI/Commands/LaunchCabinet"),
                originForClosest: origin, showCancelButton: true,
                onUpdate: () => {
                    PlanetTile projected = Find.WorldSelector.SelectedLayer.GetClosestTile_NewTemp(originGetter());
                    if (projected.Valid) GenDraw.DrawWorldRadiusRing(projected,
                        projected.LayerDef.isSpace ? CabinetTravelDef.Current.orbitRange : CabinetTravelDef.Current.worldRange,
                        CompPilotConsole.GetThrusterRadiusMat(projected));
                }, extraLabelGetter: target => target.IsValid ? Quote(planGetter(), originGetter(), target, holdersGetter()) : TaggedString.Empty);
        }
        private static List<FloatMenuOption> ArrivalOptions(CabinetManifest plan, List<IThingHolder> holders, PlanetTile tile,
            Action<PlanetTile, TransportersArrivalAction, WorldObject> confirm)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            bool hasOwner = plan.Passengers.Any(p => CaravanUtility.IsOwner(p, Faction.OfPlayer));
            MapParent receiver = Find.WorldObjects.MapParentAt(tile);
            if (!hasOwner)
            {
                if (CabinetResearch.Current.AutonomyUnlocked && receiver is Settlement autonomousSettlement && CabinetSettlementArrival.CanSelect(autonomousSettlement))
                {
                    CabinetSettlementArrival arrival = new CabinetSettlementArrival(autonomousSettlement);
                    options.Add(new FloatMenuOption((arrival.Purpose == CabinetArrivalPurpose.GiftContentsAndLeaveCabinet
                        ? "FAF_GiftSettlementOption" : "FAF_RelinquishSettlementOption").Translate(autonomousSettlement.LabelCap),
                        () => confirm(tile, arrival, autonomousSettlement)));
                    return options;
                }
                if (CabinetResearch.Current.AutonomyUnlocked && receiver?.HasMap == true && receiver.Faction?.HostileTo(Faction.OfPlayer) != true
                    && CabinetArrivalTarget.Reason(receiver, tile) == null
                    && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(holders, receiver))
                    options.Add(CabinetLandingSelection.Option(receiver, plan.Cabinets[0], (t, a) => confirm(t, a, receiver)));
                return options;
            }
            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(holders, tile)
                && !Find.WorldObjects.AnySettlementBaseAt(tile) && !Find.WorldObjects.AnySiteAt(tile))
                options.Add(new FloatMenuOption("FormCaravanHere".Translate(), () => confirm(tile, new TransportersArrivalAction_FormCaravan(), null)));
            foreach (WorldObject target in Find.WorldObjects.ObjectsAt(tile))
            {
                string rejection = CabinetArrivalTarget.Reason(target, tile);
                if (rejection != null)
                {
                    // Only objects that actually offer an arrival route contribute a disabled route option.
                    // 仅真正提供抵达路线的对象贡献禁用选项，不让同格无关航程阻断合法路线。
                    if (target is MapParent) options.Add(new FloatMenuOption(rejection, null));
                    continue;
                }
                Action<PlanetTile, TransportersArrivalAction> confirmTarget = (t, a) => confirm(t, a, target);
                if (target is MapParent parent && parent.HasMap && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(holders, parent))
                    options.Add(CabinetLandingSelection.Option(parent, plan.Cabinets[0], confirmTarget));
                if (target is Settlement settlement)
                {
                    // Keep native visit/trade/attack, but never offer the irreversible gift action.
                    // 保留原生访问/交易/攻击，绝不提供不可逆赠送柜体的选项。
                    options.AddRange(TransportersArrivalAction_VisitSettlement.GetFloatMenuOptions(confirmTarget, holders, settlement, false));
                    if (!settlement.HasMap) options.AddRange(TransportersArrivalAction_AttackSettlement.GetFloatMenuOptions(confirmTarget, holders, settlement));
                }
                else if (!(target is MapParent existing && existing.HasMap)) options.AddRange(target.GetTransportersFloatMenuOptions(holders, confirmTarget));
            }
            return options;
        }
        private static string TargetReason(CabinetManifest plan, PlanetTile origin, GlobalTargetInfo target, List<IThingHolder> holders)
        {
            if (plan == null || holders == null || !target.IsValid) return "FAF_InvalidDestination".Translate();
            string reason = ResourcesReason(plan, origin, target.Tile);
            if (reason != null) return reason;
            if (target.HasWorldObject && CabinetArrivalTarget.Reason(target.WorldObject, target.Tile) is string invalid) return invalid;
            List<FloatMenuOption> options = ArrivalOptions(plan, holders, target.Tile, (tile, action, receiver) => { });
            return options.Any(option => !option.Disabled) ? null : options.FirstOrDefault()?.Label ?? "FAF_InvalidDestination".Translate().ToString();
        }
        private static TaggedString Quote(CabinetManifest plan, PlanetTile origin, GlobalTargetInfo target, List<IThingHolder> holders)
        {
            PlanetTile destination = target.Tile;
            if (plan == null || !origin.Valid || !destination.Valid || plan.Cabinets.Any(c => !CabinetOccupancy.CanReadCabinet(c)))
                return "FAF_InvalidCabinet".Translate();
            CabinetTravelDef rule = CabinetTravelDef.Current;
            bool space = destination.LayerDef.isSpace;
            int range = space ? rule.orbitRange : rule.worldRange;
            float start = space && !origin.LayerDef.isSpace ? rule.orbitAscentBaseCoolant : rule.worldBaseCoolant;
            float slope = rule.worldCoolantPerTile * (space ? rule.orbitDistanceFactor : 1f);
            int soft = plan.Cabinets.Min(c => Mathf.Max(0, Mathf.FloorToInt((c.LaunchCoolantUnits - start + 0.000001f) / slope)));
            float total = plan.Cabinets.Sum(c => FuelCost(c, origin, destination));
            string label = "FAF_WorldQuoteBudget".Translate(range, soft, Distance(origin, destination),
                total.ToString("0.###"), (plan.Cabinets.Sum(c => c.LaunchCoolantUnits) - total).ToString("0.###"));
            label += "\n" + "FAF_WorldFlightEstimate".Translate(CabinetCruiseRoute.Quote(origin, destination).DurationTicks.ToStringTicksToPeriod());
            string reason = TargetReason(plan, origin, target, holders);
            return reason == null ? label : label + "\n" + "FAF_QuoteRejected".Translate(reason);
        }
        private static string SettlementCargoPreview(CabinetManifest plan, bool includeCabinets)
        {
            IEnumerable<Thing> goods = plan.Pieces.Select(piece => piece.Thing)
                .Concat(plan.Cabinets.Where(cab => !cab.HasMattress).SelectMany(cab => cab.Contents)).Distinct();
            if (includeCabinets) goods = goods.Concat(plan.Cabinets);
            return string.Join("\n", goods.Select(thing => thing.LabelCap.ToString()));
        }
        private static bool AutonomousArrivalAllowed(CabinetManifest plan, TransportersArrivalAction action, PlanetTile destination)
        {
            if (plan.Passengers.Any(p => CaravanUtility.IsOwner(p, Faction.OfPlayer))) return true;
            if (action is CabinetSettlementArrival settlementArrival)
                return CabinetResearch.Current.AutonomyUnlocked && OwnerOrAutonomousPassengers(plan.Passengers)
                    && !plan.Passengers.Any(p => p.RaceProps.Humanlike);
            MapParent receiver = Find.WorldObjects.MapParentAt(destination);
            return OwnerOrAutonomousPassengers(plan.Passengers) && (action is TransportersArrivalAction_LandInSpecificCell || action is CabinetMapArrival)
                && receiver?.HasMap == true && receiver.Faction?.HostileTo(Faction.OfPlayer) != true;
        }
        private static bool ValidateCommit(CabinetManifest plan, string reason, PlanetTile origin, PlanetTile destination,
            TransportersArrivalAction action, IEnumerable<IThingHolder> holders, WorldObject target)
        {
            if (plan == null) { CabinetMapJump.Reject(reason ?? "FAF_ManifestChanged".Translate().ToString()); return false; }
            reason = ResourcesReason(plan, origin, destination);
            if (reason != null) { CabinetMapJump.Reject(reason); return false; }
            if (action == null || action is TransportersArrivalAction_GiveGift || !action.StillValid(holders, destination)
                || (!(action is TransportersArrivalAction_FormCaravan) && target == null)
                || CabinetArrivalTarget.Reason(target, destination) != null
                || !AutonomousArrivalAllowed(plan, action, destination))
            { CabinetMapJump.Reject("FAF_InvalidDestination".Translate()); return false; }
            return true;
        }
        private static void MovePlanned(CabinetManifest plan, CabinetTransferTransaction transaction)
        {
            foreach (CabinetManifest.Piece piece in plan.Pieces)
                transaction.Transfer(piece.Thing, piece.Origin, plan.Pods[piece.Slot].innerContainer, piece.Count);
        }
        private static void ValidateActual(CabinetManifest plan, TransportersArrivalAction action, PlanetTile destination, WorldObject target)
        {
            if (!OwnerOrAutonomousPassengers(plan.Pods.SelectMany(pod => pod.innerContainer).OfType<Pawn>()))
                throw new InvalidOperationException("Native payload no longer has a valid player caravan owner.");
            for (int slot = 0; slot < plan.Pods.Count; slot++)
                if (CabinetBiologicalPayload.Read(plan.Pods[slot].innerContainer).Conflict
                    || CabinetPayloadCapacity.PayloadMass(plan.Cabinets[slot]) > CabinetPayloadCapacity.Limit + 0.0001f)
                    throw new InvalidOperationException("Actual native payload exceeds one berth or the configured mass capacity.");
            if (!action.StillValid(plan.Pods, destination)) throw new InvalidOperationException("Arrival ceased to be valid during commit.");
            if (CabinetArrivalTarget.Reason(target, destination) != null) throw new InvalidOperationException("Arrival receiver ceased to permit this flight during commit.");
            if (!AutonomousArrivalAllowed(plan, action, destination)) throw new InvalidOperationException("Autonomous receiving map ceased to be valid.");
        }
        private static void Consume(CabinetManifest plan, PlanetTile origin, PlanetTile destination, List<float> consumed)
        {
            foreach (Building_FlyingFridgeCabinet cabinet in plan.Cabinets)
            {
                float amount = FuelCost(cabinet, origin, destination);
                if (!cabinet.TryConsumeLaunchCoolant(amount)) throw new InvalidOperationException("Shared coolant changed during launch commit.");
                consumed.Add(amount);
            }
        }
        private static void Refund(CabinetManifest plan, List<float> consumed)
        {
            for (int index = 0; index < consumed.Count; index++) plan.Cabinets[index].RefundLaunchCoolant(consumed[index]);
        }
        private static void CommitMap(CompCabinetTransport lead, PlanetTile destination, TransportersArrivalAction action, WorldObject target)
        {
            CabinetManifest plan = MapPlan(lead, out string reason);
            Map map = lead.parent.Map;
            if (map == null || !ValidateCommit(plan, reason, map.Tile, destination, action, MapBays(lead), target)) return;
            List<CompCabinetTransport> bays = plan.Cabinets.Select(cabinet => cabinet.GetComp<CompCabinetTransport>()).ToList();
            List<IntVec3> positions = plan.Cabinets.Select(cabinet => cabinet.Position).ToList();
            List<Rot4> rotations = plan.Cabinets.Select(cabinet => cabinet.Rotation).ToList();
            List<MinifiedThing> packed = new List<MinifiedThing>();
            List<FlyShipLeaving> flights = new List<FlyShipLeaving>();
            List<float> consumed = new List<float>();
            CabinetTransferTransaction transaction = new CabinetTransferTransaction();
            try
            {
                int missionGroup = lead.groupID >= 0 ? lead.groupID : Find.UniqueIDsManager.GetNextTransporterGroupID();
                PlanetTile recoveryAnchor = CabinetMapRetention.FindSurfaceAnchor(map.Tile);
                if (!recoveryAnchor.Valid) throw new InvalidOperationException("No legal surface recovery anchor exists.");
                foreach (CompCabinetTransport bay in bays) { bay.Transferring = true; bay.RecoveryAnchor = recoveryAnchor; bay.TryRemoveLord(map); }
                MovePlanned(plan, transaction);
                for (int index = 0; index < bays.Count; index++)
                {
                    MinifiedThing wrapper = plan.Cabinets[index].MakeMinified();
                    if (wrapper == null) throw new InvalidOperationException("Native minification refused the cabinet.");
                    packed.Add(wrapper);
                    if (!plan.Pods[index].innerContainer.TryAdd(wrapper, canMergeWithExistingStacks: false))
                        throw new InvalidOperationException("Native payload refused its original cabinet.");
                    ActiveTransporter active = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
                    active.Contents = plan.Pods[index];
                    FlyShipLeaving flight = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(DefDatabase<ThingDef>.GetNamed("FAF_CabinetLeaving"), active);
                    // Native FlyShipLeaving assigns the travelling world object, not its ethereal shell, to the player.
                    // 原生FlyShipLeaving将世界航程归玩家所有，不给临时离图载体设置派系。
                    CompFridgeFlight pose = plan.Cabinets[index].GetComp<CompFridgeFlight>();
                    ((CabinetLeaving)flight).ConfigurePose(pose?.CurrentVisualAngle ?? 0f, pose?.CurrentVisualLift ?? 0f);
                    ((CabinetLeaving)flight).ConfigureCruise(map.Tile, destination);
                    flight.Rotation = rotations[index];
                    flight.groupID = missionGroup; flight.destinationTile = destination;
                    flight.arrivalAction = new CabinetSafeArrival(action, map.Tile, target);
                    flight.worldObjectDef = DefDatabase<WorldObjectDef>.GetNamed("FAF_CabinetTravelling");
                    flights.Add(flight);
                }
                ValidateActual(plan, action, destination, target);
                Consume(plan, map.Tile, destination, consumed);
                for (int index = 0; index < flights.Count; index++) GenSpawn.Spawn(flights[index], positions[index], map, rotations[index]);
                foreach (CompCabinetTransport bay in bays)
                {
                    bay.LastManeuverTick = Find.TickManager.TicksGame; bay.groupID = -1; bay.leftToLoad?.Clear();
                    bay.AutonomousMission = !plan.Passengers.Any(p => CaravanUtility.IsOwner(p, Faction.OfPlayer));
                }
                CameraJumper.TryHideWorld();
            }
            catch (Exception exception)
            {
                bool restored = transaction.Rollback(); Refund(plan, consumed);
                if (!restored)
                {
                    CabinetRecoverySite site = CabinetRecoverySite.Create(map.Tile, plan.Pods);
                    foreach (FlyShipLeaving flight in flights) { flight.Contents = null; flight.Destroy(); }
                    site.FinishDelivery();
                    Log.Error("[F.A.F.] Map launch rollback was refused; undelivered original payload retained for recovery.\n" + exception);
                    CabinetMapJump.Reject("FAF_CommitFailed".Translate()); return;
                }
                for (int index = 0; index < packed.Count; index++)
                {
                    MinifiedThing wrapper = packed[index];
                    wrapper.holdingOwner?.Remove(wrapper);
                    wrapper.GetDirectlyHeldThings().Remove(plan.Cabinets[index]);
                    wrapper.Destroy();
                }
                foreach (FlyShipLeaving flight in flights) flight.Destroy();
                for (int index = 0; index < plan.Cabinets.Count; index++)
                    if (!plan.Cabinets[index].Spawned) GenSpawn.Spawn(plan.Cabinets[index], positions[index], map, rotations[index]);
                Log.Error("[F.A.F.] Map launch failed; original entities and coolant restored.\n" + exception);
                CabinetMapJump.Reject("FAF_CommitFailed".Translate());
            }
            finally { foreach (CompCabinetTransport bay in bays) bay.Transferring = false; }
        }
        private static void CommitCaravan(Caravan caravan, PlanetTile destination, TransportersArrivalAction action, WorldObject target)
        {
            CabinetManifest plan = CaravanPlan(caravan, out string reason);
            if (!ValidateCommit(plan, reason, caravan.Tile, destination, action, new[] { caravan }, target)) return;
            CabinetTransferTransaction transaction = new CabinetTransferTransaction();
            List<float> consumed = new List<float>();
            TravellingTransporters travelling = null;
            bool committed = false;
            try
            {
                for (int index = 0; index < plan.Cabinets.Count; index++)
                {
                    MinifiedThing wrapper = (MinifiedThing)plan.Cabinets[index].ParentHolder;
                    transaction.Transfer(wrapper, wrapper.holdingOwner, plan.Pods[index].innerContainer, 1);
                }
                MovePlanned(plan, transaction);
                ValidateActual(plan, action, destination, target);
                Consume(plan, caravan.Tile, destination, consumed);
                foreach (Building_FlyingFridgeCabinet cab in plan.Cabinets)
                    cab.GetComp<CompCabinetTransport>().RecoveryAnchor = CabinetMapRetention.FindSurfaceAnchor(caravan.Tile);
                travelling = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("FAF_CabinetTravelling"));
                ((CabinetTravellingTransporters)travelling).ConfigureCruise(caravan.Tile, destination);
                travelling.SetFaction(Faction.OfPlayer); travelling.Tile = destination.Layer.GetClosestTile_NewTemp(caravan.Tile);
                travelling.destinationTile = destination; travelling.arrivalAction = new CabinetSafeArrival(action, caravan.Tile, target);
                Find.WorldObjects.Add(travelling);
                foreach (ActiveTransporterInfo pod in plan.Pods) travelling.AddTransporter(pod, justLeftTheMap: false);
                foreach (Building_FlyingFridgeCabinet cabinet in plan.Cabinets)
                {
                    cabinet.GetComp<CompCabinetTransport>().LastManeuverTick = Find.TickManager.TicksGame;
                    cabinet.GetComp<CompCabinetTransport>().AutonomousMission = !plan.Passengers.Any(p => CaravanUtility.IsOwner(p, Faction.OfPlayer));
                }
                committed = true;
            }
            catch (Exception exception)
            {
                bool restored = transaction.Rollback(); Refund(plan, consumed);
                if (!restored) CabinetRecoverySite.Create(caravan.Tile, plan.Pods).FinishDelivery();
                if (travelling != null && !travelling.Destroyed) travelling.Destroy();
                caravan.RecacheInventory();
                Log.Error("[F.A.F.] Caravan launch failed; original owners and coolant restored.\n" + exception);
                CabinetMapJump.Reject("FAF_CommitFailed".Translate());
            }
            if (committed)
            {
                // UI/final party removal happens after ownership commit, outside the rollback boundary.
                // UI与移除空队伍位于所有权提交之后，不属于实体回滚边界。
                caravan.Destroy();
                Find.WorldSelector.Select(travelling);
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    internal static class CaravanCabinetLaunchGizmo
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (!__instance.IsPlayerControlled || !CabinetResearch.Current.WorldLaunchUnlocked) yield break;
            if (!CabinetAccess.InInventory(__instance.PawnsListForReading).Any()) yield break;
            Command_Action launch = CabinetCooldownCommand.Create(
                () => CabinetWorldTravel.CaravanCooldownRemainingTicks(__instance), () => CabinetTravelDef.Current.cooldownTicks,
                "FAF_LaunchCaravan".Translate(), "FAF_LaunchCaravanDesc".Translate(),
                ContentFinder<Texture2D>.Get("FAF/UI/Commands/LaunchCabinet"), () => CabinetWorldTravel.ChooseFromCaravan(__instance));
            launch.groupable = false;
            if (CabinetWorldTravel.CaravanCooldownRemainingTicks(__instance) > 0) launch.Disable("FAF_Cooldown".Translate());
            yield return launch;
        }
    }
}
