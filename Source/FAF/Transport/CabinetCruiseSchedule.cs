// SPDX-License-Identifier: MPL-2.0
namespace FAF.Transport
{
using System;

// Immutable pure schedule: validate once, then sample. / 不可变纯时间表：构造校验一次，之后仅采样。
public sealed class CabinetCruiseSchedule
{
    private readonly double acceleration;
    private readonly double braking;
    private readonly double area;
    public int DurationTicks { get; }
    public double EffectivePeakRadiansPerTick { get; }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
    private static double Smooth(double x) => x * x * (3.0 - 2.0 * x);
    private static double Integral(double x) => x * x * x * (1.0 - 0.5 * x);
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

    // Restore with the saved parameters and saved total, never current Def values.
    // 读档传入保存的参数与总时长，不改用当前Def重新安排航程。
    public CabinetCruiseSchedule(double angleRadians, double peakRadiansPerTick,
        double accelerationFraction, double brakingFraction, int minimumTicks,
        int? savedDurationTicks = null, int additionalTicks = 0)
    {
        if (!Finite(angleRadians) || angleRadians < 0.0
            || !Finite(peakRadiansPerTick) || peakRadiansPerTick <= 0.0
            || !Finite(accelerationFraction) || accelerationFraction <= 0.0
            || !Finite(brakingFraction) || brakingFraction <= 0.0
            || accelerationFraction + brakingFraction >= 1.0 || minimumTicks < 0 || additionalTicks < 0)
            throw new ArgumentOutOfRangeException();

        acceleration = accelerationFraction;
        braking = brakingFraction;
        area = 1.0 - 0.5 * (acceleration + braking);
        // Cross-layer overhead stretches this same curve, without an extra empty horizontal minimum.
        // 跨层预算延长同一条曲线，不另叠一段空的水平最短航时。
        double required = additionalTicks + Math.Max(minimumTicks,
            Math.Ceiling(angleRadians / (peakRadiansPerTick * area)));
        if (!Finite(required) || required < 1.0 || required > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(peakRadiansPerTick));

        DurationTicks = savedDurationTicks ?? (int)required;
        if (DurationTicks < required)
            throw new ArgumentOutOfRangeException(nameof(savedDurationTicks));
        EffectivePeakRadiansPerTick = angleRadians / (DurationTicks * area);
    }

    // Only elapsed integer ticks enter the public sampling path. / 公开采样只接收经过的整数tick。
    public Sample At(long elapsedTicks)
    {
        if (elapsedTicks <= 0) return new Sample(0.0, 0.0, DurationTicks);
        if (elapsedTicks >= DurationTicks) return new Sample(1.0, 0.0, 0);
        double u = (double)elapsedTicks / DurationTicks;
        double brakeStart = 1.0 - braking;
        double cumulative, shape;
        if (u < acceleration)
        {
            double x = u / acceleration;
            cumulative = acceleration * Integral(x);
            shape = Smooth(x);
        }
        else if (u <= brakeStart)
        {
            cumulative = 0.5 * acceleration + u - acceleration;
            shape = 1.0;
        }
        else
        {
            double x = Clamp01((u - brakeStart) / braking);
            cumulative = 0.5 * acceleration + brakeStart - acceleration
                + braking * (x - Integral(x));
            shape = 1.0 - Smooth(x);
        }
        return new Sample(Clamp01(cumulative / area),
            EffectivePeakRadiansPerTick * shape, DurationTicks - (int)elapsedTicks);
    }

    public readonly struct Sample
    {
        public readonly double Progress;
        public readonly double SpeedRadiansPerTick;
        public readonly int RemainingTicks;
        internal Sample(double progress, double speed, int remainingTicks)
        {
            Progress = progress;
            SpeedRadiansPerTick = speed;
            RemainingTicks = remainingTicks;
        }
    }
}
}
