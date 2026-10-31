// SPDX-License-Identifier: MPL-2.0
using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        public float LaunchCoolantUnits { get { return CoolantTank.Fuel; } }
        public float LaunchCoolantCapacityUnits { get { return CoolantTank.Props.fuelCapacity; } }
        // Resource availability only: the transport owner enforces research, passenger and destination rules.
        // 此处只判断资源；运输负责人另行验证科研、乘员和目的地规则。
        public bool CanRefuelForLaunch { get { return !Destroyed && CoolantBottleBudget() > 0; } }
        // BASIL expands the reserve, not per-unit energy or manoeuvre efficiency.
        // BASIL扩展储液仓，不改变单位工质能量或机动效率。
        public float GetManeuverCoolantCost(float baseUnits) { return baseUnits; }

        /// <summary>Actual native tank units, after the caller obtains the cost once. / 使用调用者已换算一次的真实原生仓单位。</summary>
        public bool TryConsumeLaunchCoolant(float actualUnits)
        {
            if (Destroyed || float.IsNaN(actualUnits) || float.IsInfinity(actualUnits) || actualUnits < 0f
                || CoolantTank.Fuel < actualUnits) return false;
            CoolantTank.ConsumeFuel(actualUnits);
            return true;
        }

        /// <summary>Rollback only; returns the exact consumed units to the same authority, not a second reserve. / 仅供事务回滚；将已扣单位退回同一权威仓，不另设储备。</summary>
        public void RefundLaunchCoolant(float actualUnits)
        {
            if (float.IsNaN(actualUnits) || float.IsInfinity(actualUnits) || actualUnits < 0f)
                throw new ArgumentOutOfRangeException(nameof(actualUnits));
            // Native Refuel takes bottle equivalents, whereas ConsumeFuel takes stored fuel units.
            // 原生Refuel接收瓶数当量，而ConsumeFuel接收仓内燃料单位。
            CoolantTank.Refuel(actualUnits / CoolantTank.Props.FuelMultiplierCurrentDifficulty);
        }

        public void RequestLaunchCoolantRefuel()
        {
            if (!CanManageThermostat()) return;
            if (!CanRefuelForLaunch)
            {
                Messages.Message("FAF_CoolantReserveFull".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (RefuelCoolantFromOwnCargo(int.MaxValue) > 0 || !Spawned) return;
            if (!TryOrderCoolantRefuel(true))
                Messages.Message("FAF_NoCoolantHauler".Translate(), MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>Explicit inventory refuelling consumes real bottles without enabling thermal backup. / 主动库存补液消耗真实瓶装物，不开启温控备用。</summary>
        public int RefuelLaunchCoolantFrom(ThingOwner source, int maxBottles)
        {
            if (source == null || source.Owner is Map || maxBottles <= 0 || !CanRefuelForLaunch) return 0;
            int consumed = 0;
            for (int i = source.Count - 1; i >= 0 && consumed < maxBottles && CoolantBottleBudget() > 0; i--)
            {
                Thing item = source[i];
                if (!IsCoolantBottle(item)) continue;
                int take = Mathf.Min(item.stackCount, Mathf.Min(maxBottles - consumed, CoolantBottleBudget()));
                consumed += ConsumeCoolantBottles(source, item, take);
            }
            return consumed;
        }

        /// <summary>Consume actual bottles within the one target and whole-bottle space. / 按唯一目标及整瓶空间消费实体瓶。</summary>
        private int ConsumeCoolantBottles(ThingOwner source, Thing item, int count)
        {
            if (source == null || item == null || item.Destroyed || item.Spawned || !source.Contains(item)
                || !IsCoolantBottle(item)) return 0;
            count = Mathf.Min(count, CoolantBottleBudget());
            if (count <= 0) return 0;
            Thing bottles = source.Take(item, count);
            int consumed = bottles == null ? 0 : bottles.stackCount;
            if (consumed <= 0) return 0;
            // The float overload receives the bounded count; the native List overload uses a ceiling count.
            // 浮点重载接收已限量瓶数；原生List重载采用向上取整数量。
            CoolantTank.Refuel((float)consumed);
            bottles.Destroy(DestroyMode.Vanish);
            NotifyContentsMassChanged();
            GetComp<FAF.Transport.CompCabinetTransport>().InvalidateCargoMass();
            if (source.Owner is Pawn_InventoryTracker inventory) inventory.pawn.GetCaravan()?.RecacheInventory();
            else if (source.Owner is Building_FlyingFridgeCabinet cabinet && cabinet != this) cabinet.NotifyContentsMassChanged();
            return consumed;
        }
    }
}
