// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using Verse;
using Verse.Sound;

namespace FAF.Transport
{
    // Native drop-pod factory selects this class only through this cabinet's dropPodActive def field.
    // 原生运输舱工厂只通过本柜 dropPodActive 字段选择此类，不修改其他运输舱。
    public sealed class CabinetActivePod : ActiveTransporter
    {
        private bool placementBlocked;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref placementBlocked, "cabinetPlacementBlocked", false);
        }
        protected override void Tick()
        {
            if (Contents == null || !Spawned) return;
            age++;
            if (!placementBlocked && age > Contents.openDelay) TryOpen();
        }
        private void TryOpen()
        {
            if (!Spawned || Contents == null) return;
            try
            {
                Building_FlyingFridgeCabinet cabinet = CabinetPayload.CabinetIn(Contents);
                if (cabinet != null)
                {
                    bool landed = false;
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, 6f, true))
                        if (CabinetPayload.TryUnfold(Contents, Map, cell, cabinet.Rotation, this)) { landed = true; break; }
                    if (!landed) { placementBlocked = true; return; }
                    if (!Contents.innerContainer.Any) { Destroy(); return; }
                }
                foreach (Thing thing in Contents.innerContainer.ToList())
                {
                    // TryDrop removes only successful placements. Never clear or destroy a failed remainder.
                    // TryDrop 只移除放置成功的实体；失败余物绝不 Clear 或 Destroy。
                    if (!Contents.innerContainer.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out Thing placed,
                        nearPlaceValidator: null, playDropSound: false)) continue;
                    if (placed is Pawn pawn)
                    {
                        if (pawn.RaceProps.Humanlike) TaleRecorder.RecordTale(TaleDefOf.LandedInPod, pawn);
                        if (pawn.IsColonist && pawn.Spawned && !Map.IsPlayerHome) pawn.drafter.Drafted = true;
                        if (pawn.guest != null && pawn.guest.IsPrisoner) pawn.guest.WaitInsteadOfEscapingForDefaultTicks();
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("[F.A.F.] Cabinet pod unloading failed; remaining payload retained for manual retry.\n" + exception);
            }
            placementBlocked = Contents.innerContainer.Any;
            if (placementBlocked)
            {
                Messages.Message("FAF_PodPlacementBlocked".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }
            if (def.soundOpen != null) def.soundOpen.PlayOneShot(new TargetInfo(Position, Map));
            Destroy();
        }
        protected override void DrawAt(UnityEngine.Vector3 drawLoc, bool flip = false)
        {
            if (CabinetPayload.CabinetIn(Contents) != null) CabinetPayload.Draw(Contents, drawLoc);
            else base.DrawAt(drawLoc, flip);
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode == DestroyMode.KillFinalize && Contents?.innerContainer.Any == true && MapHeld != null)
                CabinetPayload.Breach(Contents, MapHeld, PositionHeld);
            if (Contents?.innerContainer.Any == true)
            {
                if (MapHeld == null)
                {
                    Log.ErrorOnce("[F.A.F.] Refused to destroy an unplaced cabinet pod with live payload and no map.", thingIDNumber);
                    return;
                }
                ActiveTransporterInfo info = Contents;
                RimWorld.Planet.PlanetTile tile = MapHeld.Tile;
                CabinetRecoverySite.Create(tile, new[] { info }).FinishDelivery();
                // Native Contents=null detaches its old parent; restore the new authoritative parent afterwards.
                // 原生 Contents=null 会解除旧 parent；随后恢复已经登记的新权威 parent。
                IThingHolder recovery = info.parent;
                Contents = null;
                info.parent = recovery;
            }
            base.Destroy(mode);
        }
        public override string GetInspectString()
        {
            Building_FlyingFridgeCabinet cab = CabinetPayload.CabinetIn(Contents);
            return cab == null ? base.GetInspectString() : cab.LabelCap + "\n" + "Health".Translate() + ": " + cab.HitPoints + "/" + cab.MaxHitPoints;
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (placementBlocked) yield return new Command_Action {
                defaultLabel = "FAF_RetryPodPlacement".Translate(), defaultDesc = "FAF_PodPlacementBlocked".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get("FAF/UI/Commands/Fly"), action = TryOpen
            };
            if (Contents == null) yield break;
            yield return CabinetCargoUI.PayloadCommandFor(Contents);
            foreach (MinifiedThing packed in Contents.innerContainer.OfType<MinifiedThing>())
            {
                Building_FlyingFridgeCabinet cabinet = packed.InnerThing as Building_FlyingFridgeCabinet;
                if (cabinet != null)
                {
                    Command_Action control = new Command_Action {
                        defaultLabel = Window_CabinetThermostat.TitleFor(cabinet), action = () => Window_CabinetThermostat.OpenFor(cabinet)
                    };
                    CabinetCommandIcons.ApplyCabinet(control, cabinet); yield return control;
                }
            }
        }
    }
}
