// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    // Draw the original cabinet layers; native FlyShipLeaving owns movement, payload and world handoff.
    // 绘制原柜各层；运动、载荷与世界交接仍由原生FlyShipLeaving负责。
    public sealed class CabinetLeaving : FlyShipLeaving
    {
        private float initialAngle, initialLift;
        private int visualAge;
        private Vector3 routeSourceDirection;
        private int routeAdditionalTicks, routeAlgorithm = 1;
        [System.ThreadStatic] internal static CabinetLeaving CurrentDeparture;
        internal bool HasNewCruiseSource { get { return routeAlgorithm == CabinetCruiseRoute.CurrentAlgorithm; } }
        internal Vector3 RouteSourceDirection { get { return routeSourceDirection; } }
        internal int RouteAdditionalTicks { get { return routeAdditionalTicks; } }
        internal void ConfigurePose(float angle, float lift) { initialAngle = angle; initialLift = lift; }
        internal void ConfigureCruise(PlanetTile origin, PlanetTile destination)
        {
            routeSourceDirection = CabinetCruiseRoute.SourceDirection(origin);
            routeAdditionalTicks = CabinetCruiseRoute.AdditionalTicks(origin, destination);
            routeAlgorithm = CabinetCruiseRoute.CurrentAlgorithm;
        }
        protected override void LeaveMap()
        {
            // Core projects Tile before creating the world mission. Carry only this departure's original direction
            // through that synchronous call; Core still owns grouping, payload and world-pawn handoff.
            // Core创建世界任务前会投影Tile；仅在此同步调用内传递本次真实源方向，编组/载荷/世界Pawn仍归原生。
            CabinetLeaving previous = CurrentDeparture;
            try { CurrentDeparture = this; base.LeaveMap(); }
            finally { CurrentDeparture = previous; }
        }
        public override Graphic Graphic { get { return CabinetVisualRenderer.DepartingGraphic; } }
        protected override void DrawAt(UnityEngine.Vector3 drawLoc, bool flip = false)
        {
            GetDrawPositionAndRotation(ref drawLoc, out _);
            float remaining = 1f - UnityEngine.Mathf.SmoothStep(0f, 1f, UnityEngine.Mathf.Clamp01(visualAge / 30f));
            CabinetPayload.Draw(Contents, drawLoc, true, UnityEngine.Mathf.DeltaAngle(0f, initialAngle) * remaining, initialLift * remaining);
            DrawDropSpotShadow();
        }
        protected override void TickInterval(int delta)
        {
            visualAge += delta; base.TickInterval(delta);
            if (Spawned && this.IsHashIntervalTick(6, delta)) FleckMaker.ThrowSmoke(DrawPos, Map, 0.8f);
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref initialAngle, "fafInitialAngle");
            Scribe_Values.Look(ref initialLift, "fafInitialLift"); Scribe_Values.Look(ref visualAge, "fafVisualAge");
            Scribe_Values.Look(ref routeSourceDirection, "fafCruiseSourceDirection");
            Scribe_Values.Look(ref routeAdditionalTicks, "fafCruiseAdditionalTicks");
            Scribe_Values.Look(ref routeAlgorithm, "fafCruiseAlgorithm", 1);
        }
        public override System.Collections.Generic.IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Contents != null) yield return FAF.UI.CabinetCargoUI.PayloadCommandFor(Contents);
        }
    }
}
