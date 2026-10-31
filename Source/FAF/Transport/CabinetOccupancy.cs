// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Linq;
using System;
using HarmonyLib;
using NivarianSleepInFridges;
using FAF.Cabinet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    public enum CabinetTravelPhase { Invalid, Map, Packed, Caravan, Departing, MapFlight, InTransit, Landing, Recovery, Transferring }

    // A live view of native holders, never a second owner or a saved passenger list.
    // 原生持有关系的实时视图，不是第二持有者或保存的乘员列表。
    public sealed class CabinetExteriorContext
    {
        public Map Map { get; internal set; }
        public IntVec3 Cell { get; internal set; }
        public PlanetTile Tile { get; internal set; }
        public bool InSpace { get { return Tile.Valid && Tile.LayerDef.isSpace; } }
    }
    public sealed class CabinetOccupancySnapshot
    {
        public Building_FlyingFridgeCabinet Cabinet { get; internal set; }
        public IReadOnlyList<Pawn> ActualOccupants { get; internal set; }
        public IReadOnlyList<Pawn> PendingBoarders { get; internal set; }
        public IReadOnlyList<Corpse> AssociatedCorpses { get; internal set; }
        public bool HasOccupancyConflict { get; internal set; }
        public bool GiftCabinetWaiting { get { return (Payload?.parent as CabinetRecoverySite)?.IsGiftPickup == true; } }
        public bool GiftDeliveryFailed { get { return (Payload?.parent as CabinetRecoverySite)?.GiftDeliveryFailed == true; } }
        public Pawn BedSleeper { get; internal set; }
        public Pawn RestPreparationPawn { get; internal set; }
        public bool RestPreparationActive { get { return RestPreparationPawn != null; } }
        public IThingHolder ActualOwner { get; internal set; }
        public ActiveTransporterInfo Payload { get; internal set; }
        public CabinetTravelPhase Phase { get; internal set; }
        public CabinetExteriorContext Exterior { get; internal set; }
        public bool OverCapacity { get { return ActualOccupants.Count > 1; } }
        public bool AutonomyUnlocked { get { return Cabinet.UpgradeProfile.AutonomyUnlocked; } }
        public bool AutonomousMission { get { return Cabinet.GetComp<CompCabinetTransport>().AutonomousMission; } }
        public float MassCapacity { get { return CabinetTravelDef.Current.massCapacity; } }
        public float PayloadMass
        {
            get { return CabinetPayloadCapacity.PayloadMass(Cabinet); }
        }
    }
    public static class CabinetOccupancy
    {
        private static readonly Func<CompFridgeSleep, Building_FridgeBedProxy> Proxy =
            (Func<CompFridgeSleep, Building_FridgeBedProxy>)Delegate.CreateDelegate(
                typeof(Func<CompFridgeSleep, Building_FridgeBedProxy>), AccessTools.PropertyGetter(typeof(CompFridgeSleep), "BedProxy"));
        internal static Building_FridgeBedProxy ProxyFor(Building_FlyingFridgeCabinet cab)
        {
            CompFridgeSleep sleep = cab.GetComp<CompFridgeSleep>(); return sleep == null ? null : Proxy(sleep);
        }
        public static CabinetOccupancySnapshot Read(Building_FlyingFridgeCabinet cab)
        {
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            ActiveTransporterInfo info = ReadAssociatedPayload(cab, out ActiveTransporterInfo containerInfo);
            CabinetBiologicalPayload biology = CabinetBiologicalPayload.Read(CabinetBiologicalPayload.Roots(cab, info));
            List<Pawn> occupants = ReadActualOccupants(cab, biology, out Pawn bed);
            CabinetBiologicalPayload sleepers = CabinetBiologicalPayload.Read(occupants.Where(p => p.Spawned));
            biology.Living.UnionWith(sleepers.Living); biology.Corpses.UnionWith(sleepers.Corpses);
            biology.UncontainedDead.UnionWith(sleepers.UncontainedDead);
            Map map = cab.MapHeld;
            PlanetTile tile = map != null ? map.Tile : cab.Tile;
            CabinetTravelPhase phase = ReadPhase(cab, bay, containerInfo);
            tile = ReadExteriorTile(containerInfo, tile);
            return AssembleSnapshot(cab, bay, info, biology, occupants, bed, phase, map, tile);
        }
        private static ActiveTransporterInfo ReadAssociatedPayload(Building_FlyingFridgeCabinet cab, out ActiveTransporterInfo containerInfo)
        {
            containerInfo = (cab.ParentHolder as MinifiedThing)?.ParentHolder as ActiveTransporterInfo;
            // Physical carrier and unique cabinet association are different: ambiguous Infos retain native exterior/phase.
            // 实际承运容器与唯一柜体关联不同；歧义Info仍保留原生外界和阶段，不认领共享住客。
            return CabinetPayload.CabinetIn(containerInfo) == cab ? containerInfo : null;
        }
        private static List<Pawn> ReadActualOccupants(Building_FlyingFridgeCabinet cab, CabinetBiologicalPayload biology, out Pawn bed)
        {
            List<Pawn> occupants = biology.Living.ToList();
            bed = null;
            CompFridgeSleep sleep = cab.GetComp<CompFridgeSleep>();
            Building_FridgeBedProxy proxy = sleep == null ? null : Proxy(sleep);
            if (cab.Spawned && proxy?.Spawned == true)
            {
                List<Pawn> sleepers = proxy.CurOccupants.Where(p => p.CurrentBed() == proxy).ToList();
                bed = sleepers.FirstOrDefault(); occupants.AddRange(sleepers);
            }
            return occupants;
        }
        private static CabinetTravelPhase ReadPhase(Building_FlyingFridgeCabinet cab, CompCabinetTransport bay, ActiveTransporterInfo containerInfo)
        {
            // Keep precedence explicit: temporary transfer wins over map/holder context.
            // 显式保留优先级：临时交接先于地图或持有场景。
            if (cab.Destroyed) return CabinetTravelPhase.Invalid;
            if (bay.Transferring) return CabinetTravelPhase.Transferring;
            if (cab.Spawned) return CabinetTravelPhase.Map;
            if (containerInfo?.parent is CabinetRecoverySite) return CabinetTravelPhase.Recovery;
            if (containerInfo?.parent is TravellingTransporters) return CabinetTravelPhase.InTransit;
            if (containerInfo?.parent is CabinetActivePod) return CabinetTravelPhase.Landing;
            if (containerInfo?.parent is CabinetMapFlyer) return CabinetTravelPhase.MapFlight;
            if (containerInfo?.parent is FlyShipLeaving || (containerInfo?.parent as Thing)?.ParentHolder is CabinetLeaving)
                return CabinetTravelPhase.Departing;
            return cab.GetCaravan() != null ? CabinetTravelPhase.Caravan : CabinetTravelPhase.Packed;
        }
        private static PlanetTile ReadExteriorTile(ActiveTransporterInfo containerInfo, PlanetTile tile)
        {
            if (containerInfo?.parent is WorldObject world) tile = world.Tile;
            if (containerInfo?.parent is TravellingTransporters traveller)
            {
                PlanetTile origin = (traveller.arrivalAction as CabinetSafeArrival)?.Origin ?? PlanetTile.Invalid;
                tile = origin.Valid && origin.LayerDef.isSpace ? origin : traveller.destinationTile;
            }
            return tile;
        }
        private static CabinetOccupancySnapshot AssembleSnapshot(Building_FlyingFridgeCabinet cab, CompCabinetTransport bay,
            ActiveTransporterInfo info, CabinetBiologicalPayload biology, List<Pawn> occupants, Pawn bed, CabinetTravelPhase phase, Map map, PlanetTile tile)
        {
            return new CabinetOccupancySnapshot {
                Cabinet = cab, ActualOccupants = occupants.Where(p => !p.Dead).Distinct().ToList(), BedSleeper = bed,
                AssociatedCorpses = biology.Corpses.ToList(),
                HasOccupancyConflict = biology.Conflict,
                RestPreparationPawn = CabinetRestPreparation.IsValid(cab, bay.RestPreparationTarget) ? bay.RestPreparationTarget : null,
                PendingBoarders = ReadPendingBoarders(bay, occupants),
                ActualOwner = info != null ? (IThingHolder)info : bay, Payload = info, Phase = phase,
                Exterior = new CabinetExteriorContext { Map = map, Cell = cab.PositionHeld, Tile = tile }
            };
        }
        private static List<Pawn> ReadPendingBoarders(CompCabinetTransport bay, List<Pawn> occupants)
        {
            return bay.leftToLoad == null ? new List<Pawn>() : bay.leftToLoad
                .Where(t => t.CountToTransfer > 0).SelectMany(t => t.things).OfType<Pawn>()
                .Where(p => !p.Dead && !p.Destroyed && !occupants.Contains(p)).Distinct().ToList();
        }
        public static bool CanReadCabinet(Building_FlyingFridgeCabinet cab)
        {
            if (cab == null || cab.Destroyed || cab.Faction != Faction.OfPlayer) return false;
            if (cab.Spawned) return true;
            MinifiedThing mini = cab.ParentHolder as MinifiedThing;
            if (mini == null || mini.InnerThing != cab || mini.Destroyed) return false;
            if (mini.Spawned) return true;
            if (mini.ParentHolder is ActiveTransporterInfo info) return info.innerContainer.Contains(mini) && CanReadPayload(info);
            Pawn pawn = (mini.ParentHolder as Pawn_InventoryTracker)?.pawn ?? (mini.ParentHolder as Pawn_CarryTracker)?.pawn;
            return pawn != null && ((pawn.Spawned && pawn.Faction == Faction.OfPlayer) || pawn.GetCaravan()?.IsPlayerControlled == true);
        }
        public static bool CanReadPayload(ActiveTransporterInfo info)
        {
            if (info == null || info.parent == null) return false;
            IThingHolder owner = info.parent;
            if (owner is CabinetTravellingTransporters traveller)
            {
                List<IThingHolder> children = new List<IThingHolder>(); traveller.GetChildHolders(children);
                return traveller.Spawned && traveller.Faction == Faction.OfPlayer && children.Contains(info);
            }
            if (owner is CabinetRecoverySite recovery) return recovery.Spawned && recovery.Faction == Faction.OfPlayer && recovery.Owns(info);
            if (owner is CabinetActivePod pod) return CanReadLandingPayload(pod, info);
            if (owner is CabinetMapFlyer flyer) return flyer.Spawned && flyer.Payload == info
                && CabinetPayload.CabinetIn(info)?.Faction == Faction.OfPlayer;
            if (owner is CabinetLeaving leaving) return leaving.Spawned && leaving.Contents == info
                && CabinetPayload.CabinetIn(info)?.Faction == Faction.OfPlayer;
            if (owner is ActiveTransporter active && active.ParentHolder is CabinetLeaving departure)
                return departure.Spawned && departure.Contents == info && CabinetPayload.CabinetIn(info)?.Faction == Faction.OfPlayer;
            // Compatibility for already-saved original Core travellers belonging to this cabinet.
            // 兼容旧存中已在原生Core航程内的本柜；不把sentDef单独作为权限证明。
            if (owner is TravellingTransporters legacy && legacy.Faction == Faction.OfPlayer && legacy.Spawned)
            {
                List<IThingHolder> children = new List<IThingHolder>(); legacy.GetChildHolders(children);
                return children.Contains(info) && CabinetPayload.CabinetIn(info)?.Faction == Faction.OfPlayer;
            }
            return false;
        }
        private static bool CanReadLandingPayload(CabinetActivePod pod, ActiveTransporterInfo info)
        {
            if (pod.Destroyed || pod.Contents != info || !(pod.Spawned
                || pod.ParentHolder is CabinetIncoming incoming && incoming.Spawned && incoming.Contents == info)) return false;
            List<Building_FlyingFridgeCabinet> cabinets = info.innerContainer.Select(thing => thing.GetInnerIfMinified())
                .OfType<Building_FlyingFridgeCabinet>().ToList();
            if (cabinets.Any(cabinet => !cabinet.Destroyed))
                return cabinets.Count == 1 && !cabinets[0].Destroyed && cabinets[0].Faction == Faction.OfPlayer;
            // Native MakeDropPodAt selects this pod from sentTransporterDef even after the original hull is gone.
            // 原生MakeDropPodAt即使在原柜毁坏后，仍依sentTransporterDef选本落舱；此处只开放实际余物只读页。
            return cabinets.Count <= 1 && info.innerContainer.Any
                && info.sentTransporterDef?.thingClass == typeof(Building_FlyingFridgeCabinet)
                && info.sentTransporterDef.dropPodActive == pod.def;
        }
        public static bool TryResolve(Pawn pawn, out CabinetOccupancySnapshot snapshot)
        {
            Building_FlyingFridgeCabinet cab = (pawn.ParentHolder as CompCabinetTransport)?.Cabinet;
            if (cab == null && pawn.ParentHolder is ActiveTransporterInfo info)
                cab = CabinetPayload.CabinetIn(info);
            if (cab == null) cab = (pawn.CurrentBed() as Building_FridgeBedProxy)?.ParentFridge as Building_FlyingFridgeCabinet;
            snapshot = cab == null ? null : Read(cab);
            return snapshot != null && snapshot.ActualOccupants.Contains(pawn);
        }
        public static AcceptanceReport CanAdmit(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (cab == null || cab.Destroyed || pawn == null || pawn.Destroyed || pawn.Dead || cab.Faction != Faction.OfPlayer)
                return "FAF_InvalidBoardedPassenger".Translate();
            if (!cab.UpgradeProfile.WorldLaunchUnlocked) return "FAF_BoardingLocked".Translate();
            CabinetOccupancySnapshot view = Read(cab);
            if (view.AssociatedCorpses.Count > 0 || CabinetBiologicalPayload.PendingCorpses(cab.GetComp<CompCabinetTransport>())
                || CabinetBiologicalPayload.Read(new[] { pawn }).Corpses.Count > 0) return "FAF_LiveCorpseConflict".Translate();
            if (view.HasOccupancyConflict) return "FAF_NotEnoughSeats".Translate();
            if (view.ActualOccupants.Contains(pawn)) return true;
            if (view.ActualOccupants.Count != 0 || view.PendingBoarders.Any(p => p != pawn)
                || view.RestPreparationPawn != null && view.RestPreparationPawn != pawn) return "FAF_NotEnoughSeats".Translate();
            if (!cab.Spawned || pawn.MapHeld != cab.Map) return "FAF_NoSafeExit".Translate();
            if (!pawn.Spawned && !(pawn.ParentHolder is Pawn_CarryTracker carrier && carrier.pawn.Spawned)) return "FAF_NoSafeExit".Translate();
            if (CabinetManifest.HasNestedLivingPawn(new[] { pawn })) return "FAF_UnpackPassengers".Translate();
            if (CabinetPayloadCapacity.CountCanAdd(cab, pawn, 1) == 0) return "FAF_OverMass".Translate();
            return true;
        }
        public static AcceptanceReport CanExit(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (cab == null || pawn == null || !CanReadCabinet(cab)) return "FAF_NoSafeExit".Translate();
            CabinetOccupancySnapshot view = Read(cab);
            if (!view.ActualOccupants.Contains(pawn) || !cab.Spawned || view.Phase != CabinetTravelPhase.Map)
                return "FAF_NoSafeExit".Translate();
            if (view.BedSleeper != pawn && !cab.GetComp<CompCabinetTransport>().innerContainer.Contains(pawn))
                return "FAF_NoSafeExit".Translate();
            return ExitCell(cab, pawn, out _) ? AcceptanceReport.WasAccepted : "FAF_NoSafeExit".Translate();
        }
        private static bool ExitCell(Building_FlyingFridgeCabinet cab, Pawn pawn, out IntVec3 exit)
        {
            IntVec3 hazardous = IntVec3.Invalid;
            foreach (IntVec3 cell in cab.OccupiedRect().ExpandedBy(1).EdgeCells)
                if (cell.InBounds(cab.Map) && cell.Standable(cab.Map) && !cell.Fogged(cab.Map)
                    && cell.GetFirstPawn(cab.Map) == null)
                {
                    if (!Hazardous(pawn, cell, cab.Map)) { exit = cell; return true; }
                    if (!hazardous.IsValid) hazardous = cell;
                }
            exit = hazardous; return exit.IsValid;
        }
        private static bool Hazardous(Pawn pawn, IntVec3 cell, Map map)
        {
            return (pawn.HarmedByVacuum && cell.GetVacuum(map) >= 0.5f)
                || !pawn.SafeTemperatureRange().Includes(GenTemperature.GetTemperatureForCell(cell, map));
        }
        public static bool RequestEnter(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            AcceptanceReport report = CanAdmit(cab, pawn);
            if (!report) { CabinetMapJump.Reject(report.Reason); return false; }
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            if (bay.innerContainer.Contains(pawn)) return true;
            if (Read(cab).BedSleeper == pawn) return RequestEndRest(cab, pawn);
            if (bay.groupID < 0) TransporterUtility.InitiateLoading(new[] { bay });
            TransferableOneWay reservation = new TransferableOneWay(); reservation.things.Add(pawn);
            bay.AddToTheToLoadList(reservation, 1);
            Pawn actor = pawn;
            Job job = JobMaker.MakeJob(JobDefOf.EnterTransporter, cab);
            if (pawn.Downed || !pawn.IsFreeColonist && !pawn.IsColonyMech)
            {
                actor = cab.AvailableHaulerFor(pawn);
                job = JobMaker.MakeJob(JobDefOf.HaulToTransporter, pawn, cab); job.count = 1;
            }
            bool accepted = actor != null && actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            if (!accepted) CancelBoarding(cab, pawn);
            return accepted;
        }
        public static bool RequestExit(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            AcceptanceReport report = CanExit(cab, pawn);
            if (!report || !ExitCell(cab, pawn, out IntVec3 exit)) return false;
            if (Hazardous(pawn, exit, cab.Map))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("FAF_UnsafeExitConfirm".Translate(
                    GenTemperature.GetTemperatureForCell(exit, cab.Map).ToStringTemperature("F1"), exit.GetVacuum(cab.Map).ToString("P0")),
                    () => CommitExit(cab, pawn)));
                return true;
            }
            return CommitExit(cab, pawn);
        }
        private static bool CommitExit(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (!CanExit(cab, pawn) || !ExitCell(cab, pawn, out IntVec3 exit)) return false;
            if (Read(cab).BedSleeper == pawn)
            {
                cab.GetComp<CompCabinetTransport>().ResidentRestPawn = null;
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.Position = exit; pawn.Notify_Teleported(); return true;
            }
            return cab.GetComp<CompCabinetTransport>().innerContainer.TryDrop(pawn, exit, cab.Map, ThingPlaceMode.Direct, out _);
        }
        public static AcceptanceReport CanCancelBoarding(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            return cab.Spawned && Read(cab).PendingBoarders.Contains(pawn) ? AcceptanceReport.WasAccepted : "FAF_NoPendingBoarder".Translate();
        }
        public static bool CancelBoarding(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (!CanCancelBoarding(cab, pawn)) return false;
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            foreach (TransferableOneWay pending in bay.leftToLoad.ToList())
                if (pending.things.Contains(pawn)) { int count = pending.CountToTransfer; pending.things.Remove(pawn); pending.AdjustTo(Math.Max(0, count - 1)); }
            bay.leftToLoad.RemoveAll(t => t.CountToTransfer == 0);
            if (pawn.CurJob?.def == JobDefOf.EnterTransporter && pawn.CurJob.targetA.Thing == cab)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            foreach (Pawn hauler in cab.Map.mapPawns.AllPawnsSpawned)
                if (hauler.jobs?.curDriver is JobDriver_HaulToTransporter driver && driver.Container == cab
                    && (hauler.CurJob.targetA.Thing == pawn || hauler.carryTracker?.CarriedThing == pawn))
                    hauler.jobs.EndCurrentJob(JobCondition.InterruptForced);
            return true;
        }
        public static AcceptanceReport CanRest(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (cab == null || !cab.Spawned || pawn == null || !CabinetSleepTransit.IsEligible(pawn)) return "FAF_InvalidSleeper".Translate();
            if (CabinetPayloadCapacity.CountCanAdd(cab, pawn, 1) == 0) return "FAF_OverMass".Translate();
            CabinetOccupancySnapshot state = Read(cab);
            if (state.AssociatedCorpses.Count > 0 || CabinetBiologicalPayload.PendingCorpses(cab.GetComp<CompCabinetTransport>())
                || CabinetBiologicalPayload.Read(new[] { pawn }).Corpses.Count > 0) return "FAF_LiveCorpseConflict".Translate();
            if (state.HasOccupancyConflict || state.ActualOccupants.Any(p => p != pawn)
                || state.PendingBoarders.Any(p => p != pawn)
                || state.RestPreparationPawn != null && state.RestPreparationPawn != pawn) return "FAF_NotEnoughSeats".Translate();
            Building_FridgeBedProxy bed = ProxyFor(cab);
            return bed?.Spawned == true && (RestUtility.CanUseBedNow(bed, pawn, false)
                || bed.IsActive && cab.UpgradeProfile.HeatingUnlocked && pawn.HarmedByVacuum && cab.InternalVacuum >= 0.5f
                    && pawn.MapHeld == cab.Map && CabinetRestPreparation.BedAvailableAfterPreparation(cab, pawn))
                ? AcceptanceReport.WasAccepted : "FAF_BedUnavailable".Translate();
        }
        public static bool RequestRest(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (!CanRest(cab, pawn)) return false;
            Building_FridgeBedProxy bed = ProxyFor(cab);
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            if (!RestUtility.CanUseBedNow(bed, pawn, false))
            {
                bay.RestPreparationTarget = pawn;
                if (bay.innerContainer.Contains(pawn)) return true;
                bool accepted = pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("FAF_PrepareCabinetRest"), cab), JobTag.Misc);
                if (!accepted) CabinetRestPreparation.Clear(cab, pawn);
                return accepted;
            }
            if (bay.innerContainer.Contains(pawn))
            {
                if (!bay.innerContainer.TryDrop(pawn, bed.GetSleepingSlotPos(0), cab.Map, ThingPlaceMode.Direct, out _)) return false;
                try
                {
                    RestUtility.TuckIntoBed(bed, pawn, pawn, false);
                    if (pawn.CurJob?.targetA.Thing == bed) { bay.ResidentRestPawn = pawn; return true; }
                }
                finally
                {
                    if (pawn.CurJob?.targetA.Thing != bed && pawn.Spawned)
                    {
                        pawn.DeSpawn(); bay.Transferring = true;
                        try { if (!bay.innerContainer.TryAdd(pawn, false)) GenSpawn.Spawn(pawn, bed.Position, cab.Map); }
                        finally { bay.Transferring = false; }
                    }
                }
                return false;
            }
            if (!pawn.Spawned || pawn.Map != cab.Map) return false;
            return pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobTag.Misc);
        }
        public static AcceptanceReport CanCancelRestPreparation(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            return CanReadCabinet(cab) && pawn != null && Read(cab).RestPreparationPawn == pawn
                ? AcceptanceReport.WasAccepted : "FAF_BedUnavailable".Translate();
        }
        public static bool CancelRestPreparation(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (!CanCancelRestPreparation(cab, pawn)) return false;
            CabinetRestPreparation.Clear(cab, pawn);
            if (pawn.CurJob?.def == DefDatabase<JobDef>.GetNamed("FAF_PrepareCabinetRest") && pawn.CurJob.targetA.Thing == cab)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            return true;
        }
        public static AcceptanceReport CanEndRest(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (Read(cab).BedSleeper != pawn) return "FAF_BedUnavailable".Translate();
            return cab.UpgradeProfile.WorldLaunchUnlocked ? AcceptanceReport.WasAccepted : CanExit(cab, pawn);
        }
        public static bool RequestEndRest(Building_FlyingFridgeCabinet cab, Pawn pawn)
        {
            if (!CanEndRest(cab, pawn)) return false;
            if (!cab.UpgradeProfile.WorldLaunchUnlocked) return RequestExit(cab, pawn);
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            IntVec3 position = pawn.Position;
            bay.Transferring = true;
            try
            {
                bay.ResidentRestPawn = null;
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false); pawn.DeSpawn();
                if (bay.innerContainer.TryAdd(pawn, false)) return true;
                GenSpawn.Spawn(pawn, position, cab.Map); return false;
            }
            finally { bay.Transferring = false; }
        }
    }
}
