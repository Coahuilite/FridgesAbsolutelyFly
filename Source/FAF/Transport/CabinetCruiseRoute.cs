// SPDX-License-Identifier: MPL-2.0
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    /// <summary>One route quote from real source/target directions; fuel still uses its existing projected grid distance. / 真实源/终方向的唯一航时报价；液费仍沿原目标层投影格距。</summary>
    internal static class CabinetCruiseRoute
    {
        internal const int CurrentAlgorithm = 2;

        internal static Vector3 SourceDirection(PlanetTile origin)
        {
            return Find.WorldGrid.GetTileCenter(origin).normalized;
        }
        internal static double Angle(Vector3 originDirection, PlanetTile destination)
        {
            return GenMath.SphericalDistance(originDirection.normalized,
                Find.WorldGrid.GetTileCenter(destination).normalized);
        }
        internal static int AdditionalTicks(PlanetTile origin, PlanetTile destination)
        {
            if (origin.Layer == destination.Layer) return 0;
            CabinetTravelDef rule = CabinetTravelDef.Current;
            return destination.LayerDef.isSpace ? rule.orbitAscentTicks : rule.orbitDescentTicks;
        }
        internal static CabinetCruiseSchedule Quote(PlanetTile origin, PlanetTile destination)
        {
            CabinetTravelDef rule = CabinetTravelDef.Current;
            return new CabinetCruiseSchedule(Angle(SourceDirection(origin), destination), rule.worldPeakRadiansPerTick,
                rule.accelerationFraction, rule.brakingFraction, origin.Layer == destination.Layer ? rule.minimumWorldTicks : 0,
                additionalTicks: AdditionalTicks(origin, destination));
        }
    }
}
