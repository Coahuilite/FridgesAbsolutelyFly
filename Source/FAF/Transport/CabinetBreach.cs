// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace FAF.Transport
{
    // Last-resort physical debris holds only failed local drops, with no freezer, resources or protection.
    // 残骸仅持有本地放置失败的原实体，不保留冷柜、资源或环境保护。
    public sealed class CabinetBreach : Thing, IThingHolder, IThingHolderTickable
    {
        private ThingOwner<Thing> remaining;
        public CabinetBreach() { remaining = new ThingOwner<Thing>(this); }
        public bool ShouldTickContents { get { return false; } }
        protected override int MaxTickIntervalRate { get { return 1; } }
        public ThingOwner GetDirectlyHeldThings() { return remaining; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children, remaining); }
        internal void AcceptRemainders(ThingOwner source)
        {
            source.TryTransferAllToContainer(remaining, false);
        }
        private void Release()
        {
            if (Spawned) remaining.TryDropAll(Position, Map, ThingPlaceMode.Near);
            if (!remaining.Any) base.Destroy();
        }
        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            // One native owner tick; ordinary holder enumeration remains complete for MapPawns/save traversal.
            // 唯一原生owner更新；普通持有遍历仍完整供MapPawns与存档使用。
            remaining.DoTick();
            if (this.IsHashIntervalTick(60, delta)) Release();
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (remaining.Any) { Release(); return; }
            base.Destroy(mode);
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref remaining, "fafBreachRemainder", this); }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            yield return new Command_Action { defaultLabel = "FAF_RetryPodPlacement".Translate(), action = Release };
        }
    }
}
