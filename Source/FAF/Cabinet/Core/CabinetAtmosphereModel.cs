// SPDX-License-Identifier: MPL-2.0
using System;

namespace FAF.Cabinet.Core
{
    /// <summary>Gameplay pressure retention; vacuum 0 is breathable, 1 is the exterior vacuum limit. / 游戏气密保持；真空度0为有气氛、1为外界真空极限。</summary>
    internal static class CabinetAtmosphereModel
    {
        internal static float Advance(float inside, float outside, float poweredTicks, int ticks,
            float leakTimeConstant, float recoveryTimeConstant)
        {
            if (ticks <= 0) return inside;
            // Exchange continues during powered operation; electrical restoration then counters that leak.
            // 供电时仍发生被动泄漏，再由电力恢复对抗泄漏。
            double leakRate = 1d / leakTimeConstant;
            double recoveryRate = Math.Max(0d, Math.Min(ticks, poweredTicks)) / ticks / recoveryTimeConstant;
            double rate = leakRate + recoveryRate;
            double equilibrium = outside * leakRate / rate;
            double pressure = equilibrium + (inside - equilibrium) * Math.Exp(-rate * ticks);
            return (float)Math.Max(0d, Math.Min(1d, pressure));
        }
    }
}
