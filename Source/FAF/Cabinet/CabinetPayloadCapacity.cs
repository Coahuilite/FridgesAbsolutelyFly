// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Transport;
using RimWorld;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Read the net payload and native reservations; never save another mass balance. / 读取不含本柜壳的净载荷与原生预约，不保存第二份质量余额。</summary>
    internal static class CabinetPayloadCapacity
    {
        internal static float Limit { get { return CabinetTravelDef.Current.massCapacity; } }

        internal static float Mass(Thing item)
        {
            return item == null || item.Destroyed ? 0f : Math.Max(0f, item.GetStatValue(StatDefOf.Mass));
        }

        internal static ActiveTransporterInfo Payload(Building_FlyingFridgeCabinet cabinet)
        {
            ActiveTransporterInfo info = (cabinet.ParentHolder as MinifiedThing)?.ParentHolder as ActiveTransporterInfo;
            return CabinetPayload.CabinetIn(info) == cabinet ? info : null;
        }

        internal static List<Pawn> Sleepers(Building_FlyingFridgeCabinet cabinet)
        {
            var bed = CabinetOccupancy.ProxyFor(cabinet);
            return cabinet.Spawned && bed?.Spawned == true
                ? bed.CurOccupants.Where(pawn => pawn.CurrentBed() == bed).Distinct().ToList() : new List<Pawn>();
        }

        internal static float PayloadMass(Building_FlyingFridgeCabinet cabinet)
        {
            ActiveTransporterInfo info = Payload(cabinet);
            float mass = cabinet.GetHeldMass(true);
            // The engine already supports this hull. Other real payload roots keep their entire native mass.
            // 引擎已经承托本柜壳；其他真实载荷根仍按完整原生质量计入，不能连内容物一起减重。
            if (info != null)
                foreach (Thing item in info.innerContainer)
                    if (item.GetInnerIfMinified() != cabinet) mass += Mass(item) * item.stackCount;
            ThingOwner bay = cabinet.GetComp<CompCabinetTransport>().innerContainer;
            foreach (Pawn pawn in Sleepers(cabinet))
                if (!bay.Contains(pawn) && info?.innerContainer.Contains(pawn) != true) mass += Mass(pawn);
            return mass;
        }

        internal static bool AlreadyCounted(Building_FlyingFridgeCabinet cabinet, Thing item)
        {
            ActiveTransporterInfo info = Payload(cabinet);
            for (IThingHolder holder = item as IThingHolder ?? item?.ParentHolder; holder != null; holder = holder.ParentHolder)
            {
                if (holder == cabinet || holder is CompCabinetTransport bay && bay.parent == cabinet
                    || info != null && holder == info) return true;
                if (holder is Pawn pawn && (pawn.CurrentBed() as NivarianSleepInFridges.Building_FridgeBedProxy)?.ParentFridge == cabinet)
                    return true;
            }
            return false;
        }

        internal static Pawn PreparingPawn(Building_FlyingFridgeCabinet cabinet)
        {
            Pawn pawn = cabinet.GetComp<CompCabinetTransport>().RestPreparationTarget;
            // Read the actual job rather than IsValid: that native bed query itself consumes this admission rule.
            // 读取实际任务而非IsValid；后者的原生床资格查询本身也会使用本准入规则，不能递归。
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.CurJob?.def.defName == "FAF_PrepareCabinetRest"
                && pawn.CurJob.targetA.Thing == cabinet ? pawn : null;
        }

        internal static float ReservedMass(Building_FlyingFridgeCabinet cabinet, Thing incoming = null)
        {
            float mass = 0f;
            Pawn preparing = PreparingPawn(cabinet);
            if (preparing != null && preparing != incoming && !AlreadyCounted(cabinet, preparing)) mass += Mass(preparing);
            List<TransferableOneWay> pending = cabinet.GetComp<CompCabinetTransport>().leftToLoad;
            if (pending == null) return mass;
            foreach (TransferableOneWay entry in pending)
            {
                if (!entry.HasAnyThing || entry.CountToTransfer <= 0 || incoming != null
                    && TransferableUtility.TransferAsOne(entry.AnyThing, incoming, TransferAsOneMode.PodsOrCaravanPacking)) continue;
                int remaining = entry.CountToTransfer;
                foreach (Thing item in entry.things)
                {
                    if (remaining <= 0) break;
                    if (item == null || item.Destroyed) continue;
                    int count = Math.Min(remaining, item.stackCount); remaining -= count;
                    if (item != preparing && !AlreadyCounted(cabinet, item)) mass += Mass(item) * count;
                }
            }
            return mass;
        }

        internal static int CountCanAdd(Building_FlyingFridgeCabinet cabinet, Thing item, int count)
        {
            if (item == null || item.Destroyed || count <= 0 || item.GetInnerIfMinified() == cabinet) return 0;
            // A same-cabinet move adds no load, including rearrangement of a preserved overweight save.
            // 同柜移交不新增质量；旧超重存档也可整理已有实物，不会因再次计重被锁死。
            if (AlreadyCounted(cabinet, item) || Integration.CabinetCapacityTransfer.PreservesExisting(cabinet, item)) return count;
            float current = PayloadMass(cabinet);
            if (current > Limit) return 0;
            return CountWithinMass(item, count, Limit - current - ReservedMass(cabinet, item));
        }

        internal static int CountWithinMass(Thing item, int count, float freeMass)
        {
            if (freeMass < 0f || float.IsNaN(freeMass)) return 0;
            float unit = Mass(item);
            if (float.IsNaN(unit) || float.IsInfinity(unit)) return 0;
            if (unit <= 0f) return count;
            return Math.Min(count, Math.Max(0, (int)Math.Min(int.MaxValue, Math.Floor((freeMass + 0.00001f) / unit))));
        }

        internal static bool CanInstallAfterClearing(Building_FlyingFridgeCabinet cabinet, Thing mattress)
        {
            return CountWithinMass(mattress, 1, Limit - PayloadMass(cabinet) + cabinet.ContentsMass
                - ReservedMass(cabinet, mattress)) == 1;
        }

        internal static Building_FlyingFridgeCabinet CabinetFor(ThingOwner owner)
        {
            return owner?.Owner as Building_FlyingFridgeCabinet ?? (owner?.Owner as CompCabinetTransport)?.Cabinet;
        }

        internal static bool PreservingOriginal(Building_FlyingFridgeCabinet cabinet, Thing item)
        {
            return cabinet.GetComp<CompCabinetTransport>().Transferring || CabinetDeathHandoff.IsReplacement(cabinet, item);
        }
    }
}
