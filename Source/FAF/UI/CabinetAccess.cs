// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using FAF.Cabinet;
using FAF.Transport;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.UI
{
    // Resolve live native ownership on each action; no cached inventory or copied balance.
    // 每次操作解析真实原生持有关系，不缓存库存或复制余额。
    internal static class CabinetAccess
    {
        internal static bool CanManage(Building_FlyingFridgeCabinet cabinet)
        {
            return CabinetOccupancy.CanReadCabinet(cabinet);
        }
        internal static bool CanTransferItems(Building_FlyingFridgeCabinet cabinet)
        {
            if (!CanManage(cabinet)) return false;
            CabinetTravelPhase phase = CabinetOccupancy.Read(cabinet).Phase;
            if (phase == CabinetTravelPhase.InTransit || phase == CabinetTravelPhase.Departing
                || phase == CabinetTravelPhase.MapFlight || phase == CabinetTravelPhase.Landing
                || phase == CabinetTravelPhase.Transferring) return false;
            return cabinet.MapHeld != null || InventoryPawns(cabinet).Any();
        }
        internal static Pawn HolderPawn(Thing thing)
        {
            Pawn_InventoryTracker inventory = thing.ParentHolder as Pawn_InventoryTracker;
            if (inventory != null) return inventory.pawn;
            Pawn_CarryTracker carry = thing.ParentHolder as Pawn_CarryTracker;
            return carry == null ? null : carry.pawn;
        }
        internal static IEnumerable<Pawn> InventoryPawns(Building_FlyingFridgeCabinet cabinet)
        {
            if (cabinet == null) yield break;
            Caravan caravan = cabinet.GetCaravan();
            if (caravan != null && caravan.IsPlayerControlled)
            {
                foreach (Pawn pawn in caravan.PawnsListForReading) yield return pawn;
            }
            else
            {
                Pawn holder = HolderPawn(cabinet.ParentHolder as Thing ?? cabinet);
                if (holder != null) yield return holder;
            }
        }
        internal static IEnumerable<Building_FlyingFridgeCabinet> InInventory(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
                if (pawn.inventory != null)
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        Building_FlyingFridgeCabinet cabinet = thing.GetInnerIfMinified() as Building_FlyingFridgeCabinet;
                        if (CanManage(cabinet)) yield return cabinet;
                    }
        }
        internal static Command_Action InventoryCommand(IEnumerable<Pawn> pawns)
        {
            Command_Action command = new Command_Action {
                defaultLabel = "FAF_PortableCabinets".Translate(), defaultDesc = "FAF_PortableCabinetsDesc".Translate(),
                action = () => {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (Building_FlyingFridgeCabinet cabinet in InInventory(pawns))
                    {
                        Building_FlyingFridgeCabinet selected = cabinet;
                        options.Add(new FloatMenuOption(cabinet.LabelCap + " #" + cabinet.thingIDNumber,
                            () => CabinetCargoUI.Open(selected)));
                    }
                    if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            CabinetCommandIcons.Apply(command, CabinetVisualRenderer.CurrentIcon);
            return command;
        }
    }

    [HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.GetGizmos))]
    internal static class PackedCabinetGizmos
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Thing __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            MinifiedThing packed = __instance as MinifiedThing;
            Building_FlyingFridgeCabinet cabinet = packed?.InnerThing as Building_FlyingFridgeCabinet;
            if (!CabinetAccess.CanManage(cabinet)) yield break;
            yield return CabinetResourceGizmo.For(cabinet);
            Command_Action thermostat = new Command_Action {
                defaultLabel = Window_CabinetThermostat.TitleFor(cabinet),
                action = () => Window_CabinetThermostat.OpenFor(cabinet)
            };
            CabinetCommandIcons.ApplyCabinet(thermostat, cabinet);
            yield return thermostat;
            yield return CabinetCargoUI.CommandFor(cabinet);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class PawnPortableCabinetGizmos
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Building_FlyingFridgeCabinet unused in CabinetAccess.InInventory(new[] { __instance }))
            { yield return CabinetAccess.InventoryCommand(new[] { __instance }); break; }
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    internal static class CaravanPortableCabinetGizmos
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (!__instance.IsPlayerControlled) yield break;
            foreach (Building_FlyingFridgeCabinet unused in CabinetAccess.InInventory(__instance.PawnsListForReading))
            { yield return CabinetAccess.InventoryCommand(__instance.PawnsListForReading); break; }
        }
    }
}
