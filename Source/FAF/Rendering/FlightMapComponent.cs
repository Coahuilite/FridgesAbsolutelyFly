// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Rendering
{
    public sealed class FlightMapComponent : MapComponent
    {
        // EN: This is derived render state; the authoritative flying flag is saved on each ThingComp.
        // 中：这里只保存派生绘制状态；唯一持久化的飞行状态仍由每个 ThingComp 持有。
        private readonly List<CompFridgeFlight> flyingFridges = new List<CompFridgeFlight>();

        public FlightMapComponent(Map map) : base(map) { }

        internal void Register(CompFridgeFlight flight)
        {
            if (!flyingFridges.Contains(flight)) flyingFridges.Add(flight);
            flight.InvalidatePrintedAppearance();
        }

        internal void Unregister(CompFridgeFlight flight) { flyingFridges.Remove(flight); }

        internal void InvalidateAllPrintedAppearances()
        {
            foreach (CompFridgeFlight flight in flyingFridges) flight.InvalidatePrintedAppearance();
        }

        internal void MapMeshesDirtied()
        {
            foreach (CompFridgeFlight flight in flyingFridges)
            {
                if (!flight.Flying) continue;
                Section section = map.mapDrawer.SectionAt(flight.parent.Position);
                if ((section.dirtyFlags & (ulong)MapMeshFlagDefOf.Things) != 0)
                    flight.InvalidatePrintedAppearance();
            }
        }

        public override void MapComponentTick()
        {
            for (int index = flyingFridges.Count - 1; index >= 0; index--)
                flyingFridges[index].AdvancePose();
        }

        public override void MapComponentDraw()
        {
            if (flyingFridges.Count == 0) return;
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(1);
            if (WorldComponent_GravshipController.GravshipRenderInProgess)
                view = view.Encapsulate(WorldComponent_GravshipController.GravshipRenderBounds);
            // Reverse iteration permits a failed instance to unregister itself without aborting its neighbours.
            // 倒序遍历允许失败实例取消自身注册，不会中断相邻实例。
            for (int index = flyingFridges.Count - 1; index >= 0; index--)
            {
                CompFridgeFlight flight = flyingFridges[index];
                if (!flight.Flying || (!flight.parent.def.seeThroughFog && map.fogGrid.IsFogged(flight.parent.Position))) continue;
                // EN: MapMeshOnly has one owner: its captured Print mesh. Never register another DynamicDrawPhase for it.
                // 中：MapMeshOnly 的主图仅由捕获的 Print 网格负责，绝不额外注册 DynamicDrawPhase 重画。
                // EN: MapMeshAndRealTime keeps its normal dynamic extras; RealtimeOnly remains wholly native Draw.
                // 中：MapMeshAndRealTime 保留原动态附加层；RealtimeOnly 则完全沿用原 Draw。
                try { flight.DrawPrintedAppearance(view); }
                catch (Exception exception) { flight.HandleDrawFailure(exception); }
            }
        }

        public override void MapRemoved()
        {
            foreach (CompFridgeFlight flight in flyingFridges) flight.ReleasePrintedAppearance();
            flyingFridges.Clear();
            base.MapRemoved();
        }
    }

    [HarmonyPatch(typeof(SectionLayer_ThingsGeneral), "TakePrintFrom")]
    internal static class FlyingInstanceStaticPrint
    {
        private static bool Prefix(SectionLayer_ThingsGeneral __instance, Thing t)
        {
            ThingWithComps building = t as ThingWithComps;
            CompFridgeFlight flight = building == null ? null : building.GetComp<CompFridgeFlight>();
            if (flight == null || !flight.Flying) return true;
            // EN: The body moves to its instance snapshot, but the logical power connection stays on the ground.
            // 中：柜体转入实例飞行快照，而逻辑供电连线仍印在地面。
            GroundPowerWires.PrintConnections(building, __instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.MapMeshDirty),
        new[] { typeof(IntVec3), typeof(ulong), typeof(bool), typeof(bool) })]
    internal static class FlyingInstancePrintInvalidation
    {
        private static void Postfix(Map ___map, Section[,] ___sections, ulong dirtyFlags)
        {
            // EN: Match MapDrawer's own initialization boundary before accessing its sections.
            // 中：先遵守 MapDrawer 自身的初始化边界，再访问分区。
            if (Current.ProgramState == ProgramState.Playing && ___sections != null
                && (dirtyFlags & (ulong)MapMeshFlagDefOf.Things) != 0)
                ___map.GetComponent<FlightMapComponent>().MapMeshesDirtied();
        }
    }

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.WholeMapChanged))]
    internal static class FlyingInstanceWholeMapInvalidation
    {
        private static void Postfix(Map ___map, ulong change)
        {
            if ((change & (ulong)MapMeshFlagDefOf.Things) != 0)
                ___map.GetComponent<FlightMapComponent>().InvalidateAllPrintedAppearances();
        }
    }
}
