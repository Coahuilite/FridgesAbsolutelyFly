// SPDX-License-Identifier: MPL-2.0
namespace FAF.Cabinet
{
    /// <summary>Cheap live resource readings; no cargo or occupancy enumeration. / 低成本实时资源读数，不枚举货物或占用。</summary>
    public struct CabinetResourceSnapshot
    {
        public float BatteryStoredWattDays { get; internal set; }
        public float BatteryCapacityWattDays { get; internal set; }
        public float CoolantStoredUnits { get; internal set; }
        public float CoolantCapacityUnits { get; internal set; }
        public float CoolantTargetUnits { get; internal set; }
        public bool AutomaticCoolantSupplyAvailable { get; internal set; }
        public bool AutomaticCoolantSupplyEnabled { get; internal set; }
    }
}
