// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using FAF.Transport;
using RimWorld;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        public AcceptanceReport CanManageThermostat()
        {
            // Thermal settings remain operable during a valid journey; cargo transfer has its separate policy.
            // 有效航程中仍可操作温控设置；货物转移另有权限规则。
            return FAF.Transport.CabinetOccupancy.CanReadCabinet(this)
                ? AcceptanceReport.WasAccepted : "FAF_CabinetUnavailable".Translate();
        }
        public AcceptanceReport CanRefuelFromContents()
        {
            AcceptanceReport access = CanManageThermostat();
            if (!access) return access;
            if (!coolantBackupEnabled) return "FAF_CoolantBackupDisabled".Translate();
            if (!CanRefuelForLaunch) return "FAF_CoolantReserveFull".Translate();
            return true;
        }
        public int RefuelThermalCoolantFromContents()
        {
            return CanRefuelFromContents() ? RefuelCoolantFromOwnCargo(int.MaxValue) : 0;
        }
        /// <summary>Use only this cabinet's physical cargo, in a stable order; never another pawn's supplies. / 按固定顺序仅使用本柜实体货物，不取其他角色的补给。</summary>
        public int RefuelCoolantFromOwnCargo(int maxBottles)
        {
            if (maxBottles <= 0 || !CanManageThermostat() || !CanRefuelForLaunch) return 0;
            int consumed = 0;
            foreach (ThingOwner source in OwnCoolantCargoSources())
            {
                consumed += RefuelLaunchCoolantFrom(source, maxBottles - consumed);
                if (consumed == maxBottles || !CanRefuelForLaunch) break;
            }
            return consumed;
        }
        // Shared physical sources only: each caller owns its eligibility, budget and stopping policy.
        // 只共享实际来源；资格、预算及停止规则由各调用者分别负责。
        private IEnumerable<ThingOwner> OwnCoolantCargoSources()
        {
            yield return contents;
            yield return GetComp<CompCabinetTransport>().innerContainer;
            MinifiedThing wrapper = ParentHolder as MinifiedThing;
            ActiveTransporterInfo payload = wrapper?.ParentHolder as ActiveTransporterInfo;
            // Only direct cargo of a uniquely associated payload belongs to this cabinet. No recursive inventory scan.
            // 仅唯一对应本柜的载荷之直接货物归本柜使用；不递归扫描任何角色库存。
            if (payload != null && wrapper.InnerThing == this && payload.innerContainer.Contains(wrapper)
                && CabinetPayload.CabinetIn(payload) == this)
                yield return payload.innerContainer;
        }

        internal bool HasOwnCoolantBottles()
        {
            foreach (ThingOwner source in OwnCoolantCargoSources())
                foreach (Thing item in source)
                    if (IsCoolantBottle(item)) return true;
            return false;
        }
        public AcceptanceReport CanUseDevelopmentResources()
        {
            if (!Prefs.DevMode) return "FAF_DevelopmentOnly".Translate();
            return CanManageThermostat();
        }
        // All scene entry points call these same actions; they never create bottles or alter thermostat settings.
        // 所有场景共用同一动作；不生成瓶装物，也不改变温控设置。
        public void DevFillBattery() { SetDevelopmentBattery(1f); }
        public void DevEmptyBattery() { SetDevelopmentBattery(0f); }
        private void SetDevelopmentBattery(float fraction)
        {
            if (!CanUseDevelopmentResources()) return;
            Battery.SetStoredEnergyPct(fraction);
            InvalidateBatteryFlowSample();
        }
        public void DevFillCoolant()
        {
            if (!CanUseDevelopmentResources()) return;
            CoolantTank.Refuel(System.Math.Max(0f, CoolantTank.Props.fuelCapacity - CoolantTank.Fuel)
                / CoolantTank.Props.FuelMultiplierCurrentDifficulty);
        }
        public void DevEmptyCoolant()
        {
            if (CanUseDevelopmentResources()) CoolantTank.ConsumeFuel(CoolantTank.Fuel);
        }
    }
}
