// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    // Landing is not ordinary construction: preserve harmless entities under this exact original cabinet.
    // 落柜并非普通施工：仅在同一原柜真正展开时，保留可无害共存的原实体。
    internal static class CabinetLandingPlacement
    {
        [ThreadStatic] private static Building_FlyingFridgeCabinet original;
        [ThreadStatic] internal static Thing Spawning;
        internal static bool Active { get { return original != null; } }

        internal static bool CanPreserve(ThingDef existing)
        {
            return existing.category == ThingCategory.Filth || existing.category == ThingCategory.Plant
                && existing.passability != Traversability.Impassable && existing.plant != null && !existing.plant.IsTree;
        }
        internal static bool PreserveDuringSpawn(BuildableDef incoming, BuildableDef existing)
        {
            return original != null && Spawning == original && incoming == original.def
                && existing is ThingDef thingDef && CanPreserve(thingDef);
        }
        internal static void Spawn(Building_FlyingFridgeCabinet cabinet, IntVec3 cell, Map map, Rot4 rotation)
        {
            Building_FlyingFridgeCabinet previous = original;
            Thing previousSpawning = Spawning;
            original = cabinet; Spawning = null;
            try { GenSpawn.Spawn(cabinet, cell, map, rotation); }
            finally { original = previous; Spawning = previousSpawning; }
        }
    }
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] {
        typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
    internal static class CabinetLandingSpawnIdentity
    {
        private static void Prefix(Thing newThing, out Thing __state)
        {
            __state = CabinetLandingPlacement.Spawning;
            // Nested spawning belongs to its own object, even when another mod uses the same cabinet Def.
            // 嵌套生成归其自身对象；即使其他模组又生成同Def柜体，也不继承原柜的保留权限。
            CabinetLandingPlacement.Spawning = CabinetLandingPlacement.Active ? newThing : null;
        }
        private static Exception Finalizer(Exception __exception, Thing __state)
        {
            CabinetLandingPlacement.Spawning = __state;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    internal static class CabinetLandingHarmlessCoexistence
    {
        private static void Postfix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            if (__result && CabinetLandingPlacement.PreserveDuringSpawn(newEntDef, oldEntDef)) __result = false;
        }
    }
}
