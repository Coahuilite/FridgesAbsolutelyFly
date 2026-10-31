// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using Verse;

namespace FAF.Transport
{
    // Balance lives in one XML def; no per-window or per-source fuel rules.
    // 平衡参数集中于一份XML，不在不同窗口/来源各建燃料规则。
    public sealed class CabinetTravelDef : Def
    {
        public float mapRange, mapBaseCoolant, mapCoolantPerCell;
        public int worldRange, orbitRange = 10, cooldownTicks;
        public double worldPeakRadiansPerTick = 0.00015, accelerationFraction = 0.30, brakingFraction = 0.08;
        public int minimumWorldTicks = 1200, orbitAscentTicks = 5400, orbitDescentTicks = 900;
        public float orbitAscentBaseCoolant = 1f, orbitDistanceFactor = 20f;
        public float worldBaseCoolant, worldCoolantPerTile, massCapacity;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (mapRange <= 0f || worldRange < 1 || massCapacity <= 0f || cooldownTicks < 0)
                yield return "Cabinet travel requires positive ranges/capacity and non-negative cooldown.";
            if (double.IsNaN(worldPeakRadiansPerTick) || double.IsInfinity(worldPeakRadiansPerTick)
                || worldPeakRadiansPerTick <= 0.0 || accelerationFraction <= 0.0 || brakingFraction <= 0.0
                || double.IsNaN(accelerationFraction + brakingFraction) || accelerationFraction + brakingFraction >= 1.0
                || minimumWorldTicks < 1 || orbitAscentTicks < 1 || orbitDescentTicks < 1)
                yield return "Cabinet cruise requires a positive finite peak, valid acceleration/braking fractions and positive route budgets.";
            foreach (float value in new[] { mapRange, massCapacity, mapBaseCoolant, mapCoolantPerCell, worldBaseCoolant, worldCoolantPerTile })
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                    yield return "Cabinet travel costs and limits must be finite and non-negative.";
        }
        public static CabinetTravelDef Current { get { return DefDatabase<CabinetTravelDef>.GetNamed("FAF_CabinetTravel"); } }
    }

    internal static class ManeuverCost
    {
        // Pure distance rule: research changes tank capacity, never this unit coolant cost.
        // 纯距离规则；科研只改变液仓容量，不改变单位冷却液费用。
        internal static float BaseCost(float distance, float baseCost, float perUnit)
        {
            return baseCost + Math.Max(0f, distance) * perUnit;
        }
    }
}
