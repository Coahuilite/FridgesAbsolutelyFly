// SPDX-License-Identifier: MPL-2.0
using System;

namespace FAF.Cabinet.Core
{
    /// <summary>One tick input without game/resource owners. / 不含游戏及资源持有者的单tick输入。</summary>
    public struct CabinetThermalParameters
    {
        public double Ambient, Target, WattsPerDegree, CoolantWattDaysPerUnit;
        public double PowerMultiplier, PassiveExchangeFactor;
        public bool Enabled, HeatingAllowed;
    }

    public struct CabinetThermalDemand
    {
        public double Watts;
        public int Direction; // -1 cooling, +1 heating, 0 passive. / -1制冷，+1制热，0被动。
        public bool Pulling;
    }

    /// <summary>One proposal; only the resource adapter commits it. / 单次候选结果，由资源适配层提交。</summary>
    public struct CabinetThermalResult
    {
        public double Temperature, ElectricalWattDays, CoolantUnits;
        public int FirstPoweredDirection;
    }

    public static class CabinetThermalModel
    {
        public const int CapacityStacks = 12;
        public const float MinimumTarget = -30f, MaximumTarget = 10f;
        public const double TicksPerDay = 60000d;
        // Gameplay actuator/maintenance services, not material heat capacity or a COP simulation.
        // 游戏中的拉温／维持服务，不逐物计算热容量或模拟COP。
        private const double InsulationLeakPerTick = 0.00003d;
        private const double PullWatts = 120d, PullDegreesPerTick = 0.0125d;

        public static CabinetThermalDemand Demand(double temperature, CabinetThermalParameters settings)
        {
            if (!settings.Enabled) return default(CabinetThermalDemand);
            bool pulling = temperature != settings.Target;
            int direction = pulling ? Math.Sign(settings.Target - temperature)
                : Math.Sign(settings.Target - settings.Ambient);
            if (direction == 0 || (direction > 0 && !settings.HeatingAllowed))
                return default(CabinetThermalDemand);
            double opposingGap = Math.Max(0d, -direction * (settings.Ambient - settings.Target));
            return new CabinetThermalDemand
            {
                Direction = direction,
                Pulling = pulling,
                Watts = settings.PowerMultiplier * (settings.WattsPerDegree * opposingGap + (pulling ? PullWatts : 0d))
            };
        }

        /// <summary>Integrate target/source events against real budgets. / 按真实预算积分目标及来源事件。</summary>
        public static CabinetThermalResult Step(double temperature, CabinetThermalParameters settings,
            double ticks, double electricalWattDays, double coolantUnits)
        {
            CabinetThermalResult result = new CabinetThermalResult { Temperature = temperature };
            double remaining = ticks;
            double electricity = Math.Max(0d, electricalWattDays), coolant = Math.Max(0d, coolantUnits);
            double leak = InsulationLeakPerTick * settings.PassiveExchangeFactor;
            while (remaining > 0d)
            {
                CabinetThermalDemand demand = Demand(result.Temperature, settings);
                bool electric = demand.Watts > 0d && electricity > 0d;
                bool liquid = demand.Direction < 0 && demand.Watts > 0d && !electric && coolant > 0d;
                if (!electric && !liquid)
                {
                    // Natural crossing can enable a different legal maintenance direction within this tick.
                    // 自然跨过目标后，本tick余段可能出现另一种合法维持方向。
                    double naturalTarget = PassiveTimeToTarget(result.Temperature, settings.Ambient, settings.Target, leak);
                    double elapsed = Math.Min(remaining, naturalTarget);
                    result.Temperature = Passive(result.Temperature, settings.Ambient, elapsed, leak);
                    remaining -= elapsed;
                    if (elapsed == naturalTarget) result.Temperature = settings.Target;
                    continue;
                }
                double toTarget = demand.Pulling ? PoweredTimeToTarget(result.Temperature, settings, leak, demand.Direction)
                    : double.PositiveInfinity;
                double available = electric ? electricity : coolant * settings.CoolantWattDaysPerUnit;
                double fundedTicks = available * TicksPerDay / demand.Watts;
                double duration = Math.Min(remaining, Math.Min(toTarget, fundedTicks));
                bool exhausted = duration == fundedTicks;
                double energy = exhausted ? available : demand.Watts * duration / TicksPerDay;
                if (electric)
                {
                    result.ElectricalWattDays += energy;
                    electricity = exhausted ? 0d : Math.Max(0d, electricity - energy);
                }
                else
                {
                    double units = energy / settings.CoolantWattDaysPerUnit;
                    result.CoolantUnits += units;
                    coolant = exhausted ? 0d : Math.Max(0d, coolant - units);
                }
                if (result.FirstPoweredDirection == 0) result.FirstPoweredDirection = demand.Direction;
                result.Temperature = demand.Pulling
                    ? Powered(result.Temperature, settings, duration, leak, demand.Direction) : settings.Target;
                remaining -= duration;
                // Only a paid target event clamps; unpowered exchange may naturally pass the target.
                // 只在付费到点事件截断；无供能自然换热可以越过目标。
                if (duration == toTarget) result.Temperature = settings.Target;
            }
            return result;
        }

        private static double Passive(double temperature, double ambient, double ticks, double leak)
        { return ambient + (temperature - ambient) * Math.Exp(-leak * ticks); }

        private static double PassiveTimeToTarget(double temperature, double ambient, double target, double leak)
        {
            if (temperature == target || ambient == temperature || ambient == target) return double.PositiveInfinity;
            double ratio = (target - ambient) / (temperature - ambient);
            return ratio > 0d && ratio < 1d ? -Math.Log(ratio) / leak : double.PositiveInfinity;
        }

        private static double PoweredTimeToTarget(double temperature, CabinetThermalParameters settings,
            double leak, int direction)
        {
            double speed = PullDegreesPerTick * settings.PowerMultiplier;
            double fromAmbient = direction * (temperature - settings.Ambient);
            double targetFromAmbient = direction * (settings.Target - settings.Ambient);
            if (fromAmbient >= 0d) return Math.Abs(settings.Target - temperature) / speed;
            if (targetFromAmbient <= 0d)
                return LogOnePlus((targetFromAmbient - fromAmbient) / (speed / leak - targetFromAmbient)) / leak;
            return LogOnePlus(-fromAmbient * leak / speed) / leak + targetFromAmbient / speed;
        }

        private static double Powered(double temperature, CabinetThermalParameters settings,
            double ticks, double leak, int direction)
        {
            double speed = PullDegreesPerTick * settings.PowerMultiplier;
            double fromAmbient = direction * (temperature - settings.Ambient);
            if (fromAmbient >= 0d) return temperature + direction * speed * ticks;
            // Helpful exchange remains; opposing exchange is cancelled only during this paid segment.
            // 保留有利换热，仅在本付费段抵消不利换热。
            double toAmbient = LogOnePlus(-fromAmbient * leak / speed) / leak;
            if (ticks > toAmbient) return settings.Ambient + direction * speed * (ticks - toAmbient);
            double position = speed / leak + (fromAmbient - speed / leak) * Math.Exp(-leak * ticks);
            return settings.Ambient + direction * position;
        }

        private static double LogOnePlus(double value)
        {
            // Preserve positive fractional target time for a tiny float temperature error.
            // float温差很小时仍保留正的分数tick到点时长。
            double sum = 1d + value;
            return sum == 1d ? value : Math.Log(sum) * value / (sum - 1d);
        }
    }
}
