// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    // A short-lived allocation of actual entities/counts, not an inventory or a saved manifest.
    // 仅分配真实实体及数量的短期计划，不是另一份库存或持久manifest。
    internal sealed class CabinetManifest
    {
        internal sealed class Piece
        {
            internal Thing Thing;
            internal ThingOwner Origin;
            internal int Count, Slot;
        }
        internal readonly List<Building_FlyingFridgeCabinet> Cabinets;
        internal readonly List<ActiveTransporterInfo> Pods;
        internal readonly List<Piece> Pieces = new List<Piece>();
        internal readonly List<Pawn> Passengers;
        private readonly float[] freeMass;
        internal float PlannedMass(int slot) { return CabinetTravelDef.Current.massCapacity - freeMass[slot]; }

        private CabinetManifest(List<Building_FlyingFridgeCabinet> cabinets, List<Pawn> pawns)
        {
            Cabinets = cabinets; Passengers = pawns;
            Pods = cabinets.Select(cabinet => new ActiveTransporterInfo { sentTransporterDef = cabinet.def, leaveSlag = false }).ToList();
            freeMass = cabinets.Select(cabinet => CabinetTravelDef.Current.massCapacity - cabinet.ContentsMass).ToArray();
        }
        internal static CabinetManifest Plan(List<Building_FlyingFridgeCabinet> cabinets, List<Pawn> pawns,
            List<Thing> cargo, bool unloadInventory, out string reason)
        {
            reason = null;
            List<Thing> locked = cabinets.SelectMany(cab => cab.GetComp<CompCabinetTransport>().innerContainer).ToList();
            pawns = pawns.Concat(locked.OfType<Pawn>()).Distinct().ToList();
            if (cabinets.Count == 0 || pawns.Count > cabinets.Count || pawns.Any(pawn => pawn.Dead || pawn.Destroyed))
            { reason = "FAF_NotEnoughSeats".Translate(); return null; }
            if (HasNestedLivingPawn(pawns.Cast<Thing>().Concat(cargo).Concat(locked).Concat(cabinets.SelectMany(cabinet => cabinet.Contents)),
                new HashSet<Pawn>(locked.OfType<Pawn>())))
            { reason = "FAF_UnpackPassengers".Translate(); return null; }
            CabinetManifest result = new CabinetManifest(cabinets, pawns);
            if (result.freeMass.Any(free => free < 0f)) { reason = "FAF_OverMass".Translate(); return null; }
            HashSet<int> occupied = new HashSet<int>();
            HashSet<int> corpseSlots = new HashSet<int>();
            HashSet<Thing> unloaded = unloadInventory ? new HashSet<Thing>(cargo.Concat(cabinets.Select(cab => (Thing)cab.ParentHolder))) : null;
            // Loaded entities are commitments to their actual cabinet, not a pool to optimise across bays.
            // 已登舱实体固定属于原柜，不作为可在柜间重新优化分配的虚拟池。
            for (int slot = 0; slot < cabinets.Count; slot++)
            {
                ThingOwner owner = cabinets[slot].GetComp<CompCabinetTransport>().innerContainer;
                if (CabinetBiologicalPayload.Read(cabinets[slot].Contents.Concat(owner), unloaded).Corpses.Count > 0) corpseSlots.Add(slot);
                if (owner.OfType<Pawn>().Count() > 1) { reason = "FAF_NotEnoughSeats".Translate(); return null; }
                foreach (Thing item in owner)
                {
                    if (item is Pawn) occupied.Add(slot);
                    float mass = item is Pawn resident ? BodyMass(resident, false) : item.GetStatValue(StatDefOf.Mass) * item.stackCount;
                    result.freeMass[slot] -= mass;
                    result.Pieces.Add(new Piece { Thing = item, Origin = owner, Count = item.stackCount, Slot = slot });
                }
            }
            if (result.freeMass.Any(free => free < 0f)) { reason = "FAF_OverMass".Translate(); return null; }
            List<Thing> corpseCargo = cargo.Except(locked).Distinct()
                .Where(item => CabinetBiologicalPayload.Read(new[] { item }).Corpses.Count > 0).ToList();
            List<Thing> biological = pawns.Except(locked.OfType<Pawn>()).Cast<Thing>().Concat(corpseCargo).ToList();
            int remainingPassengers = biological.OfType<Pawn>().Count();
            // Allocate constrained bodies together, reserving a real empty seat for each remaining live pawn.
            // 同时分配受约束的活体与遗体，为每名剩余活人保留真实空席，不以源库存关系误判最终分柜。
            foreach (Thing item in biological.OrderByDescending(item => item is Pawn pawn ? BodyMass(pawn, unloadInventory)
                : item.GetStatValue(StatDefOf.Mass) * item.stackCount))
            {
                bool living = item is Pawn;
                float mass = living ? BodyMass((Pawn)item, unloadInventory) : item.GetStatValue(StatDefOf.Mass) * item.stackCount;
                int emptySeats = Enumerable.Range(0, cabinets.Count).Count(index => !occupied.Contains(index) && !corpseSlots.Contains(index));
                List<int> eligible = Enumerable.Range(0, cabinets.Count).Where(index => !occupied.Contains(index)
                    && (living ? !corpseSlots.Contains(index) : corpseSlots.Contains(index) || emptySeats > remainingPassengers)).ToList();
                if (eligible.Count == 0) { reason = "FAF_LiveCorpseConflict".Translate(); return null; }
                int slot = eligible.Where(index => result.freeMass[index] >= mass)
                    .OrderBy(index => !living && !corpseSlots.Contains(index)).ThenBy(index => result.freeMass[index]).DefaultIfEmpty(-1).First();
                if (slot < 0) { reason = "FAF_OverMass".Translate(); return null; }
                if (living) { occupied.Add(slot); remainingPassengers--; } else corpseSlots.Add(slot);
                result.freeMass[slot] -= mass;
                result.Pieces.Add(new Piece { Thing = item, Origin = item.holdingOwner, Count = item.stackCount, Slot = slot });
            }
            foreach (Thing item in cargo.Except(locked).Except(corpseCargo).Distinct().OrderByDescending(item => item.GetStatValue(StatDefOf.Mass)))
            {
                int remaining = item.stackCount;
                float unitMass = Mathf.Max(0f, item.GetStatValue(StatDefOf.Mass));
                foreach (int slot in Enumerable.Range(0, cabinets.Count).OrderByDescending(index => result.freeMass[index]))
                {
                    int count = unitMass > 0f ? Mathf.Min(remaining, Mathf.FloorToInt((result.freeMass[slot] + 0.00001f) / unitMass)) : remaining;
                    if (count <= 0) continue;
                    result.Pieces.Add(new Piece { Thing = item, Origin = item.holdingOwner, Count = count, Slot = slot });
                    result.freeMass[slot] -= count * unitMass; remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining > 0) { reason = "FAF_OverMass".Translate(); return null; }
            }
            for (int slot = 0; slot < cabinets.Count; slot++)
            {
                CabinetBiologicalPayload biology = CabinetBiologicalPayload.Read(cabinets[slot].Contents);
                foreach (Piece piece in result.Pieces.Where(piece => piece.Slot == slot))
                {
                    CabinetBiologicalPayload part = CabinetBiologicalPayload.Read(new[] { piece.Thing }, piece.Thing is Pawn ? unloaded : null);
                    biology.Living.UnionWith(part.Living); biology.Corpses.UnionWith(part.Corpses); biology.UncontainedDead.UnionWith(part.UncontainedDead);
                }
                if (biology.Conflict) { reason = "FAF_LiveCorpseConflict".Translate(); return null; }
            }
            if (result.Pieces.Any(piece => piece.Origin == null)) { reason = "FAF_ManifestChanged".Translate(); return null; }
            return result;
        }
        internal static bool HasNestedLivingPawn(IEnumerable<Thing> roots, ISet<Pawn> assignedResidents = null)
        {
            // Do not bypass the real one-person berth with a carried child or an occupied cargo casket.
            // 不能用抱着的孩子或载人货物容器绕过真实的一人载位；先让每人分别登舱。
            foreach (Thing root in roots)
                if (root is IThingHolder holder && ThingOwnerUtility.GetAllThingsRecursively(holder)
                    .OfType<Pawn>().Any(pawn => !pawn.Dead && assignedResidents?.Contains(pawn) != true)) return true;
            return false;
        }
        private static float BodyMass(Pawn pawn, bool unloadInventory)
        {
            return Mathf.Max(0f, pawn.GetStatValue(StatDefOf.Mass) - (unloadInventory && pawn.inventory != null ? MassUtility.InventoryMass(pawn) : 0f));
        }
    }

    internal sealed class CabinetTransferTransaction
    {
        private sealed class Move
        {
            internal Thing Item;
            internal ThingOwner Origin, Destination;
        }
        private readonly List<Move> moved = new List<Move>();
        internal void Transfer(Thing item, ThingOwner origin, ThingOwner destination, int count)
        {
            if (item == null || origin == null || !origin.Contains(item) || count <= 0 || count > item.stackCount)
                throw new InvalidOperationException("Transport payload ownership/count changed before commit.");
            int transferred = origin.TryTransferToContainer(item, destination, count, out Thing actual, canMergeWithExistingStacks: false);
            if (transferred > 0) moved.Add(new Move { Item = actual, Origin = origin, Destination = destination });
            if (transferred != count) throw new InvalidOperationException("Native ThingOwner refused the full transport payload.");
        }
        internal bool Rollback()
        {
            // Reverse real native moves before releasing any temporary native transport holders.
            // 先逆序回滚原生实体转移，再释放临时运输持有者；绝不destroyLeftover吞物。
            bool complete = true;
            for (int index = moved.Count - 1; index >= 0; index--)
            {
                Move move = moved[index];
                try
                {
                    if (!move.Destination.Contains(move.Item)) throw new InvalidOperationException("Transport rollback lost its expected entity owner.");
                    int count = move.Item.stackCount;
                    move.Destination.TryTransferToContainer(move.Item, move.Origin, count, canMergeWithExistingStacks: true);
                    // A full native merge may destroy the source and return zero; read the physical source delta.
                    // 原生完整合叠可销毁源叠并返回零；以真实源owner的数量差确认回滚。
                    if (move.Destination.Contains(move.Item)) throw new InvalidOperationException("Native owner refused full transport rollback.");
                }
                catch (Exception exception)
                {
                    complete = false;
                    Log.Error("[F.A.F.] Original owner refused rollback; remaining real payload will be retained for recovery.\n" + exception);
                }
            }
            moved.Clear();
            return complete;
        }
    }
}
