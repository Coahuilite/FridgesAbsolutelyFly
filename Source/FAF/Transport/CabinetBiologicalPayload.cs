// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    // A read of the actual inward holder tree, never a saved passenger/corpse register.
    // 只读取真实向内持有树，不保存第二份乘员或遗体名册。
    internal sealed class CabinetBiologicalPayload
    {
        internal readonly HashSet<Pawn> Living = new HashSet<Pawn>();
        internal readonly HashSet<Corpse> Corpses = new HashSet<Corpse>();
        internal readonly HashSet<Pawn> UncontainedDead = new HashSet<Pawn>();
        private readonly HashSet<Thing> things = new HashSet<Thing>();
        private readonly HashSet<IThingHolder> holders = new HashSet<IThingHolder>();
        private ISet<Thing> excluded;
        internal bool Conflict { get { return Living.Count > 1 || Living.Count > 0 && Corpses.Count > 0 || UncontainedDead.Count > 0; } }
        internal static CabinetBiologicalPayload Read(IEnumerable<Thing> roots, ISet<Thing> excluded = null)
        {
            CabinetBiologicalPayload result = new CabinetBiologicalPayload { excluded = excluded };
            foreach (Thing root in roots) result.Visit(root);
            return result;
        }
        private void Visit(Thing thing)
        {
            if (thing == null || thing.Destroyed || excluded?.Contains(thing) == true || !things.Add(thing)) return;
            if (thing is Corpse corpse) Corpses.Add(corpse);
            if (thing is Pawn pawn)
            {
                if (!pawn.Dead) Living.Add(pawn);
                else if (!(pawn.ParentHolder is Corpse)) UncontainedDead.Add(pawn);
            }
            if (thing is IThingHolder holder) VisitHolder(holder);
        }
        private void VisitHolder(IThingHolder holder)
        {
            if (holder == null || !holders.Add(holder)) return;
            ThingOwner direct = holder.GetDirectlyHeldThings();
            if (direct != null) foreach (Thing thing in direct) Visit(thing);
            List<IThingHolder> children = new List<IThingHolder>(); holder.GetChildHolders(children);
            foreach (IThingHolder child in children)
            {
                if (child is Thing childThing) { Visit(childThing); continue; }
                if (child is ThingComp comp && excluded?.Contains(comp.parent) == true) continue;
                VisitHolder(child);
            }
        }
        internal static IEnumerable<Thing> Roots(Building_FlyingFridgeCabinet cabinet, ActiveTransporterInfo info = null)
        {
            IEnumerable<Thing> result = cabinet.Contents.Concat(cabinet.GetComp<CompCabinetTransport>().innerContainer);
            return info == null ? result : result.Concat(info.innerContainer.Where(t => t.GetInnerIfMinified() != cabinet));
        }
        internal static bool PendingCorpses(CompCabinetTransport bay)
        {
            return bay.leftToLoad != null && Read(bay.leftToLoad.Where(t => t.CountToTransfer > 0).SelectMany(t => t.things)).Corpses.Count > 0;
        }
        internal static AcceptanceReport CanAcceptCargo(Building_FlyingFridgeCabinet cabinet, Thing item)
        {
            CompCabinetTransport bay = cabinet.GetComp<CompCabinetTransport>();
            if (bay.Transferring || CabinetDeathHandoff.IsReplacement(cabinet, item)) return true;
            CabinetBiologicalPayload incoming = Read(new[] { item });
            if (incoming.Living.Count > 0) return "FAF_UnpackPassengers".Translate();
            if (incoming.UncontainedDead.Count > 0) return "FAF_InvalidBoardedPassenger".Translate();
            if (incoming.Corpses.Count == 0) return true;
            CabinetOccupancySnapshot state = CabinetOccupancy.Read(cabinet);
            return state.ActualOccupants.Count == 0 && state.PendingBoarders.Count == 0 && !state.RestPreparationActive
                ? AcceptanceReport.WasAccepted : "FAF_LiveCorpseConflict".Translate();
        }
    }
}
