// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    /// <summary>A temporary allocation of selected real counts, not an inventory or saved manifest. / 所选实物数量的临时分配，不是库存或保存的清单。</summary>
    internal sealed class CabinetLoadingCapacity
    {
        internal sealed class Assignment
        {
            internal Thing Item;
            internal int Count, Slot;
        }
        internal readonly List<Assignment> Assignments = new List<Assignment>();
        private readonly List<CompCabinetTransport> bays;
        private readonly float[] free;
        private readonly HashSet<Pawn>[] residents;
        private readonly bool[] corpses;
        private readonly List<Thing>[] counted;

        private CabinetLoadingCapacity(List<CompCabinetTransport> bays, bool instant)
        {
            this.bays = bays;
            free = new float[bays.Count]; residents = new HashSet<Pawn>[bays.Count];
            corpses = new bool[bays.Count]; counted = new List<Thing>[bays.Count];
            for (int slot = 0; slot < bays.Count; slot++)
            {
                Building_FlyingFridgeCabinet cabinet = bays[slot].Cabinet;
                counted[slot] = cabinet.Contents.ToList();
                counted[slot].AddRange(CabinetPayloadCapacity.Sleepers(cabinet));
                free[slot] = CabinetPayloadCapacity.Limit - cabinet.ContentsMass
                    - CabinetPayloadCapacity.Sleepers(cabinet).Sum(pawn => CabinetPayloadCapacity.Mass(pawn));
                if (instant)
                {
                    counted[slot].AddRange(bays[slot].innerContainer);
                    free[slot] -= CollectionsMassCalculator.MassUsage(bays[slot].innerContainer,
                        IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
                }
                CabinetOccupancySnapshot state = CabinetOccupancy.Read(cabinet);
                residents[slot] = new HashSet<Pawn>(state.ActualOccupants.Concat(state.PendingBoarders));
                if (state.RestPreparationPawn != null) residents[slot].Add(state.RestPreparationPawn);
                corpses[slot] = state.AssociatedCorpses.Count > 0;
            }
        }

        internal static List<Assignment> Selected(List<TransferableOneWay> transferables,
            TransferableOneWay changed = null, int destination = 0)
        {
            List<Assignment> selected = new List<Assignment>();
            int index = 0;
            foreach (TransferableOneWay entry in transferables)
            {
                int remaining = entry == changed ? destination : entry.CountToTransfer;
                foreach (Thing item in entry.things)
                {
                    if (remaining <= 0) break;
                    if (item == null || item.Destroyed) continue;
                    int count = Math.Min(remaining, item.stackCount); remaining -= count;
                    selected.Add(new Assignment { Item = item, Count = count, Slot = index });
                }
                index++;
            }
            return selected;
        }

        internal static AcceptanceReport Plan(List<CompCabinetTransport> bays, List<TransferableOneWay> transferables,
            out CabinetLoadingCapacity result, TransferableOneWay changed = null, int destination = 0, bool instant = false)
        {
            result = new CabinetLoadingCapacity(bays, instant);
            CabinetLoadingCapacity plan = result;
            List<Assignment> selected = Selected(transferables, changed, destination);
            HashSet<Thing> represented = new HashSet<Thing>(transferables.SelectMany(entry => entry.things));
            HashSet<Thing> chosen = new HashSet<Thing>(selected.Select(piece => piece.Item));
            for (int slot = 0; slot < bays.Count; slot++)
            {
                // A represented deselection releases a pending order, never an already held resident.
                // 本窗口的减选可撤销待办安排，但不能凭空移除实际住客。
                result.residents[slot].RemoveWhere(pawn => represented.Contains(pawn) && !chosen.Contains(pawn)
                    && !bays[slot].innerContainer.Contains(pawn) && !plan.counted[slot].Contains(pawn)
                    && CabinetPayloadCapacity.PreparingPawn(bays[slot].Cabinet) != pawn);
                foreach (Pawn pawn in result.residents[slot])
                    if (!chosen.Contains(pawn) && !bays[slot].innerContainer.Contains(pawn) && !result.Covered(slot, pawn))
                    { result.free[slot] -= CabinetPayloadCapacity.Mass(pawn); result.counted[slot].Add(pawn); }
            }
            // Keep already loaded entities assigned to their original cabinet. Deselecting them uses native drops below.
            // 已装实物固定属于原柜；减选沿下方原生落物路径处理。
            foreach (Assignment piece in selected.Where(piece => bays.Any(bay => bay.innerContainer.Contains(piece.Item))))
            {
                int slot = bays.FindIndex(bay => bay.innerContainer.Contains(piece.Item));
                if (instant && piece.Slot % bays.Count != slot) return "FAF_OverMass".Translate();
                result.Add(piece, slot);
            }
            foreach (Assignment piece in selected.Where(piece => !bays.Any(bay => bay.innerContainer.Contains(piece.Item)))
                .OrderBy(piece => piece.Item is Pawn ? 0 : CabinetBiologicalPayload.Read(new[] { piece.Item }).Corpses.Count > 0 ? 1 : 2)
                .ThenByDescending(piece => CabinetPayloadCapacity.Mass(piece.Item)))
            {
                int remaining = piece.Count;
                IEnumerable<int> slots = instant ? new[] { piece.Slot % bays.Count }
                    : Enumerable.Range(0, bays.Count).OrderBy(slot => plan.free[slot]).ToList();
                foreach (int slot in slots)
                {
                    if (!result.Eligible(slot, piece.Item)) continue;
                    int count = result.Covered(slot, piece.Item) ? remaining
                        : CabinetPayloadCapacity.CountWithinMass(piece.Item, remaining, result.free[slot]);
                    if (piece.Item is Pawn && count < 1) continue;
                    if (count <= 0) continue;
                    result.Add(new Assignment { Item = piece.Item, Count = count }, slot);
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining > 0) return "FAF_OverMass".Translate();
            }
            return true;
        }

        private bool Covered(int slot, Thing item)
        {
            for (IThingHolder holder = item as IThingHolder ?? item.ParentHolder; holder != null; holder = holder.ParentHolder)
                if (holder is Thing thing && counted[slot].Contains(thing)) return true;
            return counted[slot].Contains(item);
        }
        private bool Eligible(int slot, Thing item)
        {
            Building_FlyingFridgeCabinet cabinet = bays[slot].Cabinet;
            if (!cabinet.Spawned || cabinet.Destroyed || bays[slot].Transferring) return false;
            int fixedSlot = Enumerable.Range(0, bays.Count).Where(index => Covered(index, item)
                || CabinetPayloadCapacity.AlreadyCounted(bays[index].Cabinet, item)).DefaultIfEmpty(-1).First();
            if (fixedSlot >= 0 && fixedSlot != slot) return false;
            if (item is Pawn pawn)
                return cabinet.UpgradeProfile.WorldLaunchUnlocked && !corpses[slot]
                    && residents[slot].All(resident => resident == pawn)
                    && !Enumerable.Range(0, bays.Count).Any(index => index != slot && residents[index].Contains(pawn));
            return CabinetBiologicalPayload.Read(new[] { item }).Corpses.Count == 0 || residents[slot].Count == 0;
        }
        private void Add(Assignment piece, int slot)
        {
            if (!Covered(slot, piece.Item)) free[slot] -= CabinetPayloadCapacity.Mass(piece.Item) * piece.Count;
            if (piece.Item is Pawn pawn) residents[slot].Add(pawn);
            if (CabinetBiologicalPayload.Read(new[] { piece.Item }).Corpses.Count > 0) corpses[slot] = true;
            counted[slot].Add(piece.Item);
            Assignments.Add(new Assignment { Item = piece.Item, Count = piece.Count, Slot = slot });
        }

        internal void Apply()
        {
            // Only native pending lists are replaced. No item is split or moved while making the plan.
            // 只重建原生待装列表；计划阶段不拆叠、不转移实物。
            foreach (CompCabinetTransport bay in bays)
            { if (bay.leftToLoad == null) bay.leftToLoad = new List<TransferableOneWay>(); else bay.leftToLoad.Clear(); }
            foreach (Assignment piece in Assignments)
            {
                TransferableOneWay entry = new TransferableOneWay(); entry.things.Add(piece.Item); entry.AdjustTo(piece.Count);
                bays[piece.Slot].leftToLoad.Add(entry);
            }
            foreach (CompCabinetTransport bay in bays)
                foreach (Thing item in bay.innerContainer.ToList())
                {
                    int retained = bay.SubtractFromToLoadList(item, item.stackCount, sendMessageOnFinished: false);
                    if (retained < item.stackCount
                        && !bay.innerContainer.TryDrop(item, ThingPlaceMode.Near, item.stackCount - retained, out Thing _))
                        CabinetMapJump.Reject("FAF_StorageRejected".Translate());
                    bay.InvalidateCargoMass(); bay.RefreshLoadingCapacity();
                }
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), "AssignTransferablesToRandomTransporters")]
    internal static class CabinetAssignByRealCapacity
    {
        private static bool Prefix(List<CompTransporter> ___transporters, List<TransferableOneWay> ___transferables)
        {
            if (___transporters.Count == 0 || !___transporters.All(bay => bay is CompCabinetTransport)) return true;
            AcceptanceReport report = CabinetLoadingCapacity.Plan(___transporters.Cast<CompCabinetTransport>().ToList(),
                ___transferables, out CabinetLoadingCapacity plan);
            if (report) plan.Apply(); else CabinetMapJump.Reject(report.Reason);
            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), "DebugTryLoadInstantly")]
    internal static class CabinetSafeInstantLoading
    {
        private static bool Prefix(List<CompTransporter> ___transporters, List<TransferableOneWay> ___transferables, ref bool __result)
        {
            if (!___transporters.Any(bay => bay is CompCabinetTransport)) return true;
            if (!___transporters.All(bay => bay is CompCabinetTransport))
            { __result = false; CabinetMapJump.Reject("FAF_InvalidCabinet".Translate()); return false; }
            List<CompCabinetTransport> bays = ___transporters.Cast<CompCabinetTransport>().ToList();
            AcceptanceReport report = CabinetLoadSelection.Check(bays, ___transferables);
            if (report) report = CabinetLoadingCapacity.Plan(bays, ___transferables, out _, instant: true);
            if (!report) { __result = false; CabinetMapJump.Reject(report.Reason); return false; }
            TransporterUtility.InitiateLoading(___transporters);
            __result = true;
            foreach (CabinetLoadingCapacity.Assignment piece in CabinetLoadingCapacity.Selected(___transferables))
            {
                ThingOwner destination = bays[piece.Slot % bays.Count].innerContainer;
                if (destination.Contains(piece.Item)) continue;
                if (!Move(piece.Item, piece.Count, destination, bays[0].parent.Map))
                { __result = false; CabinetMapJump.Reject("FAF_StorageRejected".Translate()); break; }
            }
            return false;
        }

        private static bool Move(Thing item, int count, ThingOwner destination, Map map)
        {
            // Native transfers keep rejected held items in their original owner, including partial stacks.
            // 原生转移会把未接收的持有物保留在原owner，包含部分堆叠。
            if (item.holdingOwner != null)
            {
                ThingOwner source = item.holdingOwner;
                int before = item.stackCount;
                source.TryTransferToContainer(item, destination, count);
                int remaining = !item.Destroyed && source.Contains(item) ? item.stackCount : 0;
                return before - remaining == count;
            }
            if (!item.Spawned || item.Map != map || destination.GetCountCanAccept(item) < count) return false;
            IntVec3 cell = item.Position; Rot4 rotation = item.Rotation;
            Thing piece = item.SplitOff(count);
            if (destination.TryAdd(piece)) return true;
            // SplitOff may despawn a whole stack. On rejection merge back, place the same entity, or retain it
            // in the existing recovery holder if even map placement fails; never discard an unowned remainder.
            // 整叠SplitOff会取消生成；拒绝后合回、放回原物，极端放置失败则用现有回收持有者保留，绝不吞余物。
            if (piece != item && !item.Destroyed && item.TryAbsorbStack(piece, respectStackLimit: false)) return false;
            piece.Rotation = rotation;
            if (!piece.Destroyed && !piece.Spawned && piece.holdingOwner == null
                && !GenPlace.TryPlaceThing(piece, cell, map, ThingPlaceMode.Near))
            {
                ActiveTransporterInfo retained = new ActiveTransporterInfo();
                retained.innerContainer.TryAdd(piece, false);
                CabinetRecoverySite.Create(map.Tile, new[] { retained }).FinishDelivery();
            }
            return false;
        }
    }
}
