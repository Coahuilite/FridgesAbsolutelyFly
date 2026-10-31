// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet.Integration;
using FAF.Transport;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        private CompFridgeCabinetCoolant AutomaticCoolantTank { get { return GetComp<CompFridgeCabinetCoolant>(); } }
        internal bool AutomaticCoolantSupplyActive { get { return UpgradeProfile.SupplyAutomationUnlocked && AutomaticCoolantTank.AutomaticSupplyEnabled; } }

        public CabinetResourceSnapshot GetResourceSnapshot()
        {
            return new CabinetResourceSnapshot
            {
                BatteryStoredWattDays = Battery.StoredEnergy,
                BatteryCapacityWattDays = Battery.Props.storedEnergyMax,
                CoolantStoredUnits = CoolantTank.Fuel,
                CoolantCapacityUnits = CoolantTank.Props.fuelCapacity,
                CoolantTargetUnits = AutomaticCoolantTank.SupplyTargetUnits,
                AutomaticCoolantSupplyAvailable = UpgradeProfile.SupplyAutomationUnlocked,
                AutomaticCoolantSupplyEnabled = AutomaticCoolantTank.AutomaticSupplyEnabled
            };
        }

        public void SetAutomaticCoolantSupplyEnabled(bool enabled)
        {
            if (!CanManageThermostat() || enabled && !UpgradeProfile.SupplyAutomationUnlocked) return;
            AutomaticCoolantTank.SetAutomaticSupplyEnabled(enabled);
        }

        public void SetAutomaticCoolantTargetUnits(float target)
        {
            // Retained caller name; there is no separately saved automatic target.
            // 保留旧调用者名称，不另存自动目标。
            SetCoolantTargetUnits(target);
        }

        public void SetCoolantTargetUnits(float target)
        {
            if (CanManageThermostat()) AutomaticCoolantTank.SetSupplyTargetUnits(target);
        }

        internal int CoolantBottleBudget()
        {
            float unitsPerBottle = CoolantTank.Props.FuelMultiplierCurrentDifficulty;
            if (float.IsNaN(unitsPerBottle) || float.IsInfinity(unitsPerBottle) || unitsPerBottle <= 0f) return 0;
            float space = Mathf.Max(0f, Mathf.Min(AutomaticCoolantTank.SupplyTargetUnits,
                CoolantTank.Props.fuelCapacity) - CoolantTank.Fuel);
            return Mathf.FloorToInt(space / unitsPerBottle);
        }

        private string AutomaticCoolantSupplyStatus(CabinetOccupancySnapshot occupancy)
        {
            if (!UpgradeProfile.SupplyAutomationUnlocked) return "FAF_AutoCoolantLocked";
            if (!AutomaticCoolantTank.AutomaticSupplyEnabled) return "FAF_AutoCoolantOff";
            if (!CanManageThermostat()) return "FAF_AutoCoolantUnavailable";
            if (occupancy.Phase == CabinetTravelPhase.Transferring) return "FAF_AutoCoolantTransferring";
            if (CoolantBottleBudget() <= 0) return "FAF_AutoCoolantWaitingSpace";
            if (HasOwnCoolantBottles()) return "FAF_AutoCoolantReady";
            return Spawned ? "FAF_AutoCoolantWaitingHauler" : "FAF_AutoCoolantNoExternalAccess";
        }

        private void TickAutomaticCoolantSupply()
        {
            if (!this.IsHashIntervalTick(150) || !UpgradeProfile.SupplyAutomationUnlocked
                || !AutomaticCoolantTank.AutomaticSupplyEnabled || !CanManageThermostat()
                || CabinetOccupancy.Read(this).Phase == CabinetTravelPhase.Transferring) return;

            // Internal supply is first; external work is offered through the native refuelling work giver.
            // 优先采用柜内补给；外部工作由原生补液工作提供者安排。
            foreach (ThingOwner source in OwnCoolantCargoSources())
                for (int i = source.Count - 1; i >= 0; i--)
                {
                    int budget = CoolantBottleBudget();
                    if (budget <= 0) return;
                    Thing item = source[i];
                    if (!IsCoolantBottle(item)) continue;
                    ConsumeCoolantBottles(source, item, Mathf.Min(item.stackCount, budget));
                }
        }
    }
}
