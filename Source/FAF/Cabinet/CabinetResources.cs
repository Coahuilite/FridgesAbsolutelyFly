// SPDX-License-Identifier: MPL-2.0
using System;
using FAF.Cabinet.Core;
using FAF.Cabinet.Integration;
using FAF.Transport;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        private bool coolantBackupEnabled;
        private bool rapidThermalControlRequested;
        private int batteryFlowSampleTick = -1;
        private float previousBatteryEnergy;
        private float? batteryNetDrainWatts;
        private CompFridgeCabinetTemperature TemperatureControl { get { return GetComp<CompFridgeCabinetTemperature>(); } }
        private CompRefuelable CoolantTank { get { return GetComp<CompRefuelable>(); } }
        private CompFridgeCabinetPowerLoad RefrigerationLoad { get { return GetComp<CompFridgeCabinetPowerLoad>(); } }
        public bool CoolantBackupEnabled { get { return coolantBackupEnabled; } }
        public bool ThermalPowerRequestedOn { get { return CabinetPowerSwitch.Requested(GetComp<CompFlickable>()); } }
        public bool ThermalPowerSwitchPending { get { return GetComp<CompFlickable>().WantsFlick(); } }
        public void RequestThermalPowerSwitch(bool requestedOn)
        {
            if (!CanManageThermostat()) return;
            CabinetPowerSwitch.Request(this, requestedOn);
            InvalidateThermalUseSample();
            RefrigerationLoad.RefreshDemand();
        }
        public void ToggleThermalPower() { RequestThermalPowerSwitch(!ThermalPowerRequestedOn); }
        // A despawned cabinet cannot draw from a former map while native disconnection is queued.
        // 取消生成的柜体不能在原生断线队列处理之前继续使用旧地图电网。
        internal PowerNet ConnectedPowerNet
        {
            get
            {
                CompPower connection = Battery.connectParent;
                return Spawned && connection != null && connection.parent.Spawned && connection.parent.Map == Map
                    ? Battery.PowerNet : null;
            }
        }

        private struct ThermalReading
        {
            public float AmbientTemperature, TargetTemperature;
            public bool SwitchedOn;
            public CabinetThermalParameters Parameters;
            public CabinetThermalDemand Demand;
            public CabinetOccupancySnapshot Occupancy;
            public ExteriorReading Exterior;
            public float LifeSupportWatts, ElectricalThermalWatts;
            public float ElectricalWatts { get { return LifeSupportWatts + ElectricalThermalWatts; } }
        }

        private ThermalReading ReadThermalState()
        {
            CompFridgeCabinetTemperature control = TemperatureControl;
            CompProperties_FridgeCabinetTemperature settings = control.ThermalProps;
            CabinetOccupancySnapshot occupancy = CabinetOccupancy.Read(this);
            ExteriorReading exterior = ReadExterior(occupancy);
            bool switchedOn = FlickUtility.WantsToBeOn(this);
            Research.CabinetUpgradeProfile profile = UpgradeProfile;
            CabinetThermalParameters parameters = new CabinetThermalParameters
            {
                Ambient = exterior.Temperature, Target = control.TargetTemperature,
                Enabled = switchedOn && exterior.Known, HeatingAllowed = profile.HeatingUnlocked,
                WattsPerDegree = settings.wattsPerDegree, CoolantWattDaysPerUnit = settings.coolantWattDaysPerUnit,
                PassiveExchangeFactor = profile.PassiveExchangeFactor,
                PowerMultiplier = rapidThermalControlRequested && profile.RapidThermalControlUnlocked
                    ? profile.RapidThermalMultiplier : 1d
            };
            // The quote includes fractional pull/hold phases of this tick, not a whole-tick startup fee.
            // 报价包含本tick分数时段的拉温／维持，而不是收取整tick启动费。
            CabinetThermalResult quote = CabinetThermalModel.Step(internalTemperature, parameters, 1d,
                double.PositiveInfinity, 0d);
            return new ThermalReading
            {
                AmbientTemperature = exterior.Temperature, TargetTemperature = control.TargetTemperature,
                SwitchedOn = switchedOn, Parameters = parameters,
                Demand = CabinetThermalModel.Demand(internalTemperature, parameters),
                Occupancy = occupancy, Exterior = exterior,
                LifeSupportWatts = exterior.Known && switchedOn && profile.HeatingUnlocked
                    && (occupancy.ActualOccupants.Count > 0 || occupancy.RestPreparationActive) ? settings.lifeSupportWatts : 0f,
                ElectricalThermalWatts = (float)(quote.ElectricalWattDays / CompPower.WattsToWattDaysPerTick)
            };
        }

        internal CabinetThermalDemand CurrentThermalDemand { get { return ReadThermalState().Demand; } }
        internal float CurrentElectricalDemandWatts { get { return ReadThermalState().ElectricalWatts; } }
        private bool ElectricityCanOperate
        {
            get
            {
                return !Battery.StunnedByEMP && !this.IsBrokenDown()
                    && (!Spawned || !Map.gameConditionManager.ElectricityDisabled(Map));
            }
        }

        private CabinetCoolingSource SelectCoolingSource(ThermalReading reading)
        {
            if (!reading.SwitchedOn) return CabinetCoolingSource.Off;
            if (reading.ElectricalThermalWatts <= 0f) return CabinetCoolingSource.Idle;
            if (ElectricityCanOperate)
            {
                PowerNet net = ConnectedPowerNet;
                if (net != null)
                {
                    CabinetCoolingSource source = RefrigerationLoad.GridSupplySource(net);
                    if (source != CabinetCoolingSource.Unavailable) return source;
                }
                else if (Battery.StoredEnergy > reading.LifeSupportWatts * CompPower.WattsToWattDaysPerTick)
                    return CabinetCoolingSource.Electricity;
            }
            if (reading.Demand.Direction > 0) return CabinetCoolingSource.HeatingUnavailable;
            return reading.Demand.Direction < 0 && coolantBackupEnabled && CoolantTank.HasFuel
                ? CabinetCoolingSource.Coolant : CabinetCoolingSource.Unavailable;
        }

        private void TickThermalResources(int delta)
        {
            TickAutomaticCoolantSupply();
            CoolantTank.allowAutoRefuel = coolantBackupEnabled;
            if (ConnectedPowerNet == null)
            {
                // Match the native per-tick supply priority even when a held caller supplies a batch.
                // 即使持有者传入批量tick，仍逐tick保持原生供能优先顺序。
                for (int i = 0; i < delta; i++) SettleOffGridThermalTick(ReadThermalState());
            }
            else RefrigerationLoad.RefreshDemand();
            // Grid thermal/pressure changes commit at the native settlement receipt, not at this prediction.
            // 接网温度／压力在原生支付回执处提交，不在此预测供能后提前推进。
            CabinetResidentNeeds.FeedHeldOccupants(this, CabinetOccupancy.Read(this));
            SampleBatteryFlow();
        }

        public void InvalidateBatteryFlowSample()
        {
            batteryFlowSampleTick = -1;
            batteryNetDrainWatts = null;
            InvalidateElectricalUseObservation();
            InvalidateThermalUseSample();
        }
        private void SampleBatteryFlow()
        {
            int now = Find.TickManager.TicksGame;
            if (batteryFlowSampleTick >= 0 && now > batteryFlowSampleTick)
                batteryNetDrainWatts = (previousBatteryEnergy - Battery.StoredEnergy) * 60000f / (now - batteryFlowSampleTick);
            previousBatteryEnergy = Battery.StoredEnergy;
            batteryFlowSampleTick = now;
        }

        public void SetTargetTemperatureCelsius(float target)
        {
            if (!CanManageThermostat()) return;
            TemperatureControl.TargetTemperature = target;
            InvalidateThermalUseSample();
            RefrigerationLoad.RefreshDemand();
        }
        public float MaximumTargetTemperature
        {
            get { return Mathf.Max(TemperatureControl.Props.maxTargetTemperature, UpgradeProfile.MaximumTargetTemperature); }
        }
        public bool RapidThermalControlEnabled
        {
            get { return rapidThermalControlRequested && UpgradeProfile.RapidThermalControlUnlocked; }
        }
        public void SetRapidThermalControlEnabled(bool enabled)
        {
            if (!CanManageThermostat()) return;
            rapidThermalControlRequested = enabled && UpgradeProfile.RapidThermalControlUnlocked;
            InvalidateThermalUseSample();
            RefrigerationLoad.RefreshDemand();
        }
        public void SetCoolantBackupEnabled(bool enabled)
        {
            if (!CanManageThermostat()) return;
            coolantBackupEnabled = enabled;
            InvalidateThermalUseSample();
            CoolantTank.allowAutoRefuel = enabled;
        }
        public void ReconnectPower()
        {
            if (!Spawned || !CanManageThermostat()) return;
            Battery.TryManualReconnect();
            InvalidateBatteryFlowSample();
        }
        public void RequestCoolantRefuel()
        {
            if (!CanManageThermostat()) return;
            if (!coolantBackupEnabled)
            {
                Messages.Message("FAF_CoolantBackupDisabled".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (RefuelThermalCoolantFromContents() > 0 || !CanRefuelForLaunch) return;
            if (!Spawned) return;
            if (!TryOrderCoolantRefuel(false))
                Messages.Message("FAF_NoCoolantHauler".Translate(), MessageTypeDefOf.RejectInput, false);
        }
        private bool TryOrderCoolantRefuel(bool forLaunch)
        {
            foreach (Pawn pawn in Map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Downed || pawn.Drafted || pawn.InMentalState || pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)
                    || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    || !pawn.CanReach(this, PathEndMode.Touch, Danger.Some)) continue;
                Job job;
                if (forLaunch) job = CabinetLaunchRefuelRequest.CreateJob(pawn, this);
                else job = RefuelWorkGiverUtility.CanRefuel(pawn, this, true)
                    ? RefuelWorkGiverUtility.RefuelJob(pawn, this, true) : null;
                if (job != null && pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) return true;
            }
            return false;
        }
        public void ReturnCoolantToMap()
        {
            if (!Spawned || !CanManageThermostat()) return;
            SetCoolantBackupEnabled(false);
            int count = Mathf.FloorToInt(CoolantTank.Fuel);
            if (count <= 0)
            {
                Messages.Message("FAF_CoolantFractionRetained".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Thing returned = ThingMaker.MakeThing(CabinetDefOf.Nivarian_CryoCoolant);
            returned.stackCount = count;
            // Account for each actual placed/merged stack, even if a later placement cannot finish.
            // 按真正放置或合并的数量结算，即使之后无法放完，也不会重复退还。
            GenPlace.TryPlaceThing(returned, InteractionCell, Map, ThingPlaceMode.Near,
                placedAction: (placed, amount) =>
                {
                    placed.SetForbidden(true, false);
                    CoolantTank.ConsumeFuel(amount);
                });
            // Only a temporary unplaced remainder is discarded; its fuel remains in the native tank.
            // 只销毁临时的未放置余量；对应燃料仍保留在原生仓内。
            if (!returned.Destroyed && !returned.Spawned) returned.Destroy(DestroyMode.Vanish);
        }

        public CabinetThermostatSnapshot GetThermostatSnapshot()
        {
            ThermalReading reading = ReadThermalState();
            CabinetThermalDemand demand = reading.Demand;
            CabinetCoolingSource source = SelectCoolingSource(reading);
            int now = Find.TickManager.TicksGame;
            bool fresh = thermalUseSampleTick >= 0 && (thermalUseSampleTick == now || thermalUseSampleTick == now - 1);
            float coolantRate = fresh && reading.SwitchedOn && reading.Demand.Direction < 0
                && coolantBackupEnabled ? lastThermalCoolantUnitsPerDay : 0f;
            if (fresh && reading.Demand.Direction == lastThermalDirection
                && reading.ElectricalThermalWatts > 0f && reading.SwitchedOn
                && (lastThermalSource == CabinetCoolingSource.Electricity || lastThermalSource == CabinetCoolingSource.Coolant
                    || lastThermalSource == CabinetCoolingSource.Mixed)) source = lastThermalSource;
            CabinetThermostatSnapshot snapshot = new CabinetThermostatSnapshot
            {
                InternalTemperatureCelsius = internalTemperature,
                AmbientTemperatureCelsius = reading.AmbientTemperature,
                TargetTemperatureCelsius = reading.TargetTemperature,
                MinimumTargetCelsius = TemperatureControl.Props.minTargetTemperature,
                MaximumTargetCelsius = MaximumTargetTemperature,
                RefrigerationDemandWatts = reading.ElectricalThermalWatts,
                BatteryStoredWattDays = Battery.StoredEnergy,
                BatteryCapacityWattDays = Battery.Props.storedEnergyMax,
                BatteryNetDrainWatts = batteryNetDrainWatts,
                BatteryRemainingDays = !batteryNetDrainWatts.HasValue ? (float?)null
                    : batteryNetDrainWatts.Value <= 0f ? float.PositiveInfinity : Battery.StoredEnergy / batteryNetDrainWatts.Value,
                CoolantLabel = CabinetDefOf.Nivarian_CryoCoolant.LabelCap,
                CoolantStoredUnits = CoolantTank.Fuel,
                CoolantCapacityUnits = CoolantTank.Props.fuelCapacity,
                CoolantTargetUnits = AutomaticCoolantTank.SupplyTargetUnits,
                CoolantConsumptionPerDay = coolantRate,
                CoolantRemainingDays = coolantRate <= 0f ? float.PositiveInfinity : CoolantTank.Fuel / coolantRate,
                CoolingSourceKey = CabinetThermostatSnapshot.SourceKeyFor(source),
                WiringModeKey = "FAF_ThermostatWireAutomatic",
                PoweredOn = reading.SwitchedOn,
                CoolantBackupEnabled = coolantBackupEnabled,
                AutomaticCoolantSupplyAvailable = UpgradeProfile.SupplyAutomationUnlocked,
                AutomaticCoolantSupplyEnabled = AutomaticCoolantTank.AutomaticSupplyEnabled,
                AutomaticCoolantTargetUnits = AutomaticCoolantTank.SupplyTargetUnits,
                AutomaticCoolantSupplyStatusKey = AutomaticCoolantSupplyStatus(reading.Occupancy),
                HeatingUnlocked = UpgradeProfile.HeatingUnlocked,
                RapidThermalControlAvailable = UpgradeProfile.RapidThermalControlUnlocked,
                RapidThermalControlEnabled = RapidThermalControlEnabled,
                ThermalDirection = demand.Direction,
                InternalVacuum = internalVacuum,
                ExteriorVacuum = reading.Exterior.Vacuum,
                LifeSupportDemandWatts = reading.LifeSupportWatts,
                TotalElectricalDemandWatts = reading.ElectricalWatts,
                LifeSupportPowered = lastLifeSupportPowered && reading.LifeSupportWatts > 0f,
                EnvironmentKnown = reading.Exterior.Known,
                RestPreparationActive = reading.Occupancy.RestPreparationActive,
                HasActualOccupants = reading.Occupancy.ActualOccupants.Count > 0
            };
            AddElectricalUseToSnapshot(snapshot);
            return snapshot;
        }
    }
}
