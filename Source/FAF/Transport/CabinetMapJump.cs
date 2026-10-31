// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NivarianSleepInFridges;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    internal static class CabinetSleepTransit
    {
        private static readonly Func<Pawn, bool> Eligible = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>),
            AccessTools.Method(typeof(CompFridgeSleep).Assembly.GetType("NivarianSleepInFridges.FridgeSleepUtility", true), "IsEligibleSleeper"));
        internal static List<Pawn> Sleepers(Building_FlyingFridgeCabinet cabinet)
        {
            Pawn sleeper = CabinetOccupancy.Read(cabinet).BedSleeper;
            return sleeper == null ? new List<Pawn>() : new List<Pawn> { sleeper };
        }
        internal static bool IsEligible(Pawn pawn) { return Eligible(pawn); }
    }

    internal static class CabinetMapJump
    {
        internal static void Choose(Building_FlyingFridgeCabinet cabinet)
        {
            Rot4 rotation = cabinet.Rotation;
            TargetingParameters parameters = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false };
            Find.Targeter.BeginTargeting(parameters, target => {
                string reason = Validate(cabinet, target.Cell, rotation);
                if (reason != null) { Reject(reason); return; }
                Commit(cabinet, target.Cell, rotation);
            }, target => {
                GenDraw.DrawRadiusRing(cabinet.Position, CabinetTravelDef.Current.mapRange);
                if (!target.IsValid || !target.Cell.InBounds(cabinet.Map)) return;
                Color color = Validate(cabinet, target.Cell, rotation) == null
                    ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor;
                // The native building ghost preserves the current retrofit's directional silhouette.
                // 原生建筑虚影保留当前改装阶段的四向轮廓；仅用选点朝向，不改动原柜朝向。
                GhostDrawer.DrawGhostThing(target.Cell, rotation, cabinet.def, cabinet.Graphic, color,
                    AltitudeLayer.Blueprint, cabinet, drawPlaceWorkers: true, stuff: cabinet.Stuff);
                GenDraw.DrawFieldEdges(GenAdj.OccupiedRect(target.Cell, rotation, cabinet.def.size).ToList(), color);
            }, target => Validate(cabinet, target.Cell, rotation) == null,
            onGuiAction: target => {
                if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent) { rotation.Rotate(RotationDirection.Clockwise); Event.current.Use(); }
                if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent) { rotation.Rotate(RotationDirection.Counterclockwise); Event.current.Use(); }
                CabinetTravelDef rule = CabinetTravelDef.Current;
                float cost = Cost(cabinet, target.Cell);
                string label = "FAF_MapQuoteBudget".Translate(rule.mapRange,
                    Mathf.Max(0f, (cabinet.LaunchCoolantUnits - rule.mapBaseCoolant) / rule.mapCoolantPerCell).ToString("0.#"),
                    cabinet.Position.DistanceTo(target.Cell).ToString("0.#"), cost.ToString("0.###"),
                    (cabinet.LaunchCoolantUnits - cost).ToString("0.###"));
                string reason = Validate(cabinet, target.Cell, rotation);
                if (reason != null) label += "\n" + "FAF_QuoteRejected".Translate(reason);
                GenUI.DrawMouseAttachment(null, label);
            });
        }
        private static float Cost(Building_FlyingFridgeCabinet cabinet, IntVec3 destination)
        {
            CabinetTravelDef rule = CabinetTravelDef.Current;
            return cabinet.GetManeuverCoolantCost(ManeuverCost.BaseCost(cabinet.Position.DistanceTo(destination), rule.mapBaseCoolant, rule.mapCoolantPerCell));
        }
        private static string Validate(Building_FlyingFridgeCabinet cabinet, IntVec3 destination, Rot4 rotation)
        {
            if (cabinet == null || !cabinet.Spawned || cabinet.Faction != Faction.OfPlayer || cabinet.Map != Find.CurrentMap
                || !cabinet.UpgradeProfile.MapJumpUnlocked) return "FAF_InvalidCabinet".Translate();
            CompCabinetTransport bay = cabinet.GetComp<CompCabinetTransport>();
            if (bay.CoolingDown) return "FAF_Cooldown".Translate();
            if (bay.AnythingLeftToLoad || (bay.innerContainer.Any && !cabinet.UpgradeProfile.WorldLaunchUnlocked)) return "FAF_UnloadBeforeJump".Translate();
            if (cabinet.Position.DistanceTo(destination) > CabinetTravelDef.Current.mapRange || destination == cabinet.Position) return "FAF_OutOfRange".Translate();
            if (cabinet.OccupiedRect().Any(cell => cell.Roofed(cabinet.Map))) return "FAF_RoofBlocked".Translate();
            AcceptanceReport landing = CabinetPayload.PlacementReport(cabinet, destination, cabinet.Map, rotation, cabinet);
            if (!landing) return landing.Reason;
            CabinetOccupancySnapshot view = CabinetOccupancy.Read(cabinet);
            if (view.RestPreparationActive) return "FAF_FinishLoading".Translate();
            if (view.HasOccupancyConflict) return view.AssociatedCorpses.Count > 0 ? "FAF_LiveCorpseConflict".Translate().ToString() : "FAF_NotEnoughSeats".Translate().ToString();
            if (view.BedSleeper != null && !CabinetSleepTransit.IsEligible(view.BedSleeper)) return "FAF_InvalidSleeper".Translate();
            if (CabinetManifest.HasNestedLivingPawn(bay.innerContainer.Concat(cabinet.Contents))) return "FAF_UnpackPassengers".Translate();
            float mass = CabinetPayloadCapacity.PayloadMass(cabinet);
            if (mass > CabinetTravelDef.Current.massCapacity) return "FAF_OverMass".Translate();
            return cabinet.LaunchCoolantUnits + 0.000001f < Cost(cabinet, destination) ? "FAF_NotEnoughCoolant".Translate().ToString() : null;
        }
        private static void Commit(Building_FlyingFridgeCabinet cabinet, IntVec3 destination, Rot4 rotation)
        {
            string reason = Validate(cabinet, destination, rotation);
            if (reason != null) { Reject(reason); return; }
            Map map = cabinet.Map; IntVec3 origin = cabinet.Position; Rot4 oldRotation = cabinet.Rotation;
            Vector3 startDraw = cabinet.DrawPos;
            CompFridgeFlight flotation = cabinet.GetComp<CompFridgeFlight>();
            float initialAngle = flotation?.CurrentVisualAngle ?? 0f, initialLift = flotation?.CurrentVisualLift ?? 0f;
            Pawn sleeper = CabinetOccupancy.Read(cabinet).BedSleeper;
            IntVec3 oldPawnCell = sleeper == null ? IntVec3.Invalid : sleeper.Position;
            CompCabinetTransport bay = cabinet.GetComp<CompCabinetTransport>();
            bool sealedSleeper = sleeper != null && bay.ResidentRestPawn == sleeper;
            ActiveTransporterInfo info = new ActiveTransporterInfo { sentTransporterDef = cabinet.def, leaveSlag = false };
            CabinetTransferTransaction transaction = new CabinetTransferTransaction();
            MinifiedThing packed = null; CabinetMapFlyer flyer = null; float cost = Cost(cabinet, destination); bool paid = false;
            bay.Transferring = true;
            try
            {
                foreach (Thing thing in bay.innerContainer.ToList()) transaction.Transfer(thing, bay.innerContainer, info.innerContainer, thing.stackCount);
                // End the real bed job before despawning; no CurrentBed/proxy travels between positions.
                // 先结束真实床任务再取消生成；不携带旧CurrentBed或代理床跨位置移动。
                if (sleeper != null)
                {
                    bay.ResidentRestPawn = null;
                    sleeper.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, startNewJob: false); sleeper.DeSpawn();
                    if (!info.innerContainer.TryAdd(sleeper, false)) throw new InvalidOperationException("Native flight owner refused the original sleeper.");
                }
                packed = cabinet.MakeMinified();
                if (packed == null || !info.innerContainer.TryAdd(packed, false)) throw new InvalidOperationException("Cabinet packing failed.");
                flyer = (CabinetMapFlyer)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("FAF_CabinetMapFlyer"));
                // The ethereal carrier has no faction; its original cabinet and passengers retain theirs.
                // 临时飞行载体不拥有派系；原柜与乘员始终保留各自派系。
                flyer.Setup(info, origin, destination, rotation, startDraw, initialAngle, initialLift);
                if (!cabinet.TryConsumeLaunchCoolant(cost)) throw new InvalidOperationException("Coolant changed before jump.");
                paid = true; GenSpawn.Spawn(flyer, origin, map);
                bay.LastManeuverTick = Find.TickManager.TicksGame;
                for (int i = 0; i < 6; i++) FleckMaker.ThrowDustPuff(origin, map, 1.2f);
            }
            catch (Exception exception)
            {
                if (flyer != null) { flyer.DetachPayload(); flyer.Destroy(); }
                bool restored = transaction.Rollback();
                if (paid) cabinet.RefundLaunchCoolant(cost);
                if (!restored)
                {
                    CabinetRecoverySite.Create(map.Tile, new[] { info }).FinishDelivery();
                    Log.Error("[F.A.F.] Map flight failed and rollback was refused; undelivered original payload retained for recovery.\n" + exception);
                    Reject("FAF_CommitFailed".Translate()); return;
                }
                if (packed != null) { info.innerContainer.Remove(packed); packed.GetDirectlyHeldThings().Remove(cabinet); packed.Destroy(); }
                if (!cabinet.Spawned) GenSpawn.Spawn(cabinet, origin, map, oldRotation);
                if (sleeper != null && !sleeper.Spawned)
                {
                    info.innerContainer.Remove(sleeper);
                    if (!sealedSleeper || !bay.innerContainer.TryAdd(sleeper, false)) GenSpawn.Spawn(sleeper, oldPawnCell, map);
                }
                Log.Error("[F.A.F.] Map flight could not commit; original cabinet/payload restored.\n" + exception);
                Reject("FAF_CommitFailed".Translate());
            }
            finally { bay.Transferring = false; }
        }
        internal static void Reject(string reason) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
    }
}
