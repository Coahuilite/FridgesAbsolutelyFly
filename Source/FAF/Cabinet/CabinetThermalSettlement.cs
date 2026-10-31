// SPDX-License-Identifier: MPL-2.0
using System;
using FAF.Cabinet.Core;
using RimWorld;
using Verse;

namespace FAF.Cabinet
{
    public sealed partial class Building_FlyingFridgeCabinet
    {
        // A native tick proposal and display observation, never a second temperature or energy balance.
        // 原生单tick候选和显示观察，不是第二份温度或能量余额；均不保存。
        private ThermalReading nativeThermalReading;
        private int nativeThermalFrameTick = -1;
        private int thermalUseSampleTick = -1, lastThermalDirection;
        private float lastThermalCoolantUnitsPerDay;
        private CabinetCoolingSource lastThermalSource;

        private void InvalidateThermalUseSample()
        {
            thermalUseSampleTick = -1;
            lastThermalCoolantUnitsPerDay = 0f;
            lastThermalSource = CabinetCoolingSource.Unavailable;
        }

        internal void PrepareNativeThermalTick(PowerNet net, out float thermalWatts, out float lifeSupportWatts)
        {
            thermalWatts = 0f; lifeSupportWatts = 0f;
            nativeThermalFrameTick = Find.TickManager.TicksGame;
            nativeThermalReading = ReadThermalState();
            if (!ElectricityCanOperate) return;
            // Remove this trader's previous on-load once before quoting its replacement.
            // 报价替换负载前，仅加回一次本负载原先计入电网的功率。
            double available = net.CurrentStoredEnergy() + net.CurrentEnergyGainRate()
                + (RefrigerationLoad.PowerOn ? -RefrigerationLoad.PowerOutput * CompPower.WattsToWattDaysPerTick : 0d);
            available = Math.Max(0d, available);
            double life = Math.Min(available, nativeThermalReading.LifeSupportWatts * CompPower.WattsToWattDaysPerTick);
            lifeSupportWatts = (float)(life / CompPower.WattsToWattDaysPerTick);
            CabinetThermalResult quote = CabinetThermalModel.Step(internalTemperature, nativeThermalReading.Parameters,
                1d, Math.Max(0d, available - life), 0d);
            thermalWatts = (float)(quote.ElectricalWattDays / CompPower.WattsToWattDaysPerTick);
        }

        internal void CompleteNativeThermalTick(float thermalWattDays, float lifeSupportWattDays)
        {
            if (nativeThermalFrameTick != Find.TickManager.TicksGame) return;
            nativeThermalFrameTick = -1;
            ObserveElectricalLoad(thermalWattDays, lifeSupportWattDays);
            CompleteThermalTick(nativeThermalReading, thermalWattDays, lifeSupportWattDays);
        }

        private void SettleOffGridThermalTick(ThermalReading reading)
        {
            float life = ElectricityCanOperate
                ? DrawThermalBattery(reading.LifeSupportWatts * CompPower.WattsToWattDaysPerTick) : 0f;
            double budget = ElectricityCanOperate ? Battery.StoredEnergy : 0d;
            CabinetThermalResult quote = CabinetThermalModel.Step(internalTemperature, reading.Parameters,
                1d, budget, AvailableThermalCoolant);
            float thermal = DrawThermalBattery((float)quote.ElectricalWattDays);
            ObserveElectricalLoad(thermal, life);
            CompleteThermalTick(reading, thermal, life);
        }

        private double AvailableThermalCoolant { get { return coolantBackupEnabled ? CoolantTank.Fuel : 0d; } }

        private float DrawThermalBattery(float wattDays)
        {
            if (wattDays <= 0f) return 0f;
            float before = Battery.StoredEnergy;
            Battery.DrawPower(Math.Min(before, wattDays));
            return Math.Max(0f, before - Battery.StoredEnergy);
        }

        private void CompleteThermalTick(ThermalReading reading, float thermalWattDays,
            float lifeSupportWattDays)
        {
            // Quote liquid only after the actual electrical grant is known. Native float debits may round.
            // 先取得实际电力支付，再报价液体；原生float扣费可能发生舍入。
            CabinetThermalResult liquidQuote = CabinetThermalModel.Step(internalTemperature, reading.Parameters,
                1d, thermalWattDays, AvailableThermalCoolant);
            float before = CoolantTank.Fuel;
            if (liquidQuote.CoolantUnits > 0d) CoolantTank.ConsumeFuel((float)liquidQuote.CoolantUnits);
            float liquidUsed = Math.Max(0f, before - CoolantTank.Fuel);
            CabinetThermalResult actual = CabinetThermalModel.Step(internalTemperature, reading.Parameters,
                1d, thermalWattDays, liquidUsed);
            if (reading.Exterior.Known) internalTemperature = (float)actual.Temperature;
            float lifeTicks = reading.LifeSupportWatts <= 0f ? 0f : Math.Min(1f,
                lifeSupportWattDays / (reading.LifeSupportWatts * CompPower.WattsToWattDaysPerTick));
            AdvanceCabinAtmosphere(reading.Exterior, lifeTicks, 1);
            thermalUseSampleTick = Find.TickManager.TicksGame;
            lastThermalCoolantUnitsPerDay = liquidUsed * 60000f;
            lastThermalDirection = actual.FirstPoweredDirection;
            lastThermalSource = actual.ElectricalWattDays > 0d
                ? liquidUsed > 0f ? CabinetCoolingSource.Mixed : CabinetCoolingSource.Electricity
                : liquidUsed > 0f ? CabinetCoolingSource.Coolant : CabinetCoolingSource.Unavailable;
        }
    }
}
