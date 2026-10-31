// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet.Core;

namespace FAF.Cabinet
{
    /// <summary>A display-only snapshot; the cabinet and native comps remain authoritative. / 仅供显示的快照；柜体和原生组件仍是唯一状态来源。</summary>
    public sealed class CabinetThermostatSnapshot
    {
        // Map the domain state to presentation keys only at the view boundary. / 只在视图边界把领域状态映射为显示键。
        internal static string SourceKeyFor(CabinetCoolingSource source)
        {
            switch (source)
            {
                case CabinetCoolingSource.Off: return "FAF_ThermostatSourceOff";
                case CabinetCoolingSource.Idle: return "FAF_ThermostatSourceIdle";
                case CabinetCoolingSource.Electricity: return "FAF_ThermostatSourceElectricity";
                case CabinetCoolingSource.Coolant: return "FAF_ThermostatSourceCoolant";
                case CabinetCoolingSource.Mixed: return "FAF_ThermostatSourceMixed";
                case CabinetCoolingSource.WaitingForGrid: return "FAF_ThermostatSourceWaiting";
                case CabinetCoolingSource.HeatingUnavailable: return "FAF_ThermostatSourceHeatingUnavailable";
                default: return "FAF_ThermostatSourceUnavailable";
            }
        }

        public float InternalTemperatureCelsius { get; internal set; }
        public float AmbientTemperatureCelsius { get; internal set; }
        public float TargetTemperatureCelsius { get; internal set; }
        public float MinimumTargetCelsius { get; internal set; }
        public float MaximumTargetCelsius { get; internal set; }
        // Legacy caller name; the value is the one-tick pull/hold quote, including fractional phases.
        // 保留既有调用名称；数值为包含分数阶段的单tick拉温／维持报价。
        public float RefrigerationDemandWatts { get; internal set; }
        public float BatteryStoredWattDays { get; internal set; }
        public float BatteryCapacityWattDays { get; internal set; }
        public float? BatteryNetDrainWatts { get; internal set; }
        public float? BatteryRemainingDays { get; internal set; }
        public string CoolantLabel { get; internal set; }
        public float CoolantStoredUnits { get; internal set; }
        public float CoolantCapacityUnits { get; internal set; }
        public float CoolantTargetUnits { get; internal set; }
        public float CoolantConsumptionPerDay { get; internal set; }
        public float? CoolantRemainingDays { get; internal set; }
        public string CoolingSourceKey { get; internal set; }
        public string WiringModeKey { get; internal set; }
        public bool PoweredOn { get; internal set; }
        public bool CoolantBackupEnabled { get; internal set; }
        public bool AutomaticCoolantSupplyAvailable { get; internal set; }
        public bool AutomaticCoolantSupplyEnabled { get; internal set; }
        // Legacy presentation alias for CoolantTargetUnits, not a second configuration.
        // CoolantTargetUnits的旧呈现别名，不是第二份配置。
        public float AutomaticCoolantTargetUnits { get; internal set; }
        public string AutomaticCoolantSupplyStatusKey { get; internal set; }
        public bool HeatingUnlocked { get; internal set; }
        public bool RapidThermalControlAvailable { get; internal set; }
        public bool RapidThermalControlEnabled { get; internal set; }
        public int ThermalDirection { get; internal set; }
        public float InternalVacuum { get; internal set; }
        public float ExteriorVacuum { get; internal set; }
        public float LifeSupportDemandWatts { get; internal set; }
        public float TotalElectricalDemandWatts { get; internal set; }
        // Completed native settlement observations; null until a common completed tick is available.
        // 原生实际结算观察；尚无共同完整tick样本时为null。
        public float? ActualElectricalUseWatts { get; internal set; }
        public float? ActualThermalElectricalWatts { get; internal set; }
        public float? ActualLifeSupportElectricalWatts { get; internal set; }
        public float? ActualBatterySelfDischargeWatts { get; internal set; }
        public bool LifeSupportPowered { get; internal set; }
        public bool EnvironmentKnown { get; internal set; }
        public bool RestPreparationActive { get; internal set; }
        public bool HasActualOccupants { get; internal set; }
    }
}
