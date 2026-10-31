// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    // Only this mission's original entities are rescued; ordinary installed assets are not globally insured.
    // 仅接管本任务原实体；不为普通已安装资产提供全局保险。
    internal static class CabinetMapRetention
    {
        internal static PlanetTile FindSurfaceAnchor(PlanetTile origin)
        {
            PlanetTile seed = Find.WorldGrid.Surface.GetClosestTile_NewTemp(origin);
            if (!seed.Valid) return PlanetTile.Invalid;
            Queue<PlanetTile> pending = new Queue<PlanetTile>(); HashSet<PlanetTile> visited = new HashSet<PlanetTile>();
            List<PlanetTile> neighbors = new List<PlanetTile>(); pending.Enqueue(seed); visited.Add(seed);
            while (pending.Count > 0)
            {
                PlanetTile tile = pending.Dequeue();
                if (tile.LayerDef.SurfaceTiles && tile.LayerDef.canFormCaravans && !Find.World.Impassable(tile)) return tile;
                Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                foreach (PlanetTile neighbor in neighbors) if (visited.Add(neighbor)) pending.Enqueue(neighbor);
            }
            return PlanetTile.Invalid;
        }
        internal static PlanetTile RecoveryTile(PlanetTile destination, IEnumerable<ActiveTransporterInfo> payloads)
        {
            if (destination.LayerDef.SurfaceTiles && destination.LayerDef.canFormCaravans && !Find.World.Impassable(destination)) return destination;
            foreach (ActiveTransporterInfo info in payloads)
            {
                PlanetTile anchor = CabinetPayload.CabinetIn(info)?.GetComp<CompCabinetTransport>().RecoveryAnchor ?? PlanetTile.Invalid;
                if (anchor.Valid && anchor.LayerDef.SurfaceTiles && !Find.World.Impassable(anchor)) return anchor;
            }
            return FindSurfaceAnchor(destination);
        }
        internal static bool Anchored(Building_FlyingFridgeCabinet cab)
        {
            return cab.Faction == Faction.OfPlayer && (cab.GetComp<CompCabinetTransport>().RemoteStationKeeping
                || CabinetOccupancy.Read(cab).ActualOccupants.Count > 0);
        }
        internal static bool MapHasAnchor(Map map)
        {
            foreach (Thing thing in LocalMissionThings(map))
            {
                Building_FlyingFridgeCabinet cab = thing.GetInnerIfMinified() as Building_FlyingFridgeCabinet;
                if (cab != null && Anchored(cab)) return true;
                ActiveTransporterInfo info = (thing as CabinetIncoming)?.Contents ?? (thing as CabinetLeaving)?.Contents
                    ?? (thing as CabinetActivePod)?.Contents ?? (thing as CabinetMapFlyer)?.Payload;
                if (info?.innerContainer.Any == true) return true;
            }
            return false;
        }
        internal static IEnumerable<Thing> LocalMissionThings(Map map)
        {
            foreach (Thing thing in map.listerThings.AllThings) yield return thing;
            // An established station can be picked up without silently relinquishing its saved station intent.
            // 已建立驻留的柜被拿起后仍保留明确驻留意图，不因进入角色库存而悄悄失去移图保护。
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.inventory != null)
                    foreach (Thing item in pawn.inventory.innerContainer)
                        if (item is MinifiedThing wrapper && wrapper.InnerThing is Building_FlyingFridgeCabinet) yield return item;
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried is MinifiedThing mini && mini.InnerThing is Building_FlyingFridgeCabinet) yield return carried;
            }
        }
        internal static void PreserveResident(Building_FlyingFridgeCabinet cab, PlanetTile sourceTile)
        {
            List<Pawn> sleepers = CabinetOccupancy.Read(cab).ActualOccupants.Where(p => p.Spawned).ToList();
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>(); bay.Transferring = true;
            ActiveTransporterInfo info = new ActiveTransporterInfo { sentTransporterDef = cab.def, leaveSlag = false };
            PlanetTile anchor = bay.RecoveryAnchor.Valid ? bay.RecoveryAnchor : FindSurfaceAnchor(sourceTile);
            CabinetRecoverySite recovery = CabinetRecoverySite.Create(anchor, new[] { info });
            try
            {
                // Establish a persistent owner before touching the remaining payload.
                // 在处理余下人货前先建立可保存的权威持有者。
                MinifiedThing wrapper = cab.ParentHolder as MinifiedThing;
                if (wrapper == null) wrapper = cab.MakeMinified();
                else if (wrapper.Spawned) wrapper.DeSpawn();
                bool accepted = wrapper?.holdingOwner == null ? wrapper != null && info.innerContainer.TryAdd(wrapper, false)
                    : wrapper.holdingOwner.TryTransferToContainer(wrapper, info.innerContainer, 1, false) == 1;
                if (!accepted) throw new System.InvalidOperationException("Emergency owner refused the original cabinet wrapper.");
                foreach (Thing thing in bay.innerContainer.ToList())
                    if (bay.innerContainer.TryTransferToContainer(thing, info.innerContainer, thing.stackCount, false) != thing.stackCount)
                        throw new System.InvalidOperationException("Emergency native owner refused a payload item; original container retained.");
                foreach (Pawn pawn in sleepers)
                {
                    bay.ResidentRestPawn = null;
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false); pawn.DeSpawn();
                    if (!info.innerContainer.TryAdd(pawn, false))
                    {
                        // Keep the original pawn in the original cabinet if a peer rejects the native direct-info path.
                        // 其他补丁拒绝直接Info时，将原pawn保留在原柜中，不留无主实体。
                        if (!bay.innerContainer.TryAdd(pawn, false)) throw new System.InvalidOperationException("Both emergency holders rejected the original pawn.");
                    }
                }
            }
            finally { bay.Transferring = false; recovery.FinishDelivery(); }
        }
    }
    [HarmonyPatch(typeof(SpaceMapParent), nameof(SpaceMapParent.ShouldRemoveMapNow))]
    internal static class CabinetSpaceMapAnchor
    {
        private static bool Prefix(SpaceMapParent __instance, ref bool alsoRemoveWorldObject, ref bool __result)
        {
            if (!__instance.HasMap) return true;
            if (!CabinetMapRetention.MapHasAnchor(__instance.Map)) return true;
            alsoRemoveWorldObject = false; __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))]
    internal static class CabinetBeforeMapRemoval
    {
        private static void Prefix(Map map)
        {
            // Run before MapDeiniter clears pawn/thing references, not after an unreliable Destroy callback.
            // 在MapDeiniter清理人/物引用前交接，不依赖未必执行的Destroy回调。
            foreach (Thing thing in CabinetMapRetention.LocalMissionThings(map).Distinct().ToList())
            {
                if (thing is CabinetMapFlyer flyer) { flyer.Destroy(); continue; }
                if (thing is CabinetActivePod pod && pod.Contents != null) { pod.Destroy(); continue; }
                ActiveTransporterInfo info = (thing as CabinetIncoming)?.Contents ?? (thing as CabinetLeaving)?.Contents;
                if (info != null)
                {
                    CabinetRecoverySite site = CabinetRecoverySite.Create(map.Tile, new[] { info });
                    if (thing is CabinetIncoming incoming) incoming.Contents = null;
                    if (thing is CabinetLeaving leaving) leaving.Contents = null;
                    info.parent = site; site.FinishDelivery();
                }
                if (thing.GetInnerIfMinified() is Building_FlyingFridgeCabinet cab && map.Tile.LayerDef.isSpace && CabinetMapRetention.Anchored(cab))
                    CabinetMapRetention.PreserveResident(cab, map.Tile);
            }
        }
    }
}
