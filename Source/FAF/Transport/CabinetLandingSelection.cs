// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    internal static class CabinetLandingSelection
    {
        internal static FloatMenuOption Option(MapParent parent, Building_FlyingFridgeCabinet cabinet,
            Action<PlanetTile, TransportersArrivalAction> confirm)
        {
            return new FloatMenuOption("LandInExistingMap".Translate(parent.Label), () => {
                Current.Game.CurrentMap = parent.Map; CameraJumper.TryHideWorld(); Rot4 rotation = cabinet.Rotation;
                Find.Targeter.BeginTargeting(TargetingParameters.ForDropPodsDestination(), target => {
                    if (!parent.HasMap || !CabinetPayload.CanPlace(cabinet, target.Cell, parent.Map, rotation)) return;
                    confirm(parent.Tile, new CabinetMapArrival(parent, target.Cell, rotation));
                }, target => {
                    if (parent.HasMap) GenDraw.DrawFieldEdges(GenAdj.OccupiedRect(target.Cell, rotation, cabinet.def.size).ToList());
                }, target => parent.HasMap && CabinetPayload.CanPlace(cabinet, target.Cell, parent.Map, rotation),
                onGuiAction: target => {
                    if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent) { rotation.Rotate(RotationDirection.Clockwise); Event.current.Use(); }
                    if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent) { rotation.Rotate(RotationDirection.Counterclockwise); Event.current.Use(); }
                    if (parent.HasMap)
                    {
                        AcceptanceReport report = CabinetPayload.PlacementReport(cabinet, target.Cell, parent.Map, rotation);
                        if (!report) GenUI.DrawMouseAttachment(null, report.Reason);
                    }
                });
            });
        }
    }
    // Persist the chosen native landing inputs once; delegate actual pod/map delivery to the native action.
    // 仅保存所选原生落地参数；实际地图投送仍委托原生抵达动作。
    public sealed class CabinetMapArrival : TransportersArrivalAction
    {
        private MapParent parent;
        private IntVec3 cell;
        private Rot4 rotation;
        public override bool GeneratesMap { get { return false; } }
        public CabinetMapArrival() { }
        internal CabinetMapArrival(MapParent parent, IntVec3 cell, Rot4 rotation) { this.parent = parent; this.cell = cell; this.rotation = rotation; }
        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            if (parent == null || parent.Tile != destinationTile) return false;
            FloatMenuAcceptanceReport native = TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, parent);
            if (!native) return native;
            foreach (IThingHolder holder in pods)
            {
                IEnumerable<Building_FlyingFridgeCabinet> cabinets = holder is CompCabinetTransport bay ? new[] { bay.Cabinet }
                    : holder is ActiveTransporterInfo info ? new[] { CabinetPayload.CabinetIn(info) }
                    : holder is Caravan caravan ? CabinetAccess.InInventory(caravan.PawnsListForReading) : Enumerable.Empty<Building_FlyingFridgeCabinet>();
                foreach (Building_FlyingFridgeCabinet cabinet in cabinets)
                {
                    if (cabinet == null) continue;
                    AcceptanceReport report = CabinetPayload.PlacementReport(cabinet, cell, parent.Map, rotation);
                    if (!report) return FloatMenuAcceptanceReport.WithFailReason(report.Reason);
                }
            }
            return true;
        }
        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            foreach (ActiveTransporterInfo info in transporters) CabinetPayload.CabinetIn(info)?.SetTransitRotation(rotation);
            new TransportersArrivalAction_LandInSpecificCell(parent, cell).Arrived(transporters, tile);
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_References.Look(ref parent, "fafLandingMap");
            Scribe_Values.Look(ref cell, "fafLandingCell"); Scribe_Values.Look(ref rotation, "fafLandingRotation");
        }
    }
}
