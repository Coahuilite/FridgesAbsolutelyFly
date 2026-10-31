// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Shared live item readback for installed and portable contents, with no simulated timers. / 安装与便携内容页共用的实体读数，不另行模拟计时。</summary>
    public static class CabinetItemDetails
    {
        public static IEnumerable<string> ReadLines(Thing stored)
        {
            Thing observed = stored.GetInnerIfMinified();
            float temperature = observed.AmbientTemperature;
            CompRottable rot = observed.TryGetComp<CompRottable>();
            float temperatureRate = GenTemperature.RotRateAtTemperature(temperature);
            string condition = "FAF_PreservationNotApplicable".Translate();
            if (rot != null)
                condition = (temperatureRate <= 0f ? "FAF_PreservationFrozen"
                    : temperatureRate < 1f ? "FAF_PreservationChilled" : "FAF_PreservationNormal").Translate();
            yield return "FAF_PreservationTemperature".Translate(temperature.ToStringTemperature("F2"), condition);
            if (rot == null) yield return "FAF_PreservationNoRottable".Translate();
            else
            {
                string stage = (rot.Stage == RotStage.Fresh ? "RotStateFresh"
                    : rot.Stage == RotStage.Rotting ? "RotStateRotting" : "RotStateDessicated").Translate();
                yield return "FAF_PreservationProgress".Translate(stage, rot.RotProgressPct.ToStringPercent("F1"));
                float actualRate = rot.Active ? temperatureRate : 0f;
                string remaining;
                if (rot.Stage != RotStage.Fresh) remaining = "FAF_PreservationAlreadyRotten".Translate();
                else if (!rot.Active) remaining = "FAF_PreservationInactive".Translate();
                else if (actualRate <= 0f) remaining = "FAF_PreservationStopped".Translate();
                else remaining = rot.TicksUntilRotAtTemp(temperature).ToStringTicksToPeriod();
                yield return "FAF_PreservationRate".Translate(actualRate.ToString("G4"), remaining);
            }

            CompTemperatureRuinable damage = observed.TryGetComp<CompTemperatureRuinable>();
            if (damage != null)
            {
                yield return "FAF_TemperatureDamageProgress".Translate(Integration.NativeTemperatureDamage.Progress(damage).ToStringPercent("F1"));
                string nativeWarning = damage.CompInspectStringExtra();
                if (!nativeWarning.NullOrEmpty()) yield return nativeWarning;
                yield return "FAF_TemperatureDamageSafeRange".Translate(
                    damage.Props.minSafeTemperature.ToStringTemperature("F1"),
                    damage.Props.maxSafeTemperature.ToStringTemperature("F1"));
            }
            CompHatcher hatcher = observed.TryGetComp<CompHatcher>();
            if (hatcher != null)
            {
                // Preserve the race/game's own hatching progress, time and temperature-damage condition.
                // 保留游戏自身的孵化进度、剩余时间与温度损坏判定。
                string nativeHatching = hatcher.CompInspectStringExtra();
                if (!nativeHatching.NullOrEmpty()) yield return nativeHatching;
                else if (hatcher.TemperatureDamaged) yield return "FAF_HatchingTemperatureDamaged".Translate();
            }
        }

        public static void Draw(Thing stored, float width, ref float curY, float indent = 36f)
        {
            foreach (string line in ReadLines(stored)) DrawLine(width, ref curY, line, indent);
        }

        public static void DrawLine(float width, ref float curY, string text, float indent = 36f)
        {
            float height = Mathf.Max(24f, Text.CalcHeight(text, width - indent - 8f));
            Rect rect = new Rect(indent, curY, width - indent - 8f, height);
            Widgets.Label(rect, text);
            TooltipHandler.TipRegion(rect, "FAF_PreservationRateTip".Translate());
            curY += height;
        }
    }
}
