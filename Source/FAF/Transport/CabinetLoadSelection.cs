// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    // Only the current cabinet loading dialog owns this short-lived input scope, never an inventory or reservation.
    // 作用域仅属于当前冷柜装载窗口的输入调用，不保存库存或第二份预约。
    internal static class CabinetLoadSelection
    {
        internal sealed class InputScope
        {
            internal Dialog_LoadTransporters Dialog;
            internal List<CompCabinetTransport> Bays;
            internal Transferable Rejected;
        }
        [ThreadStatic] internal static InputScope Current;
        private static readonly AccessTools.FieldRef<Dialog_LoadTransporters, List<TransferableOneWay>> Transferables =
            AccessTools.FieldRefAccess<Dialog_LoadTransporters, List<TransferableOneWay>>("transferables");

        internal static bool AllowIncrease(Transferable transferable, int destination)
        {
            InputScope scope = Current;
            TransferableOneWay changed = transferable as TransferableOneWay;
            if (scope == null || changed == null || destination <= changed.CountToTransfer) return true;
            List<TransferableOneWay> transferables = Transferables(scope.Dialog);
            if (!transferables.Contains(changed)) return true;
            AcceptanceReport report = Check(scope.Bays, transferables, changed, Math.Min(destination, changed.MaxCount));
            if (report) return true;
            scope.Rejected = changed;
            CabinetMapJump.Reject(report.Reason);
            return false;
        }

        private static IEnumerable<Thing> SelectedRoots(IEnumerable<TransferableOneWay> transferables,
            TransferableOneWay changed, int destination)
        {
            foreach (TransferableOneWay entry in transferables)
            {
                int remaining = entry == changed ? destination : entry.CountToTransfer;
                foreach (Thing thing in entry.things)
                {
                    if (remaining <= 0) break;
                    if (thing == null || thing.Destroyed) continue;
                    yield return thing;
                    remaining -= thing.stackCount;
                }
            }
        }

        internal static bool LeavesSeatsForSelectedPawns(CompCabinetTransport destination, List<CompCabinetTransport> bays)
        {
            InputScope scope = Current;
            if (scope == null || !scope.Bays.Contains(destination)) return true;
            CabinetOccupancySnapshot target = CabinetOccupancy.Read(destination.Cabinet);
            if (target.AssociatedCorpses.Count > 0 || CabinetBiologicalPayload.PendingCorpses(destination)) return true;
            HashSet<Pawn> remaining = CabinetBiologicalPayload.Read(SelectedRoots(Transferables(scope.Dialog), null, 0)).Living;
            int free = 0;
            foreach (CompCabinetTransport bay in bays)
            {
                CabinetOccupancySnapshot state = CabinetOccupancy.Read(bay.Cabinet);
                remaining.ExceptWith(state.ActualOccupants); remaining.ExceptWith(state.PendingBoarders);
                if (state.RestPreparationPawn != null) remaining.Remove(state.RestPreparationPawn);
                if (bay.Cabinet.UpgradeProfile.WorldLaunchUnlocked && !state.HasOccupancyConflict
                    && state.ActualOccupants.Count == 0 && state.PendingBoarders.Count == 0 && !state.RestPreparationActive
                    && state.AssociatedCorpses.Count == 0 && !CabinetBiologicalPayload.PendingCorpses(bay)) free++;
            }
            return free > remaining.Count;
        }

        internal static AcceptanceReport Check(List<CompCabinetTransport> bays, List<TransferableOneWay> transferables,
            TransferableOneWay changed = null, int destination = 0)
        {
            List<Thing> roots = SelectedRoots(transferables, changed, destination).Distinct().ToList();
            CabinetBiologicalPayload selected = CabinetBiologicalPayload.Read(roots);
            if (selected.UncontainedDead.Count > 0) return "FAF_InvalidBoardedPassenger".Translate();
            if (CabinetManifest.HasNestedLivingPawn(roots)) return "FAF_UnpackPassengers".Translate();
            if (roots.OfType<Pawn>().Any(pawn => CabinetBiologicalPayload.Read(new[] { pawn }).Corpses.Count > 0))
                return "FAF_LiveCorpseConflict".Translate();

            List<CabinetOccupancySnapshot> states = bays.Select(bay => CabinetOccupancy.Read(bay.Cabinet)).ToList();
            if (states.Any(state => state.HasOccupancyConflict)) return "FAF_LiveCorpseConflict".Translate();
            HashSet<Thing> represented = new HashSet<Thing>(transferables.SelectMany(entry => entry.things));
            HashSet<Thing> chosen = new HashSet<Thing>(roots);
            Dictionary<Pawn, int> fixedOwners = new Dictionary<Pawn, int>();
            bool[] corpseSlots = states.Select(state => state.AssociatedCorpses.Count > 0).ToArray();
            bool[] available = bays.Select(bay => bay.parent.Spawned && !bay.parent.Destroyed && bay.parent.Faction == Faction.OfPlayer && !bay.Transferring).ToArray();
            bool[] eligible = Enumerable.Range(0, bays.Count).Select(slot => available[slot] && bays[slot].Cabinet.UpgradeProfile.WorldLaunchUnlocked).ToArray();
            // Actual occupants take precedence; the same pawn appearing in the UI or an intent consumes no second seat.
            // 实际住客优先；同一Pawn同时出现在选择或意图中时，不重复占席。
            for (int slot = 0; slot < states.Count; slot++)
                foreach (Pawn pawn in states[slot].ActualOccupants)
                    if (!fixedOwners.ContainsKey(pawn)) fixedOwners.Add(pawn, slot);
            for (int slot = 0; slot < states.Count; slot++)
            {
                Pawn preparing = states[slot].RestPreparationPawn;
                if (preparing != null && !fixedOwners.ContainsKey(preparing)) fixedOwners.Add(preparing, slot);
                foreach (Pawn pawn in states[slot].PendingBoarders)
                    if ((!represented.Contains(pawn) || selected.Living.Contains(pawn)) && !fixedOwners.ContainsKey(pawn))
                        fixedOwners.Add(pawn, slot);
                // A dialog deselection cancels a represented pending intent, not an already held body.
                // 减选可取消本窗口所表示的待装意图，但不会凭空腾出已实际持有身体的席位。
                if (bays[slot].leftToLoad != null)
                    corpseSlots[slot] |= CabinetBiologicalPayload.Read(bays[slot].leftToLoad.Where(entry => entry.CountToTransfer > 0)
                        .SelectMany(entry => entry.things).Where(thing => !represented.Contains(thing) || chosen.Contains(thing))).Corpses.Count > 0;
            }
            int[] occupants = Enumerable.Range(0, bays.Count).Select(slot => fixedOwners.Count(pair => pair.Value == slot)).ToArray();
            if (Enumerable.Range(0, bays.Count).Any(slot => occupants[slot] > 1 || occupants[slot] > 0 && corpseSlots[slot]))
                return "FAF_LiveCorpseConflict".Translate();
            int newLiving = selected.Living.Count(pawn => !fixedOwners.ContainsKey(pawn));
            int free = Enumerable.Range(0, bays.Count).Count(slot => eligible[slot] && occupants[slot] == 0 && !corpseSlots[slot]);
            if (newLiving > 0 && !eligible.Any(value => value)) return "FAF_BoardingLocked".Translate();
            if (newLiving > free) return "FAF_LoadSelectionNoBerth".Translate();
            bool corpseBerth = Enumerable.Range(0, bays.Count).Any(slot => available[slot] && occupants[slot] == 0 && corpseSlots[slot]);
            // All corpses may share a cargo bay; reserve one only if no real corpse bay already exists.
            // 遗体可以多具同仓；只有尚无真实遗体货仓时，才额外保留一个无活体的仓位。
            bool spareCargoBerth = free > newLiving || Enumerable.Range(0, bays.Count)
                .Any(slot => available[slot] && !eligible[slot] && occupants[slot] == 0 && !corpseSlots[slot]);
            if (selected.Corpses.Count > 0 && !corpseBerth && !spareCargoBerth) return "FAF_LiveCorpseConflict".Translate();
            return CabinetLoadingCapacity.Plan(bays, transferables, out _, changed, destination);
        }
    }

    [HarmonyPatch]
    internal static class CabinetLoadSelectionInputScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.DoWindowContents));
            yield return AccessTools.Method(typeof(Dialog_LoadTransporters), "AssignTransferablesToRandomTransporters");
        }
        private static void Prefix(Dialog_LoadTransporters __instance, List<CompTransporter> ___transporters,
            out CabinetLoadSelection.InputScope __state)
        {
            __state = CabinetLoadSelection.Current;
            CabinetLoadSelection.Current = ___transporters.Count > 0 && ___transporters.All(bay => bay is CompCabinetTransport)
                ? new CabinetLoadSelection.InputScope { Dialog = __instance, Bays = ___transporters.Cast<CompCabinetTransport>().ToList() } : null;
            if (CabinetLoadSelection.Current != null)
                foreach (CompCabinetTransport bay in CabinetLoadSelection.Current.Bays) bay.RefreshLoadingCapacity();
        }
        private static Exception Finalizer(Exception __exception, CabinetLoadSelection.InputScope __state)
        {
            CabinetLoadSelection.Current = __state;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Transferable), nameof(Transferable.AdjustTo))]
    internal static class CabinetLoadSelectionIncrease
    {
        private static bool Prefix(Transferable __instance, int destination) { return CabinetLoadSelection.AllowIncrease(__instance, destination); }
    }
    [HarmonyPatch(typeof(TransferableUIUtility), "DoCountAdjustInterfaceInternal")]
    internal static class CabinetLoadSelectionRejectedBuffer
    {
        private static void Postfix(Transferable trad)
        {
            if (CabinetLoadSelection.Current?.Rejected != trad) return;
            trad.EditBuffer = trad.CountToTransfer.ToStringCached();
            CabinetLoadSelection.Current.Rejected = null;
        }
    }
}
