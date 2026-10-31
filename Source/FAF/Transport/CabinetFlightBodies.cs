// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    internal static class CabinetPayload
    {
        internal static Building_FlyingFridgeCabinet CabinetIn(ActiveTransporterInfo info)
        {
            if (info == null) return null;
            Building_FlyingFridgeCabinet found = null;
            foreach (MinifiedThing wrapper in info.innerContainer.OfType<MinifiedThing>())
                if (wrapper.InnerThing is Building_FlyingFridgeCabinet cab)
                { if (found != null) return null; found = cab; }
            return found;
        }
        internal static void Draw(ActiveTransporterInfo info, Vector3 location, bool singleCellAnchor = true, float angle = 0f, float lift = 0f)
        {
            Building_FlyingFridgeCabinet cab = CabinetIn(info);
            if (cab == null) return;
            location.y = AltitudeLayer.Building.AltitudeFor();
            if (singleCellAnchor) location += GenThing.TrueCenter(IntVec3.Zero, cab.Rotation, cab.def.size, 0f) - IntVec3.Zero.ToVector3Shifted();
            Matrix4x4? previous = Rendering.FlightDrawScope.ManeuverTransform;
            try
            {
                // Every layer, including real pawn render requests, shares one submission-time pose.
                // 每一层（包括真实角色的绘制请求）共用同一个提交时姿态。
                Rendering.FlightDrawScope.ManeuverTransform = Matrix4x4.Translate(location + new Vector3(0f, lift * 0.06f, lift))
                    * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up)) * Matrix4x4.Translate(-location);
                CabinetVisualRenderer.BodyFor(cab).Draw(location, cab.Rotation, cab);
                CabinetVisualRenderer.DrawContentsAndFrame(cab, location);
            }
            finally { Rendering.FlightDrawScope.ManeuverTransform = previous; }
        }
        internal static bool CanPlace(Building_FlyingFridgeCabinet cab, IntVec3 cell, Map map, Rot4 rotation, Thing ignore = null)
        {
            return PlacementReport(cab, cell, map, rotation, ignore);
        }
        internal static AcceptanceReport PlacementReport(Building_FlyingFridgeCabinet cab, IntVec3 cell, Map map, Rot4 rotation, Thing ignore = null)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, rotation, cab.def.size))
            {
                if (!c.InBounds(map)) return "OutOfBounds".Translate();
                if (c.Fogged(map)) return "CannotPlaceInUndiscovered".Translate();
                if (c.Roofed(map)) return "FAF_RoofBlocked".Translate();
                // Coexistence is allowed, but native Spawn must never wipe an existing entity to make room.
                // 允许真实共存，但原生Spawn不能为落柜而擦除任何现有实体。
                foreach (Thing thing in c.GetThingList(map))
                {
                    if (thing == ignore) continue;
                    if (thing.def.plant?.IsTree == true) return "SpaceAlreadyOccupied".Translate();
                    if (GenSpawn.SpawningWipes(cab.def, thing.def) && !CabinetLandingPlacement.CanPreserve(thing.def))
                        return "FAF_LandingWouldDestroy".Translate(thing.LabelCap);
                }
            }
            return GenConstruct.CanPlaceBlueprintAt(cab.def, cell, rotation, map, thingToIgnore: ignore,
                thing: cab, stuffDef: cab.Stuff);
        }
        internal static bool TryUnfold(ActiveTransporterInfo info, Map map, IntVec3 cell, Rot4 rotation, Thing ignore)
        {
            Building_FlyingFridgeCabinet cab = CabinetIn(info);
            if (cab == null || !CanPlace(cab, cell, map, rotation, ignore)) return false;
            MinifiedThing wrapper = (MinifiedThing)cab.ParentHolder;
            CompCabinetTransport bay = cab.GetComp<CompCabinetTransport>();
            List<Thing> moved = new List<Thing>();
            bay.Transferring = true;
            try
            {
                wrapper.GetDirectlyHeldThings().Remove(cab);
                try { CabinetLandingPlacement.Spawn(cab, cell, map, rotation); }
                catch { if (!cab.Spawned) wrapper.GetDirectlyHeldThings().TryAdd(cab); throw; }
                // Same objects, no opening into vacuum: people and cargo return to this cabinet's real bay.
                // 同一实体，不向真空开舱：人员和货物回到本柜真实运输舱。
                foreach (Thing thing in info.innerContainer.ToList())
                {
                    if (thing == wrapper) continue;
                    if (thing is Pawn pawn && pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
                    int count = thing.stackCount;
                    if (info.innerContainer.TryTransferToContainer(thing, bay.innerContainer, count, false) != count)
                        throw new InvalidOperationException("Native bay refused its returning payload.");
                    moved.Add(thing);
                }
                info.innerContainer.Remove(wrapper); wrapper.Destroy();
                bay.RemoteStationKeeping = map.Tile.LayerDef.isSpace;
                return true;
            }
            catch
            {
                foreach (Thing thing in moved)
                    if (bay.innerContainer.Contains(thing)) bay.innerContainer.TryTransferToContainer(thing, info.innerContainer, thing.stackCount, false);
                if (cab.Spawned) cab.DeSpawn();
                if (cab.ParentHolder != wrapper) wrapper.GetDirectlyHeldThings().TryAdd(cab, false);
                throw;
            }
            finally { bay.Transferring = false; }
        }
        internal static void Breach(ActiveTransporterInfo info, Map map, IntVec3 cell,
            Building_FlyingFridgeCabinet originalCabinet = null, MinifiedThing originalWrapper = null)
        {
            Building_FlyingFridgeCabinet cab = originalCabinet ?? CabinetIn(info);
            if (cab != null)
            {
                MinifiedThing wrapper = originalWrapper ?? cab.ParentHolder as MinifiedThing;
                if (!cab.Destroyed) cab.Destroy(DestroyMode.KillFinalize);
                if (wrapper != null) { wrapper.GetDirectlyHeldThings().Remove(cab); info.innerContainer.Remove(wrapper); wrapper.Destroy(); }
            }
            foreach (Thing thing in info.innerContainer.ToList())
            {
                if (info.innerContainer.TryDrop(thing, cell, map, ThingPlaceMode.Near, out _)) continue;
                if (thing is Pawn pawn && !pawn.Destroyed)
                {
                    // A hull breach is not a safe airlock. Even an unsafe floor must not become an immortal coffin.
                    // 破舱不是安全气闸；即使地面危险，也不能变成不受伤的棺材。
                    info.innerContainer.Remove(pawn);
                    try { GenSpawn.Spawn(pawn, cell, map); }
                    catch (Exception exception)
                    {
                        if (!pawn.Spawned) info.innerContainer.TryAdd(pawn, false);
                        Log.Error("[F.A.F.] Breached landing hull could not place a pawn; preserving unprotected local debris.\n" + exception);
                    }
                }
            }
            if (info.innerContainer.Any)
            {
                CabinetBreach debris = (CabinetBreach)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("FAF_CabinetBreach"));
                GenSpawn.Spawn(debris, cell, map); debris.AcceptRemainders(info.innerContainer);
            }
        }
    }
    public sealed class CabinetIncoming : DropPodIncoming
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            GetDrawPositionAndRotation(ref drawLoc, out _);
            CabinetPayload.Draw(Contents, drawLoc);
            DrawDropSpotShadow();
        }
    }
    // A real native Info holds the original minified cabinet throughout the short map flight.
    // 近场飞行始终以真实原生Info持有同一包裹柜，不以瞬移或新柜替换。
    public sealed class CabinetMapFlyer : Thing, IThingHolder
    {
        private ActiveTransporterInfo payload;
        private IntVec3 start, destination;
        private Rot4 landingRotation;
        private int elapsed, duration;
        private Vector3 startDraw, endDraw;
        private float initialAngle, initialLift, finalAngle;
        private bool blocked;
        public ActiveTransporterInfo Payload { get { return payload; } }
        internal void DetachPayload() { payload = null; }
        internal void Setup(ActiveTransporterInfo info, IntVec3 from, IntVec3 to, Rot4 rotation, Vector3 initialDraw, float angle, float lift)
        {
            payload = info; info.parent = this; start = from; destination = to; landingRotation = rotation;
            Building_FlyingFridgeCabinet cab = CabinetPayload.CabinetIn(info);
            startDraw = initialDraw; endDraw = GenThing.TrueCenter(to, rotation, cab.def.size, AltitudeLayer.Building.AltitudeFor());
            initialAngle = angle; initialLift = lift; finalAngle = Mathf.DeltaAngle(cab.Rotation.AsAngle, rotation.AsAngle);
            duration = Mathf.CeilToInt(Mathf.Max(def.pawnFlyer.flightDurationMin,
                from.DistanceTo(to) / def.pawnFlyer.flightSpeed) * GenTicks.TicksPerRealSecond);
        }
        protected override int MaxTickIntervalRate { get { return 1; } }
        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta); elapsed = Math.Min(duration, elapsed + delta);
            Vector3 ground = Vector3.Lerp(startDraw, endDraw, Progress);
            if (ground.ToIntVec3().InBounds(Map)) Position = ground.ToIntVec3();
            if (elapsed >= duration && !blocked) TryLand();
        }
        private void TryLand()
        {
            try
            {
                if (CabinetPayload.TryUnfold(payload, Map, destination, landingRotation, this))
                {
                    for (int i = 0; i < 6; i++) FleckMaker.ThrowDustPuff(destination, Map, 1.2f);
                    payload = null; Destroy(); return;
                }
            }
            catch (Exception exception) { Log.Error("[F.A.F.] Map landing failed; original payload retained for manual retry.\n" + exception); }
            blocked = true;
        }
        private float Progress { get { return def.pawnFlyer.Worker.AdjustedProgress(duration == 0 ? 0f : Mathf.Clamp01((float)elapsed / duration)); } }
        public override Vector3 DrawPos
        {
            get
            {
                float progress = Progress, height = def.pawnFlyer.Worker.GetHeight(progress);
                return Vector3.Lerp(startDraw, endDraw, progress) + Vector3.forward * (def.pawnFlyer.heightFactor * height);
            }
        }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float blend = Mathf.SmoothStep(0f, 1f, Progress);
            CabinetPayload.Draw(payload, drawLoc, false, Mathf.LerpAngle(initialAngle, finalAngle, blend), initialLift * (1f - blend));
        }
        public ThingOwner GetDirectlyHeldThings() { return null; }
        public void GetChildHolders(List<IThingHolder> outChildren) { if (payload != null) outChildren.Add(payload); }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref payload, "fafPayload");
            Scribe_Values.Look(ref start, "fafStart"); Scribe_Values.Look(ref destination, "fafDestination");
            Scribe_Values.Look(ref landingRotation, "fafLandingRotation");
            Scribe_Values.Look(ref elapsed, "fafElapsed"); Scribe_Values.Look(ref duration, "fafDuration", 60);
            Scribe_Values.Look(ref blocked, "fafLandingBlocked");
            Scribe_Values.Look(ref startDraw, "fafStartDraw"); Scribe_Values.Look(ref endDraw, "fafEndDraw");
            Scribe_Values.Look(ref initialAngle, "fafInitialAngle"); Scribe_Values.Look(ref initialLift, "fafInitialLift");
            Scribe_Values.Look(ref finalAngle, "fafFinalAngle");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && payload != null) payload.parent = this;
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (payload?.innerContainer.Any == true && MapHeld != null)
                CabinetRecoverySite.Create(MapHeld.Tile, new[] { payload }).FinishDelivery();
            payload = null; base.Destroy(mode);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (payload != null) yield return CabinetCargoUI.PayloadCommandFor(payload);
            if (blocked) yield return new Command_Action { defaultLabel = "FAF_RetryPodPlacement".Translate(), action = TryLand };
        }
    }
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetPosture))]
    internal static class CabinetDirectPayloadPosture
    {
        private static void Postfix(Pawn p, ref PawnPosture __result)
        {
            if (p.ParentHolder is ActiveTransporterInfo info && CabinetPayload.CabinetIn(info) != null && !p.Dead)
                __result = PawnPosture.LayingOnGroundFaceUp;
        }
    }
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
    internal static class CabinetDirectPayloadBodyAngle
    {
        private static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (!(___pawn.ParentHolder is ActiveTransporterInfo info)) return;
            Building_FlyingFridgeCabinet cab = CabinetPayload.CabinetIn(info);
            if (cab != null) __result = cab.GetComp<CompCabinetTransport>().HeldPawnBodyAngle;
        }
    }
    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    internal static class CabinetDirectPayloadAltitude
    {
        private static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (___pawn.ParentHolder is ActiveTransporterInfo info && CabinetPayload.CabinetIn(info) is Building_FlyingFridgeCabinet cab)
                __result.y = cab.GetComp<CompCabinetTransport>().HeldPawnDrawPos_Y;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class CabinetLandedHullDamage
    {
        private static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(__instance is CabinetActivePod pod) || pod.MapHeld == null) return true;
            Building_FlyingFridgeCabinet cab = CabinetPayload.CabinetIn(pod.Contents);
            if (cab == null) return true;
            MinifiedThing wrapper = cab.ParentHolder as MinifiedThing;
            __result = cab.TakeDamage(dinfo);
            if (cab.Destroyed)
            {
                CabinetPayload.Breach(pod.Contents, pod.MapHeld, pod.PositionHeld, cab, wrapper);
                pod.Destroy(DestroyMode.KillFinalize);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.HitPoints), MethodType.Getter)]
    internal static class CabinetLandingHullHealth
    {
        private static bool Prefix(Thing __instance, ref int __result)
        {
            if (__instance is CabinetActivePod pod && CabinetPayload.CabinetIn(pod.Contents) is Building_FlyingFridgeCabinet cab)
            { __result = cab.HitPoints; return false; }
            return true;
        }
    }
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    internal static class CabinetLandingHullMaximumHealth
    {
        private static void Postfix(StatRequest req, bool applyPostProcess, StatDef ___stat, ref float __result)
        {
            if (___stat == StatDefOf.MaxHitPoints && req.Thing is CabinetActivePod pod
                && CabinetPayload.CabinetIn(pod.Contents) is Building_FlyingFridgeCabinet cab)
                __result = cab.GetStatValue(StatDefOf.MaxHitPoints, applyPostProcess);
        }
    }
}
