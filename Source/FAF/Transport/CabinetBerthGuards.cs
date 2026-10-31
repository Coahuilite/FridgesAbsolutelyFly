// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NivarianSleepInFridges;
using FAF.Cabinet;
using RimWorld;
using Verse;
using Verse.AI;

namespace FAF.Transport
{
    // The berth is shared by the real proxy and the real bay; old over-capacity contents are never culled.
    // 真实代理床与运输舱共用席位；旧超员内容绝不裁除。
    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.GetCountCanAccept))]
    internal static class CabinetBerthAcceptance
    {
        private static void Postfix(ThingOwner __instance, Thing item, bool canMergeWithExistingStacks, ref int __result)
        {
            Building_FlyingFridgeCabinet cabinet = (__instance.Owner as CompCabinetTransport)?.Cabinet
                ?? __instance.Owner as Building_FlyingFridgeCabinet;
            if (cabinet == null || cabinet.GetComp<CompCabinetTransport>().Transferring || CabinetDeathHandoff.IsReplacement(cabinet, item)
                || Cabinet.Integration.CabinetCapacityTransfer.Restoring(__instance, item)) return;
            __result = Math.Min(__result, CabinetPayloadCapacity.CountCanAdd(cabinet, item, __result));
            if (!(item is Pawn pawn))
            {
                if (!CabinetBiologicalPayload.CanAcceptCargo(cabinet, item)
                    || __instance.Owner is Building_FlyingFridgeCabinet && !cabinet.CanAcceptItem(item)) __result = 0;
                else if (__instance.Owner is Building_FlyingFridgeCabinet)
                    __result = Math.Min(__result, cabinet.CountCanAccept(item, canMergeWithExistingStacks));
                return;
            }
            CompCabinetTransport bay = __instance.Owner as CompCabinetTransport;
            if (bay == null) { __result = 0; return; }
            CabinetOccupancySnapshot state = CabinetOccupancy.Read(bay.Cabinet);
            if (!bay.Cabinet.UpgradeProfile.WorldLaunchUnlocked || state.HasOccupancyConflict || state.AssociatedCorpses.Count > 0
                || CabinetBiologicalPayload.PendingCorpses(bay) || CabinetBiologicalPayload.Read(new[] { pawn }).Corpses.Count > 0
                || state.ActualOccupants.Any(p => p != pawn)
                || state.PendingBoarders.Any(p => p != pawn) || state.RestPreparationPawn != null && state.RestPreparationPawn != pawn) __result = 0;
        }
    }
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedNow))]
    internal static class CabinetBedSharesBerth
    {
        private static void Postfix(Thing bedThing, Pawn sleeper, ref bool __result)
        {
            Building_FlyingFridgeCabinet cab = (bedThing as Building_FridgeBedProxy)?.ParentFridge as Building_FlyingFridgeCabinet;
            if (!__result || cab == null) return;
            if (CabinetPayloadCapacity.CountCanAdd(cab, sleeper, 1) == 0) { __result = false; return; }
            CabinetOccupancySnapshot state = CabinetOccupancy.Read(cab);
            if (state.HasOccupancyConflict || state.AssociatedCorpses.Count > 0 || CabinetBiologicalPayload.PendingCorpses(cab.GetComp<CompCabinetTransport>())
                || CabinetBiologicalPayload.Read(new[] { sleeper }).Corpses.Count > 0
                || state.ActualOccupants.Any(p => p != sleeper) || state.PendingBoarders.Any(p => p != sleeper)
                || state.RestPreparationPawn != null && state.RestPreparationPawn != sleeper) __result = false;
        }
    }
    [HarmonyPatch(typeof(JobDriver_EnterTransporter), "MakeNewToils")]
    internal static class CabinetBoardingBeforeDespawn
    {
        private static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver_EnterTransporter __instance)
        {
            foreach (Toil toil in __result)
            {
                Action original = toil.initAction;
                if (original != null) toil.initAction = () => {
                    CompCabinetTransport bay = __instance.Transporter as CompCabinetTransport;
                    if (bay == null) { original(); return; }
                    AcceptanceReport allowed = CabinetOccupancy.CanAdmit(bay.Cabinet, __instance.pawn);
                    if (!allowed) { __instance.EndJobWith(JobCondition.Incompletable); return; }
                    Map map = __instance.pawn.Map; IntVec3 cell = __instance.pawn.Position;
                    try { original(); }
                    finally
                    {
                        // A peer patch may still reject TryAdd; restore the exact pawn, not a replacement.
                        // 其他补丁仍可拒绝TryAdd；恢复原pawn，不能留无主持有者或复制。
                        Pawn pawn = __instance.pawn;
                        if (!pawn.Destroyed && !pawn.Spawned && pawn.holdingOwner == null && map != null)
                            GenSpawn.Spawn(pawn, cell, map);
                    }
                };
                yield return toil;
            }
        }
    }
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "CheckForErrors")]
    internal static class CabinetLoadSelectionSeats
    {
        private static bool Prefix(List<Pawn> pawns, List<CompTransporter> ___transporters, List<TransferableOneWay> ___transferables, ref bool __result)
        {
            if (___transporters.Count == 0 || !___transporters.All(t => t is CompCabinetTransport)) return true;
            AcceptanceReport report = CabinetLoadSelection.Check(___transporters.Cast<CompCabinetTransport>().ToList(), ___transferables);
            if (report && ___transporters.All(t => t.innerContainer.OfType<Pawn>().All(pawns.Contains))) return true;
            CabinetMapJump.Reject(report ? "FAF_NotEnoughSeats".Translate().ToString() : report.Reason);
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "AddToTransferables")]
    internal static class CabinetIndividualPawnSelection
    {
        private static bool Prefix(Thing t, List<CompTransporter> ___transporters, List<TransferableOneWay> ___transferables)
        {
            if (!(t is Pawn) || !___transporters.All(b => b is CompCabinetTransport)) return true;
            // Native grouping can combine identical animals. Each berth needs a concrete pawn reservation instead.
            // 原生会合并同类动物；每个席位必须预约确切Pawn，而非一组可替代动物。
            if (!___transferables.Any(entry => entry.things.Contains(t)))
            {
                TransferableOneWay entry = new TransferableOneWay(); entry.things.Add(t); ___transferables.Add(entry);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.AddToTheToLoadList))]
    internal static class CabinetLoadAssignmentSeats
    {
        private static bool Prefix(CompTransporter __instance, TransferableOneWay t, int count)
        {
            if (!(__instance is CompCabinetTransport bay) || count <= 0) return true;
            if (!(t.AnyThing is Pawn))
            {
                if (CabinetBiologicalPayload.Read(new[] { t.AnyThing }).Corpses.Count == 0) return true;
                List<CompCabinetTransport> candidates = bay.TransportersInGroup(bay.Map).OfType<CompCabinetTransport>().ToList();
                int remaining = count;
                // Native assignment calls this repeatedly for one grouped transferable. Its cleared/rebuilt
                // leftToLoad lists are the allocation ledger; never restart at an already reserved corpse.
                // 原生会为同一分组多次调用本方法；本轮清空后重建的leftToLoad即分配依据，不能反复预约首具遗体。
                foreach (Thing thing in t.things)
                {
                    if (remaining <= 0) break;
                    if (thing.Destroyed) continue;
                    int assigned = candidates.Sum(b => b.leftToLoad == null ? 0 : b.leftToLoad
                        .Where(entry => entry.things.Contains(thing)).Sum(entry => entry.CountToTransfer));
                    int available = Math.Max(0, thing.stackCount - assigned);
                    if (available == 0) continue;
                    CompCabinetTransport actual = candidates.FirstOrDefault(b => b.innerContainer.Contains(thing));
                    CompCabinetTransport destination = actual
                        ?? (CabinetBiologicalPayload.CanAcceptCargo(bay.Cabinet, thing)
                            && CabinetLoadSelection.LeavesSeatsForSelectedPawns(bay, candidates) ? bay : null)
                        ?? candidates.FirstOrDefault(b => CabinetBiologicalPayload.CanAcceptCargo(b.Cabinet, thing)
                            && CabinetLoadSelection.LeavesSeatsForSelectedPawns(b, candidates));
                    if (destination == null) { CabinetMapJump.Reject("FAF_LiveCorpseConflict".Translate()); return false; }
                    if (destination.leftToLoad == null) destination.leftToLoad = new List<TransferableOneWay>();
                    TransferableOneWay reservation = destination.leftToLoad.FirstOrDefault(entry => entry.things.Contains(thing));
                    if (reservation == null)
                    {
                        reservation = new TransferableOneWay(); reservation.things.Add(thing);
                        destination.leftToLoad.Add(reservation);
                    }
                    int take = Math.Min(remaining, available);
                    reservation.AdjustTo(reservation.CountToTransfer + take); remaining -= take;
                }
                return false;
            }
            List<CompCabinetTransport> group = bay.TransportersInGroup(bay.Map).OfType<CompCabinetTransport>().ToList();
            foreach (Pawn pawn in t.things.OfType<Pawn>().Take(count))
            {
                CompCabinetTransport target = group.FirstOrDefault(b => b.innerContainer.Contains(pawn))
                ?? group.FirstOrDefault(b => b.leftToLoad?.Any(x => x.things.Contains(pawn) && x.CountToTransfer > 0) == true)
                ?? (CabinetOccupancy.CanAdmit(bay.Cabinet, pawn) ? bay : null)
                ?? group.FirstOrDefault(b => CabinetOccupancy.CanAdmit(b.Cabinet, pawn));
                if (target == null) { CabinetMapJump.Reject("FAF_NotEnoughSeats".Translate()); return false; }
                if (target.leftToLoad == null) target.leftToLoad = new List<TransferableOneWay>();
                TransferableOneWay entry = target.leftToLoad.FirstOrDefault(e => e.things.Contains(pawn));
                if (entry == null) { entry = new TransferableOneWay(); entry.things.Add(pawn); target.leftToLoad.Add(entry); }
                entry.AdjustTo(1);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new[] { typeof(Map) })]
    internal static class CabinetCancellationKeepsOccupants
    {
        private static bool Prefix(CompTransporter __instance, Map map, ref bool __result)
        {
            if (!(__instance is CompCabinetTransport bay) || bay.Transferring) return true;
            bay.CancelArrangements(map); __result = true; return false;
        }
    }
}
