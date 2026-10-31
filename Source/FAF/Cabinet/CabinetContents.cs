// SPDX-License-Identifier: MPL-2.0
using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        /// <summary>Actual held entities; use TryStore/TryTransferOut for mutations. / 真实持有实体；修改时使用统一存取接口。</summary>
        public ThingOwner Contents { get { return contents; } }
        public Research.CabinetUpgradeProfile UpgradeProfile { get { return Research.CabinetResearch.Current; } }
        public int ItemStackCapacity { get { return UpgradeProfile.CapacityStacks; } }

        /// <summary>Live contents mass, not a second persisted balance. / 实时内容物质量，不另存质量余额。</summary>
        public float ContentsMass
        {
            get { return GetContentsMass(true); }
        }
        internal float GetContentsMass(bool applyPostProcess)
        {
            float mass = 0f;
            foreach (Thing item in contents) mass += item.stackCount * item.GetStatValue(StatDefOf.Mass, applyPostProcess);
            return mass;
        }

        /// <summary>Both physically held compartments, without the hull or external transit siblings. / 两个实际持有仓的原物质量，不含柜壳或外部在途同级载荷。</summary>
        internal float GetHeldMass(bool applyPostProcess)
        {
            float mass = GetContentsMass(applyPostProcess);
            ThingOwner bay = GetComp<FAF.Transport.CompCabinetTransport>().innerContainer;
            if (bay != null)
                foreach (Thing item in bay) mass += item.stackCount * item.GetStatValue(StatDefOf.Mass, applyPostProcess);
            return mass;
        }

        /// <summary>Move physical held/unowned items; never teleport spawned map things. / 转移已持有或无主的实体，不传送地图物品。</summary>
        public int TryStore(Thing item, int count)
        {
            if (count <= 0 || item == null || item.Spawned || contents.Contains(item) || !CanAcceptItem(item)) return 0;
            for (IThingHolder holder = ParentHolder; holder != null; holder = holder.ParentHolder)
                if (ReferenceEquals(holder, item)) return 0;
            int allowed = Math.Min(count, CountCanAccept(item));
            ThingOwner source = item.holdingOwner;
            int before = item.stackCount;
            if (source == null) contents.TryAdd(item, allowed);
            else source.TryTransferToContainer(item, contents, allowed);
            // A fully absorbed native stack can be destroyed; its returned stackCount is then zero.
            // 原生完整合叠会销毁源叠，返回的stackCount可能已是零；以真实源剩余量报告转移数量。
            int remaining = !item.Destroyed && item.holdingOwner == source ? item.stackCount : 0;
            int moved = before - remaining;
            if (moved > 0) NotifyContentsMassChanged();
            return moved;
        }

        /// <summary>Native split/transfer with one physical owner and safe rejection. / 原生拆叠转移；保持唯一实体所有者，拒绝时不吞物。</summary>
        public int TryTransferOut(Thing item, ThingOwner destination, int count)
        {
            if (count <= 0 || item == null || destination == null || destination == contents
                || !contents.Contains(item)) return 0;
            // Never create a holder cycle by moving an item into itself or its descendants.
            // 禁止将物品放进自身或其后代容器，避免持有环。
            for (IThingHolder holder = destination.Owner; holder != null; holder = holder.ParentHolder)
                if (ReferenceEquals(holder, item)) return 0;
            Building_FlyingFridgeCabinet otherCabinet = destination.Owner as Building_FlyingFridgeCabinet;
            if (otherCabinet != null) return otherCabinet.TryStore(item, count);
            int before = item.stackCount;
            contents.TryTransferToContainer(item, destination, Math.Min(count, before));
            int remaining = !item.Destroyed && item.holdingOwner == contents ? item.stackCount : 0;
            int moved = before - remaining;
            if (moved > 0) NotifyContentsMassChanged();
            return moved;
        }

        internal void NotifyContentsMassChanged()
        {
            // Native mass queries read our stat directly; invalidate its caravan's movement/food caches too.
            // 原生质量查询直接读取本柜属性；同时使所属远行队的移动与食物缓存失效。
            for (IThingHolder holder = this; holder != null; holder = holder.ParentHolder)
                if (holder is Thing thing) StatDefOf.Mass.Worker.ClearCacheForThing(thing);
            this.GetCaravan()?.RecacheInventory();
            GetComp<FAF.Transport.CompCabinetTransport>().RefreshLoadingCapacity();
        }

        public bool AllowsTemperatureBasedRecovery
        {
            get { return InternalTemperature < TemperatureControl.ThermalProps.icyCoreMaximumTemperature; }
        }
    }
}
