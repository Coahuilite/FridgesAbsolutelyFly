// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet.Integration;
using RimWorld;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        // A two-tick observation buffer, not an energy account. Nothing here is saved or spent.
        // 两个tick的观察缓冲，不是能量账户；此处既不保存也不扣除资源。
        private struct ElectricalUseObservation
        {
            internal int Tick, LoadTicks;
            internal bool HasLoad, HasSelfDischarge;
            internal float ThermalWattDays, LifeSupportWattDays, SelfDischargeWattDays;
            internal bool Complete { get { return HasLoad && HasSelfDischarge; } }
        }
        private ElectricalUseObservation currentElectricalUse, previousElectricalUse;

        private void PrepareElectricalObservation()
        {
            int now = Find.TickManager.TicksGame;
            if (currentElectricalUse.Tick == now) return;
            previousElectricalUse = currentElectricalUse;
            currentElectricalUse = new ElectricalUseObservation { Tick = now };
        }
        internal void ObserveElectricalLoad(float thermalWattDays, float lifeSupportWattDays)
        {
            PrepareElectricalObservation();
            currentElectricalUse.HasLoad = true;
            currentElectricalUse.LoadTicks++;
            currentElectricalUse.ThermalWattDays += thermalWattDays;
            currentElectricalUse.LifeSupportWattDays += lifeSupportWattDays;
        }
        internal void ObserveBatterySelfDischarge(float wattDays)
        {
            PrepareElectricalObservation();
            currentElectricalUse.HasSelfDischarge = true;
            currentElectricalUse.SelfDischargeWattDays += wattDays;
        }
        private void InvalidateElectricalUseObservation()
        {
            currentElectricalUse = default(ElectricalUseObservation);
            previousElectricalUse = default(ElectricalUseObservation);
        }
        private void AddElectricalUseToSnapshot(CabinetThermostatSnapshot snapshot)
        {
            int now = Find.TickManager.TicksGame;
            ElectricalUseObservation observation = currentElectricalUse;
            if (!observation.Complete || (observation.Tick != now && observation.Tick != now - 1))
            {
                observation = previousElectricalUse;
                if (!observation.Complete || observation.Tick != now - 1) return;
            }
            // A held batch can settle several canonical load ticks; display its per-tick mean, not their sum.
            // 持有者批量调用可能结算多个标准负载tick；显示单tick均值，不把合计误作功率。
            float conversion = CompPower.WattsToWattDaysPerTick;
            float loadConversion = conversion * observation.LoadTicks;
            snapshot.ActualThermalElectricalWatts = observation.ThermalWattDays / loadConversion;
            snapshot.ActualLifeSupportElectricalWatts = observation.LifeSupportWattDays / loadConversion;
            snapshot.ActualBatterySelfDischargeWatts = observation.SelfDischargeWattDays / conversion;
            snapshot.ActualElectricalUseWatts = (observation.ThermalWattDays + observation.LifeSupportWattDays) / loadConversion
                + observation.SelfDischargeWattDays / conversion;
        }
        internal void RefreshElectricalLoad()
        {
            ThermalReading reading = ReadThermalState();
            RefrigerationLoad.RefreshDemand(reading.ElectricalThermalWatts, reading.LifeSupportWatts);
        }
    }
}
