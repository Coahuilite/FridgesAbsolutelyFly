// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    // Only new freezer missions use this saved absolute clock; old native travellers retain native timing.
    // 仅新柜体任务使用保存的绝对时钟；旧原生航程保留原计时。
    public sealed class CabinetTravellingTransporters : TravellingTransporters
    {
        private int departureTick, duration, algorithm = 1;
        private double angle, peak = 0.000375, acceleration = 0.30, braking = 0.08;
        private int minimum = 240;
        private Vector3 sourceDirection;
        private int additionalTicks;
        private CabinetCruiseSchedule schedule;
        protected override int UpdateRateTicks { get { return 1; } }
        public override void Draw() { CabinetWorldIcon.Draw(this); }
        private Vector3 RouteStart
        {
            get { return sourceDirection.normalized * Find.WorldGrid.GetTileCenter(destinationTile).magnitude; }
        }
        public override Vector3 DrawPos
        {
            get { return algorithm == CabinetCruiseRoute.CurrentAlgorithm && schedule != null
                ? Vector3.Slerp(RouteStart, Find.WorldGrid.GetTileCenter(destinationTile), ProgressNow()) : base.DrawPos; }
        }
        public override bool ExpandingIconFlipHorizontal
        {
            get { return algorithm == CabinetCruiseRoute.CurrentAlgorithm
                ? GenWorldUI.WorldToUIPosition(RouteStart).x > GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(destinationTile)).x
                : base.ExpandingIconFlipHorizontal; }
        }
        public override float ExpandingIconRotation
        {
            get
            {
                if (algorithm != CabinetCruiseRoute.CurrentAlgorithm || !def.rotateGraphicWhenTraveling) return base.ExpandingIconRotation;
                Vector2 start = GenWorldUI.WorldToUIPosition(RouteStart);
                Vector2 end = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(destinationTile));
                float rotation = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
                if (rotation > 180f) rotation -= 180f;
                return rotation + 90f;
            }
        }
        internal void ConfigureCruise(PlanetTile origin, PlanetTile destination)
        {
            sourceDirection = CabinetCruiseRoute.SourceDirection(origin);
            additionalTicks = CabinetCruiseRoute.AdditionalTicks(origin, destination);
            algorithm = CabinetCruiseRoute.CurrentAlgorithm;
        }
        public override void PostAdd()
        {
            base.PostAdd();
            CabinetLeaving leaving = CabinetLeaving.CurrentDeparture;
            if (algorithm != CabinetCruiseRoute.CurrentAlgorithm && leaving?.HasNewCruiseSource == true
                && leaving.destinationTile == destinationTile && leaving.worldObjectDef == def)
            {
                sourceDirection = leaving.RouteSourceDirection; additionalTicks = leaving.RouteAdditionalTicks;
                algorithm = CabinetCruiseRoute.CurrentAlgorithm;
            }
            if (algorithm == CabinetCruiseRoute.CurrentAlgorithm)
            {
                angle = CabinetCruiseRoute.Angle(sourceDirection, destinationTile);
                CabinetTravelDef rule = CabinetTravelDef.Current;
                peak = rule.worldPeakRadiansPerTick; acceleration = rule.accelerationFraction;
                braking = rule.brakingFraction; minimum = additionalTicks > 0 ? 0 : rule.minimumWorldTicks;
            }
            else
            {
                // An old saved leaving carrier has no new source marker. Keep the original projected route/rules.
                // 旧存离图载体没有新源标记，保留原来的投影路径及旧计时参数，不拿新Def重排。
                angle = GenMath.SphericalDistance(Find.WorldGrid.GetTileCenter(Tile).normalized,
                    Find.WorldGrid.GetTileCenter(destinationTile).normalized);
            }
            departureTick = Find.TickManager.TicksGame;
            schedule = new CabinetCruiseSchedule(angle, peak, acceleration, braking, minimum, additionalTicks: additionalTicks);
            duration = schedule.DurationTicks;
        }
        internal float ProgressNow()
        {
            CabinetCruiseSchedule.Sample sample = schedule.At((long)Find.TickManager.TicksGame - departureTick);
            float progress = (float)sample.Progress;
            // Float rounding near the smooth braking endpoint must not make Core arrive before the saved clock.
            // 平滑制动末尾的float舍入不能让Core早于保存的时钟抵达；算法1仍保持原有行为。
            return algorithm == CabinetCruiseRoute.CurrentAlgorithm && sample.RemainingTicks > 0
                ? Mathf.Min(progress, 0.99999994f) : progress;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref departureTick, "fafDepartureTick"); Scribe_Values.Look(ref duration, "fafDuration");
            Scribe_Values.Look(ref angle, "fafRouteAngle"); Scribe_Values.Look(ref peak, "fafPeak", 0.000375);
            Scribe_Values.Look(ref acceleration, "fafAcceleration", 0.30); Scribe_Values.Look(ref braking, "fafBraking", 0.08);
            Scribe_Values.Look(ref minimum, "fafMinimumTicks", 240); Scribe_Values.Look(ref algorithm, "fafCurveAlgorithm", 1);
            Scribe_Values.Look(ref sourceDirection, "fafSourceDirection");
            Scribe_Values.Look(ref additionalTicks, "fafAdditionalTicks");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (algorithm != 1 && algorithm != CabinetCruiseRoute.CurrentAlgorithm)
                    throw new InvalidOperationException("Unsupported freezer cruise algorithm: " + algorithm);
                if (algorithm == CabinetCruiseRoute.CurrentAlgorithm
                    && !(sourceDirection.sqrMagnitude >= 0.5f && sourceDirection.sqrMagnitude <= 1.5f))
                    throw new InvalidOperationException("Saved freezer cruise has no valid original source direction.");
                schedule = new CabinetCruiseSchedule(angle, peak, acceleration, braking, minimum, duration,
                    algorithm == CabinetCruiseRoute.CurrentAlgorithm ? additionalTicks : 0);
            }
        }
        public override string GetInspectString()
        {
            string phase = algorithm == CabinetCruiseRoute.CurrentAlgorithm && additionalTicks > 0
                ? (destinationTile.LayerDef.isSpace ? "FAF_CruiseEnteringOrbit" : "FAF_CruiseReturningSurface").Translate() + "\n" : "";
            return base.GetInspectString() + "\n" + phase + "FAF_CruiseETA".Translate(
                schedule.At((long)Find.TickManager.TicksGame - departureTick).RemainingTicks.ToStringTicksToPeriod());
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            List<IThingHolder> holders = new List<IThingHolder>(); GetChildHolders(holders);
            foreach (IThingHolder holder in holders)
                if (holder is ActiveTransporterInfo info) yield return CabinetCargoUI.PayloadCommandFor(info);
        }
    }

    [HarmonyPatch(typeof(TravellingTransporters), "TickInterval")]
    internal static class CabinetCruiseProgressPatch
    {
        internal static bool Available;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            var progress = AccessTools.Field(typeof(TravellingTransporters), "traveledPct");
            int found = 0;
            for (int i = 1; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, progress) && code[i - 1].opcode == OpCodes.Add) found++;
            Available = found == 1;
            if (!Available)
            {
                Log.Error("[F.A.F.] Native cruise progress anchor changed. New freezer world departures are disabled; native travellers are unchanged.");
                return code;
            }
            List<CodeInstruction> result = new List<CodeInstruction>();
            for (int i = 0; i < code.Count; i++)
            {
                if (i > 0 && code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, progress) && code[i - 1].opcode == OpCodes.Add)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(CodeInstruction.Call(typeof(CabinetCruiseProgressPatch), nameof(SelectProgress)));
                }
                result.Add(code[i]);
            }
            return result;
        }
        private static float SelectProgress(float candidate, TravellingTransporters instance)
        {
            return instance is CabinetTravellingTransporters cabinet ? cabinet.ProgressNow() : candidate;
        }
    }
}
