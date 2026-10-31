// SPDX-License-Identifier: MPL-2.0
using System;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    public sealed partial class Window_CabinetThermostat
    {
        private const float LayoutGap = 8f;
        private const float PanelPadding = 10f;
        private const float ColumnGap = 16f;

        // Ephemeral geometry only. Resources, targets and permissions remain in the native cabinet snapshot.
        // 这里只保存本次绘制的几何量；资源、目标和权限仍取自原柜快照。
        private struct ThermostatLayout
        {
            internal Rect Temperatures, Target, Operating, Battery, Coolant, Supply, Environment;
            internal Rect BasicStock, Backup, Rapid, Development;
            internal float Height;
        }

        private static float TextHeight(string text, float width, float minimum = 22f, GameFont font = GameFont.Small)
        {
            GameFont oldFont = Text.Font;
            bool oldWrap = Text.WordWrap;
            try
            {
                Text.Font = font;
                Text.WordWrap = true;
                return Mathf.Max(minimum, Text.CalcHeight(text, Mathf.Max(1f, width)));
            }
            finally { Text.Font = oldFont; Text.WordWrap = oldWrap; }
        }

        private static float ButtonHeight(string text, float width)
        { return TextHeight(text, width - 16f, 20f) + 8f; }

        private static float CheckHeight(string text, float width)
        { return TextHeight(text, width - 28f, 28f); }

        private float HeaderHeight(float width)
        {
            return TextHeight(TitleFor(cabinet), width - 24f, 30f, GameFont.Medium)
                + TextHeight(cabinet.LabelCap, width, 22f);
        }

        private void DrawHeader(Rect rect)
        {
            float titleHeight = TextHeight(TitleFor(cabinet), rect.width - 24f, 30f, GameFont.Medium);
            Label(new Rect(rect.x, rect.y, rect.width - 24f, titleHeight), TitleFor(cabinet), Color.white, GameFont.Medium);
            Label(new Rect(rect.x, rect.y + titleHeight, rect.width, rect.height - titleHeight), cabinet.LabelCap, MutedColor);
        }

        private ThermostatLayout BuildLayout(float width, CabinetThermostatSnapshot state)
        {
            ThermostatLayout layout = new ThermostatLayout();
            float y;
            if (width >= 520f)
            {
                float temperatureWidth = Mathf.Clamp(width * 0.34f, 210f, 280f);
                float targetWidth = width - temperatureWidth - ColumnGap;
                float height = Mathf.Max(TemperaturesHeight(temperatureWidth, state), TargetHeight(targetWidth, state));
                layout.Temperatures = new Rect(0f, 0f, temperatureWidth, height);
                layout.Target = new Rect(temperatureWidth + ColumnGap, 0f, targetWidth, height);
                y = height;
            }
            else
            {
                float height = TemperaturesHeight(width, state);
                layout.Temperatures = new Rect(0f, 0f, width, height);
                float targetHeight = TargetHeight(width, state);
                layout.Target = new Rect(0f, height + LayoutGap, width, targetHeight);
                y = layout.Target.yMax;
            }
            y += LayoutGap;
            layout.Operating = new Rect(0f, y, width, OperatingHeight(width, state));
            y = layout.Operating.yMax + LayoutGap;

            if (SmartTier) y = LayoutSmartResources(ref layout, width, y, state);
            else
            {
                layout.BasicStock = new Rect(0f, y, width, BasicStockHeight(width, state));
                y = layout.BasicStock.yMax + LayoutGap;
                layout.Supply = new Rect(0f, y, width, SupplyHeight(width, state));
                y = layout.Supply.yMax;
            }
            y = LayoutModes(ref layout, width, y + LayoutGap, state);
            if (Prefs.DevMode)
            {
                layout.Development = new Rect(0f, y + LayoutGap, width, DevelopmentHeight(width));
                y = layout.Development.yMax;
            }
            layout.Height = y;
            return layout;
        }

        private float LayoutSmartResources(ref ThermostatLayout layout, float width, float y, CabinetThermostatSnapshot state)
        {
            if (width >= 760f)
            {
                float column = (width - ColumnGap) * 0.5f;
                layout.Battery = new Rect(0f, y, column, BatteryHeight(column, state));
                layout.Environment = new Rect(0f, layout.Battery.yMax + LayoutGap, column, EnvironmentHeight(column, state));
                layout.Coolant = new Rect(column + ColumnGap, y, column, CoolantHeight(column, state));
                layout.Supply = new Rect(column + ColumnGap, layout.Coolant.yMax + LayoutGap, column, SupplyHeight(column, state));
                return Mathf.Max(layout.Environment.yMax, layout.Supply.yMax);
            }
            layout.Battery = new Rect(0f, y, width, BatteryHeight(width, state));
            layout.Coolant = new Rect(0f, layout.Battery.yMax + LayoutGap, width, CoolantHeight(width, state));
            layout.Supply = new Rect(0f, layout.Coolant.yMax + LayoutGap, width, SupplyHeight(width, state));
            layout.Environment = new Rect(0f, layout.Supply.yMax + LayoutGap, width, EnvironmentHeight(width, state));
            return layout.Environment.yMax;
        }

        private static float LayoutModes(ref ThermostatLayout layout, float width, float y, CabinetThermostatSnapshot state)
        {
            string backup = "FAF_ThermostatCoolantBackup".Translate();
            string rapid = "FAF_RapidThermal".Translate();
            if (state.RapidThermalControlAvailable && width >= 520f)
            {
                float column = (width - ColumnGap) * 0.5f;
                float height = Mathf.Max(CheckHeight(backup, column), CheckHeight(rapid, column));
                layout.Backup = new Rect(0f, y, column, height);
                layout.Rapid = new Rect(column + ColumnGap, y, column, height);
                return y + height;
            }
            layout.Backup = new Rect(0f, y, width, CheckHeight(backup, width));
            if (!state.RapidThermalControlAvailable) return layout.Backup.yMax;
            layout.Rapid = new Rect(0f, layout.Backup.yMax + LayoutGap, width, CheckHeight(rapid, width));
            return layout.Rapid.yMax;
        }

        private void DrawBody(ThermostatLayout layout, CabinetThermostatSnapshot state)
        {
            DrawTemperatures(layout.Temperatures, state);
            DrawTarget(layout.Target, state);
            DrawOperatingState(layout.Operating, state);
            if (SmartTier)
            {
                DrawBattery(layout.Battery, state);
                DrawCoolant(layout.Coolant, state);
                DrawEnvironment(layout.Environment, state);
            }
            else DrawBasicStock(layout.BasicStock, state);
            DrawCoolantSupplySettings(layout.Supply, state);
            bool backup = state.CoolantBackupEnabled;
            Widgets.CheckboxLabeled(layout.Backup, "FAF_ThermostatCoolantBackup".Translate(), ref backup);
            if (backup != state.CoolantBackupEnabled) cabinet.SetCoolantBackupEnabled(backup);
            TooltipHandler.TipRegion(layout.Backup, "FAF_ThermostatCoolantBackupDesc".Translate());
            if (state.RapidThermalControlAvailable)
            {
                bool rapid = state.RapidThermalControlEnabled;
                Widgets.CheckboxLabeled(layout.Rapid, "FAF_RapidThermal".Translate(), ref rapid);
                if (rapid != state.RapidThermalControlEnabled) cabinet.SetRapidThermalControlEnabled(rapid);
                TooltipHandler.TipRegion(layout.Rapid, "FAF_RapidThermalDesc".Translate());
            }
            if (Prefs.DevMode) DrawDevelopment(layout.Development);
        }

        private static float TemperaturesHeight(float width, CabinetThermostatSnapshot state)
        {
            float column = (width - PanelPadding * 2f - LayoutGap) * 0.5f;
            float heading = Mathf.Max(TextHeight("FAF_ThermostatInside".Translate(), column),
                TextHeight("FAF_ThermostatOutside".Translate(), column));
            float value = Mathf.Max(TextHeight(state.InternalTemperatureCelsius.ToStringTemperature("F1"), column, 32f, GameFont.Medium),
                TextHeight(state.AmbientTemperatureCelsius.ToStringTemperature("F1"), column));
            return PanelPadding * 2f + heading + 4f + value;
        }

        private static void DrawTemperatures(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float column = (rect.width - PanelPadding * 2f - LayoutGap) * 0.5f;
            float heading = Mathf.Max(TextHeight("FAF_ThermostatInside".Translate(), column),
                TextHeight("FAF_ThermostatOutside".Translate(), column));
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding;
            Label(new Rect(x, y, column, heading), "FAF_ThermostatInside".Translate(), CyanColor);
            Label(new Rect(x + column + LayoutGap, y, column, heading), "FAF_ThermostatOutside".Translate(), MutedColor);
            y += heading + 4f;
            Label(new Rect(x, y, column, rect.yMax - PanelPadding - y),
                state.InternalTemperatureCelsius.ToStringTemperature("F1"), Color.white, GameFont.Medium);
            Label(new Rect(x + column + LayoutGap, y, column, rect.yMax - PanelPadding - y),
                state.AmbientTemperatureCelsius.ToStringTemperature("F1"), Color.white);
        }

        private static string TargetRange(CabinetThermostatSnapshot state, bool invalid)
        {
            return (invalid ? "FAF_TemperatureInvalid" : "FAF_ThermostatRange").Translate(
                state.MinimumTargetCelsius.ToStringTemperature("0.##"), state.MaximumTargetCelsius.ToStringTemperature("0.##"));
        }

        private static float TargetHeaderHeight(float width)
        {
            float inner = width - PanelPadding * 2f;
            float title = TextHeight("FAF_ThermostatTarget".Translate(), inner >= 310f ? inner - 142f : inner);
            return inner >= 310f ? Mathf.Max(28f, title) : title + 4f + 28f;
        }

        private static float TargetHeight(float width, CabinetThermostatSnapshot state)
        {
            float range = Mathf.Max(TextHeight(TargetRange(state, false), width - PanelPadding * 2f),
                TextHeight(TargetRange(state, true), width - PanelPadding * 2f));
            return PanelPadding * 2f + TargetHeaderHeight(width) + 4f + 22f + 4f + range;
        }

        private void DrawTarget(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding;
            float inner = rect.width - PanelPadding * 2f;
            float header = TargetHeaderHeight(rect.width);
            float titleHeight = TextHeight("FAF_ThermostatTarget".Translate(), inner >= 310f ? inner - 142f : inner);
            Label(new Rect(x, y, inner >= 310f ? inner - 142f : inner, titleHeight),
                "FAF_ThermostatTarget".Translate(), LilacColor);
            float fieldWidth = Mathf.Min(100f, Mathf.Max(1f, inner - 30f));
            Rect field = new Rect(rect.xMax - PanelPadding - fieldWidth - 30f,
                y + (inner >= 310f ? 0f : titleHeight + 4f), fieldWidth, 28f);
            DrawTargetField(field, state);
            Label(new Rect(field.xMax + 6f, field.y + 3f, 24f, 22f), TemperatureUnit(), MutedColor);
            y += header + 4f;
            float target = GenTemperature.CelsiusTo(state.TargetTemperatureCelsius, Prefs.TemperatureMode);
            float selected = Widgets.HorizontalSlider(new Rect(x, y, inner, 22f), target,
                GenTemperature.CelsiusTo(state.MinimumTargetCelsius, Prefs.TemperatureMode),
                GenTemperature.CelsiusTo(state.MaximumTargetCelsius, Prefs.TemperatureMode), middleAlignment: true);
            if (!Mathf.Approximately(target, selected) && cabinet.CanManageThermostat())
            {
                cabinet.SetTargetTemperatureCelsius(DisplayToCelsius(selected, Prefs.TemperatureMode));
                targetBuffer = FormatTarget(selected);
                targetBufferDirty = false;
                GUI.FocusControl(null);
                targetHadFocus = false;
            }
            y += 26f;
            bool invalid = targetBufferDirty && !TryReadTarget(state, out _);
            Label(new Rect(x, y, inner, rect.yMax - PanelPadding - y), TargetRange(state, invalid),
                invalid ? ColorLibrary.RedReadable : MutedColor);
        }

        private string Demand(CabinetThermostatSnapshot state)
        {
            return SmartTier ? "FAF_ThermalDemand".Translate(state.RefrigerationDemandWatts.ToString("0.#"),
                (state.ThermalDirection > 0 ? "FAF_Heating" : state.ThermalDirection < 0 ? "FAF_Cooling" : "FAF_Passive").Translate())
                : "FAF_CoolingDemand".Translate(state.RefrigerationDemandWatts.ToString("0.#"));
        }

        private float OperatingHeight(float width, CabinetThermostatSnapshot state)
        {
            string source = "FAF_ThermostatSupply".Translate(state.CoolingSourceKey.Translate());
            if (!SmartTier && width >= 520f)
                return Mathf.Max(TextHeight(source, width * 0.6f - LayoutGap), TextHeight(Demand(state), width * 0.4f));
            if (width < 300f)
                return TextHeight(source, width) + 2f + TextHeight(Demand(state), width) + 2f
                    + TextHeight((state.PoweredOn ? "FAF_ThermostatSwitchOn" : "FAF_ThermostatSwitchOff").Translate(), width);
            return TextHeight(source, width) + 2f + Mathf.Max(TextHeight(Demand(state), width - 140f),
                TextHeight((state.PoweredOn ? "FAF_ThermostatSwitchOn" : "FAF_ThermostatSwitchOff").Translate(), 132f));
        }

        private void DrawOperatingState(Rect rect, CabinetThermostatSnapshot state)
        {
            TooltipHandler.TipRegion(rect, "FAF_ThermostatDemandDesc".Translate());
            string source = "FAF_ThermostatSupply".Translate(state.CoolingSourceKey.Translate());
            if (!SmartTier && rect.width >= 520f)
            {
                Label(new Rect(rect.x, rect.y, rect.width * 0.6f - LayoutGap, rect.height), source, CyanColor);
                Label(new Rect(rect.x + rect.width * 0.6f, rect.y, rect.width * 0.4f, rect.height), Demand(state), Color.white);
                return;
            }
            float sourceHeight = TextHeight(source, rect.width);
            Label(new Rect(rect.x, rect.y, rect.width, sourceHeight), source, CyanColor);
            float y = rect.y + sourceHeight + 2f;
            if (rect.width < 300f)
            {
                y = DrawLine(rect.x, y, rect.width, Demand(state), Color.white) + 2f;
                DrawLine(rect.x, y, rect.width,
                    (state.PoweredOn ? "FAF_ThermostatSwitchOn" : "FAF_ThermostatSwitchOff").Translate(), MutedColor);
                return;
            }
            Label(new Rect(rect.x, y, rect.width - 140f, rect.yMax - y), Demand(state), Color.white);
            Label(new Rect(rect.xMax - 132f, y, 132f, rect.yMax - y),
                (state.PoweredOn ? "FAF_ThermostatSwitchOn" : "FAF_ThermostatSwitchOff").Translate(),
                MutedColor, GameFont.Small, TextAnchor.UpperRight);
        }

        private static string BatteryFlow(CabinetThermostatSnapshot state)
        {
            if (!state.BatteryNetDrainWatts.HasValue) return "FAF_ThermostatBatteryUnknown".Translate();
            if (state.BatteryNetDrainWatts.Value > 0f)
                return "FAF_ThermostatBatteryDrain".Translate(state.BatteryNetDrainWatts.Value.ToString("0.#"));
            if (state.BatteryNetDrainWatts.Value < 0f)
                return "FAF_ThermostatBatteryCharge".Translate((-state.BatteryNetDrainWatts.Value).ToString("0.#"));
            return "FAF_ThermostatBatteryIdle".Translate();
        }

        private static string BatteryAmount(CabinetThermostatSnapshot state)
        { return "FAF_ThermostatEnergy".Translate(state.BatteryStoredWattDays.ToString("0.#"), state.BatteryCapacityWattDays.ToString("0.#")); }

        private static string CoolantAmount(CabinetThermostatSnapshot state)
        { return "FAF_ThermostatCoolantAmount".Translate(state.CoolantStoredUnits.ToString("0.#"), state.CoolantCapacityUnits.ToString("0.#")); }

        private static string StockLabel(string amount, float stored)
        { return stored <= 0f ? amount + " · " + "FAF_ThermostatEmpty".Translate() : amount; }

        private static string CoolantTitle(CabinetThermostatSnapshot state)
        { return string.IsNullOrEmpty(state.CoolantLabel) ? "FAF_ThermostatCoolant".Translate().ToString() : state.CoolantLabel; }

        private static string CoolantFlow(CabinetThermostatSnapshot state)
        { return "FAF_CoolantThermalUse".Translate(state.CoolantConsumptionPerDay.ToString("0.##")); }

        private static string BatteryRemaining(CabinetThermostatSnapshot state)
        { return "FAF_ThermostatRemaining".Translate(Remaining(state.BatteryRemainingDays, state.BatteryStoredWattDays)); }

        private static string CoolantRemaining(CabinetThermostatSnapshot state)
        { return "FAF_ThermostatRemaining".Translate(Remaining(state.CoolantRemainingDays, state.CoolantStoredUnits)); }

        private static float BatteryHeaderHeight(float inner)
        {
            float button = ButtonHeight("FAF_ThermostatReconnect".Translate(), Mathf.Min(112f, inner));
            float title = TextHeight("FAF_ThermostatBattery".Translate(), inner >= 280f ? inner - 120f : inner);
            return inner >= 280f ? Mathf.Max(button, title) : title + 4f + button;
        }

        private static float BatteryHeight(float width, CabinetThermostatSnapshot state)
        {
            float inner = width - PanelPadding * 2f;
            float header = BatteryHeaderHeight(inner);
            return PanelPadding * 2f + header + 4f + TextHeight(StockLabel(BatteryAmount(state), state.BatteryStoredWattDays), inner)
                + 4f + TextHeight(BatteryFlow(state), inner) + 2f + TextHeight(BatteryRemaining(state), inner)
                + 4f + TextHeight(state.WiringModeKey.Translate(), inner);
        }

        private void DrawBattery(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding, inner = rect.width - PanelPadding * 2f;
            float header = BatteryHeaderHeight(inner);
            float title = TextHeight("FAF_ThermostatBattery".Translate(), inner >= 280f ? inner - 120f : inner);
            Label(new Rect(x, y, inner >= 280f ? inner - 120f : inner, title), "FAF_ThermostatBattery".Translate(), CyanColor);
            float buttonWidth = Mathf.Min(112f, inner);
            Rect reconnect = new Rect(rect.xMax - PanelPadding - buttonWidth,
                y + (inner >= 280f ? 0f : title + 4f), buttonWidth,
                ButtonHeight("FAF_ThermostatReconnect".Translate(), buttonWidth));
            if (Widgets.ButtonText(reconnect,
                "FAF_ThermostatReconnect".Translate(), active: cabinet.Spawned)) cabinet.ReconnectPower();
            y += header + 4f;
            string stock = StockLabel(BatteryAmount(state), state.BatteryStoredWattDays);
            float barHeight = TextHeight(stock, inner);
            DrawResourceBar(new Rect(x, y, inner, barHeight), stock, state.BatteryStoredWattDays, state.BatteryCapacityWattDays, BatteryFill);
            y += barHeight + 4f;
            y = DrawLine(x, y, inner, BatteryFlow(state), Color.white) + 2f;
            y = DrawLine(x, y, inner, BatteryRemaining(state), MutedColor) + 4f;
            DrawLine(x, y, inner, state.WiringModeKey.Translate(), MutedColor);
            TooltipHandler.TipRegion(rect, "FAF_ThermostatBatteryFlowDesc".Translate());
            if (!cabinet.Spawned) TooltipHandler.TipRegion(reconnect, "FAF_MapServiceUnavailable".Translate());
        }

        private static float CoolantActionsHeight(float width)
        {
            float part = (width - LayoutGap) * 0.5f;
            return Mathf.Max(ButtonHeight("FAF_ThermostatRefuel".Translate(), part),
                ButtonHeight("FAF_ThermostatReturn".Translate(), part));
        }

        private static float CoolantHeight(float width, CabinetThermostatSnapshot state)
        {
            float inner = width - PanelPadding * 2f;
            return PanelPadding * 2f + TextHeight(CoolantTitle(state), inner) + 4f
                + TextHeight(StockLabel(CoolantAmount(state), state.CoolantStoredUnits), inner) + 4f
                + TextHeight(CoolantFlow(state), inner) + 2f + TextHeight(CoolantRemaining(state), inner)
                + 6f + CoolantActionsHeight(inner);
        }

        private void DrawCoolant(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding, inner = rect.width - PanelPadding * 2f;
            y = DrawLine(x, y, inner, CoolantTitle(state), LilacColor) + 4f;
            string stock = StockLabel(CoolantAmount(state), state.CoolantStoredUnits);
            float height = TextHeight(stock, inner);
            DrawResourceBar(new Rect(x, y, inner, height), stock, state.CoolantStoredUnits, state.CoolantCapacityUnits, CoolantFill);
            y += height + 4f;
            y = DrawLine(x, y, inner, CoolantFlow(state), Color.white) + 2f;
            y = DrawLine(x, y, inner, CoolantRemaining(state), MutedColor) + 6f;
            float part = (inner - LayoutGap) * 0.5f;
            height = CoolantActionsHeight(inner);
            if (Widgets.ButtonText(new Rect(x, y, part, height), "FAF_ThermostatRefuel".Translate(), active: CanRefuelThermal(state)))
                RefuelThermalCoolant();
            Rect returnRect = new Rect(x + part + LayoutGap, y, part, height);
            if (Widgets.ButtonText(returnRect, "FAF_ThermostatReturn".Translate(), active: cabinet.Spawned && state.CoolantStoredUnits > 0f))
                cabinet.ReturnCoolantToMap();
            if (!cabinet.Spawned) TooltipHandler.TipRegion(returnRect, "FAF_MapServiceUnavailable".Translate());
        }

        private static void DrawResourceBar(Rect rect, string amount, float stored, float capacity, Color fill)
        {
            Widgets.DrawBoxSolid(rect, EmptyFill);
            float fraction = capacity > 0f ? Mathf.Clamp01(stored / capacity) : 0f;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * fraction, rect.height), fill);
            Label(rect, amount, Color.white, GameFont.Small, TextAnchor.MiddleCenter);
        }

        private static string CoolantTargetHint(CabinetThermostatSnapshot state)
        { return (state.AutomaticCoolantSupplyAvailable ? "FAF_CoolantTargetSharedHint" : "FAF_CoolantTargetManualHint").Translate(); }

        private static string CoolantTargetRange(CabinetThermostatSnapshot state, bool invalid)
        { return (invalid ? "FAF_CoolantTargetInvalid" : "FAF_CoolantTargetRange").Translate(state.CoolantCapacityUnits.ToString("0.##")); }

        private static float SupplyTargetHeaderHeight(float width)
        {
            float inner = width - PanelPadding * 2f;
            float title = TextHeight("FAF_CoolantRefillTarget".Translate(), inner >= 280f ? inner - 126f : inner);
            return inner >= 280f ? Mathf.Max(28f, title) : title + 4f + 28f;
        }

        private static float SupplyHeight(float width, CabinetThermostatSnapshot state)
        {
            float inner = width - PanelPadding * 2f;
            float range = Mathf.Max(TextHeight(CoolantTargetRange(state, false), inner), TextHeight(CoolantTargetRange(state, true), inner));
            float height = PanelPadding * 2f + SupplyTargetHeaderHeight(width) + 2f + range;
            if (state.AutomaticCoolantSupplyAvailable)
                height += 6f + CheckHeight("FAF_AutoCoolantEnable".Translate(), inner) + 2f
                    + TextHeight(state.AutomaticCoolantSupplyStatusKey.Translate(), inner);
            return height;
        }

        private void DrawCoolantSupplySettings(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding, inner = rect.width - PanelPadding * 2f;
            float header = SupplyTargetHeaderHeight(rect.width);
            float title = TextHeight("FAF_CoolantRefillTarget".Translate(), inner >= 280f ? inner - 126f : inner);
            Label(new Rect(x, y, inner >= 280f ? inner - 126f : inner, title), "FAF_CoolantRefillTarget".Translate(), LilacColor);
            float fieldWidth = Mathf.Min(118f, inner);
            DrawCoolantTargetField(new Rect(rect.xMax - PanelPadding - fieldWidth,
                y + (inner >= 280f ? 0f : title + 4f), fieldWidth, 28f), state);
            y += header + 2f;
            bool invalid = coolantTargetBufferDirty && !TryReadCoolantTarget(state, out _);
            float range = Mathf.Max(TextHeight(CoolantTargetRange(state, false), inner), TextHeight(CoolantTargetRange(state, true), inner));
            Label(new Rect(x, y, inner, range), CoolantTargetRange(state, invalid), invalid ? ColorLibrary.RedReadable : MutedColor);
            // Rule explanations belong to their controls; the actual supply status below stays visible.
            // 规则说明跟随对应控件；下方的实际供液状态仍直接显示。
            TooltipHandler.TipRegion(new Rect(x, rect.y + PanelPadding, inner, header + 2f + range), CoolantTargetHint(state));
            y += range;
            if (!state.AutomaticCoolantSupplyAvailable) return;
            y += 6f;
            float checkHeight = CheckHeight("FAF_AutoCoolantEnable".Translate(), inner);
            Rect automaticRect = new Rect(x, y, inner, checkHeight);
            bool automatic = state.AutomaticCoolantSupplyEnabled;
            Widgets.CheckboxLabeled(automaticRect, "FAF_AutoCoolantEnable".Translate(), ref automatic);
            if (automatic != state.AutomaticCoolantSupplyEnabled && cabinet.CanManageThermostat())
                cabinet.SetAutomaticCoolantSupplyEnabled(automatic);
            y += checkHeight + 2f;
            float statusHeight = TextHeight(state.AutomaticCoolantSupplyStatusKey.Translate(), inner);
            Label(new Rect(x, y, inner, statusHeight), state.AutomaticCoolantSupplyStatusKey.Translate(), CyanColor);
            TooltipHandler.TipRegion(new Rect(x, automaticRect.y, inner, checkHeight + 2f + statusHeight), "FAF_AutoCoolantCargoHint".Translate());
        }

        private static string EnvironmentHeading(CabinetThermostatSnapshot state)
        { return "FAF_EnvironmentTitle".Translate() + ": " + EnvironmentStatus(state); }

        private static string EnvironmentPressure(CabinetThermostatSnapshot state)
        {
            return state.EnvironmentKnown ? "FAF_EnvironmentVacuum".Translate(state.InternalVacuum.ToString("P0"), state.ExteriorVacuum.ToString("P0"))
                : "FAF_EnvironmentUnknown".Translate();
        }

        private static string EnvironmentDemand(CabinetThermostatSnapshot state)
        { return "FAF_EnvironmentLoad".Translate(state.LifeSupportDemandWatts.ToString("0.#"), state.TotalElectricalDemandWatts.ToString("0.#")); }

        private static string ActualElectricalUse(CabinetThermostatSnapshot state)
        {
            return state.ActualElectricalUseWatts.HasValue ? "FAF_ActualElectricalUse".Translate(ActualWatts(state.ActualElectricalUseWatts))
                : "FAF_ActualElectricalUseUnknown".Translate();
        }

        private static float EnvironmentHeight(float width, CabinetThermostatSnapshot state)
        {
            float inner = width - PanelPadding * 2f;
            return PanelPadding * 2f + TextHeight(EnvironmentHeading(state), inner) + 2f + TextHeight(EnvironmentPressure(state), inner)
                + 2f + TextHeight(EnvironmentDemand(state), inner) + 2f + TextHeight(ActualElectricalUse(state), inner);
        }

        private static void DrawEnvironment(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding, inner = rect.width - PanelPadding * 2f;
            y = DrawLine(x, y, inner, EnvironmentHeading(state), CyanColor) + 2f;
            y = DrawLine(x, y, inner, EnvironmentPressure(state), Color.white) + 2f;
            y = DrawLine(x, y, inner, EnvironmentDemand(state), Color.white) + 2f;
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, y - rect.y), "FAF_EnvironmentCaution".Translate());
            Rect actual = new Rect(x, y, inner, TextHeight(ActualElectricalUse(state), inner));
            Label(actual, ActualElectricalUse(state), Color.white);
            TooltipHandler.TipRegion(actual, "FAF_ActualElectricalUseDetails".Translate(ActualWatts(state.ActualThermalElectricalWatts),
                ActualWatts(state.ActualLifeSupportElectricalWatts), ActualWatts(state.ActualBatterySelfDischargeWatts)));
        }

        private static string ActualWatts(float? value) { return value.HasValue ? value.Value.ToString("0.#") : "—"; }

        private static string BasicBattery(CabinetThermostatSnapshot state)
        { return "FAF_BatteryStock".Translate(state.BatteryStoredWattDays.ToString("0.#"), state.BatteryCapacityWattDays.ToString("0.#")); }

        private static string BasicCoolant(CabinetThermostatSnapshot state)
        { return "FAF_CoolantStock".Translate(state.CoolantStoredUnits.ToString("0.##"), state.CoolantCapacityUnits.ToString("0.#")); }

        private static readonly string[] ServiceLabels = { "FAF_ThermostatReconnect", "FAF_ThermostatRefuel", "FAF_ThermostatReturn" };
        private static readonly string[] FooterLabels = { "FAF_CargoManagement", "FAF_SwitchOff", "CloseButton" };
        private static readonly string[] DevelopmentLabels = { "FAF_DevFillBattery", "FAF_DevEmptyBattery", "FAF_DevFillCoolant", "FAF_DevEmptyCoolant" };

        private static int ServiceColumns(float width) { return width >= 480f ? 3 : width >= 300f ? 2 : 1; }
        private static int FooterColumns(float width) { return width >= 430f ? 3 : width >= 280f ? 2 : 1; }
        private static int DevelopmentColumns(float width) { return width >= 760f ? 4 : width >= 340f ? 2 : 1; }

        private static float ActionRowHeight(string[] labels, float width, int columns, int row)
        {
            float part = (width - LayoutGap * (columns - 1)) / columns;
            float height = 28f;
            for (int i = row * columns; i < Mathf.Min(labels.Length, (row + 1) * columns); i++)
                height = Mathf.Max(height, ButtonHeight(labels[i].Translate(), part));
            return height;
        }

        private static float ActionRowsHeight(string[] labels, float width, int columns)
        {
            float height = 0f;
            int rows = (labels.Length + columns - 1) / columns;
            for (int row = 0; row < rows; row++) height += ActionRowHeight(labels, width, columns, row);
            return height + LayoutGap * (rows - 1);
        }

        private static float BasicStockHeight(float width, CabinetThermostatSnapshot state)
        {
            float inner = width - PanelPadding * 2f;
            float amounts = inner >= 480f ? Mathf.Max(TextHeight(BasicBattery(state), (inner - LayoutGap) * 0.5f),
                TextHeight(BasicCoolant(state), (inner - LayoutGap) * 0.5f))
                : TextHeight(BasicBattery(state), inner) + 2f + TextHeight(BasicCoolant(state), inner);
            return PanelPadding * 2f + amounts + 6f + ActionRowsHeight(ServiceLabels, inner, ServiceColumns(inner));
        }

        private void DrawBasicStock(Rect rect, CabinetThermostatSnapshot state)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            float x = rect.x + PanelPadding, y = rect.y + PanelPadding, inner = rect.width - PanelPadding * 2f;
            if (inner >= 480f)
            {
                float part = (inner - LayoutGap) * 0.5f;
                float height = Mathf.Max(TextHeight(BasicBattery(state), part), TextHeight(BasicCoolant(state), part));
                Label(new Rect(x, y, part, height), BasicBattery(state), CyanColor);
                Label(new Rect(x + part + LayoutGap, y, part, height), BasicCoolant(state), LilacColor);
                y += height;
            }
            else
            {
                y = DrawLine(x, y, inner, BasicBattery(state), CyanColor) + 2f;
                y = DrawLine(x, y, inner, BasicCoolant(state), LilacColor);
            }
            y += 6f;
            int columns = ServiceColumns(inner);
            float buttonWidth = (inner - LayoutGap * (columns - 1)) / columns;
            int rows = (ServiceLabels.Length + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                float height = ActionRowHeight(ServiceLabels, inner, columns, row);
                for (int i = row * columns; i < Mathf.Min(ServiceLabels.Length, (row + 1) * columns); i++)
                {
                    Rect button = new Rect(x + (i % columns) * (buttonWidth + LayoutGap), y, buttonWidth, height);
                    bool active = i == 0 ? cabinet.Spawned : i == 1 ? CanRefuelThermal(state) : cabinet.Spawned && state.CoolantStoredUnits > 0f;
                    if (Widgets.ButtonText(button, ServiceLabels[i].Translate(), active: active))
                    {
                        if (i == 0) cabinet.ReconnectPower();
                        else if (i == 1) RefuelThermalCoolant();
                        else cabinet.ReturnCoolantToMap();
                    }
                    if (!cabinet.Spawned && i != 1) TooltipHandler.TipRegion(button, "FAF_MapServiceUnavailable".Translate());
                }
                y += height + LayoutGap;
            }
        }

        private static float DevelopmentHeight(float width)
        { return ActionRowsHeight(DevelopmentLabels, width, DevelopmentColumns(width)); }

        private void DrawDevelopment(Rect rect)
        {
            int columns = DevelopmentColumns(rect.width);
            float part = (rect.width - LayoutGap * (columns - 1)) / columns;
            AcceptanceReport allowed = cabinet.CanUseDevelopmentResources();
            float y = rect.y;
            int rows = (DevelopmentLabels.Length + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                float height = ActionRowHeight(DevelopmentLabels, rect.width, columns, row);
                for (int i = row * columns; i < Mathf.Min(DevelopmentLabels.Length, (row + 1) * columns); i++)
                {
                    Action action = i == 0 ? (Action)cabinet.DevFillBattery : i == 1 ? cabinet.DevEmptyBattery
                        : i == 2 ? cabinet.DevFillCoolant : cabinet.DevEmptyCoolant;
                    CabinetCrewPanel.Button(new Rect(rect.x + (i % columns) * (part + LayoutGap), y, part, height), DevelopmentLabels[i], allowed, action);
                }
                y += height + LayoutGap;
            }
        }

        private string PowerActionLabel { get { return (cabinet.ThermalPowerRequestedOn ? "FAF_SwitchOff" : "FAF_SwitchOn").Translate(); } }

        private static float LayoutFooterRowHeight(float width, int columns, int row)
        {
            // Both power labels reserve enough height even when an external action changes the switch.
            // 电源被外部动作切换时，两种文案都预留足够行高。
            float part = (width - LayoutGap * (columns - 1)) / columns;
            float height = Mathf.Max(32f, ActionRowHeight(FooterLabels, width, columns, row));
            return row == 1 / columns ? Mathf.Max(height, ButtonHeight("FAF_SwitchOn".Translate(), part)) : height;
        }

        private static float FooterHeight(float width)
        {
            int columns = FooterColumns(width), rows = (FooterLabels.Length + columns - 1) / columns;
            float height = LayoutGap * (rows - 1);
            for (int row = 0; row < rows; row++) height += LayoutFooterRowHeight(width, columns, row);
            return height;
        }

        private void DrawFooter(Rect rect)
        {
            int columns = FooterColumns(rect.width);
            float part = (rect.width - LayoutGap * (columns - 1)) / columns;
            int rows = (FooterLabels.Length + columns - 1) / columns;
            float y = rect.y;
            for (int row = 0; row < rows; row++)
            {
                float height = LayoutFooterRowHeight(rect.width, columns, row);
                for (int i = row * columns; i < Mathf.Min(FooterLabels.Length, (row + 1) * columns); i++)
                {
                    Rect button = new Rect(rect.x + (i % columns) * (part + LayoutGap), y, part, height);
                    if (Widgets.ButtonText(button, i == 1 ? PowerActionLabel : FooterLabels[i].Translate().ToString()))
                    {
                        if (i == 0) CabinetCargoUI.Open(cabinet);
                        else if (i == 1) cabinet.RequestThermalPowerSwitch(!cabinet.ThermalPowerRequestedOn);
                        else Close();
                    }
                    if (i == 1 && cabinet.ThermalPowerSwitchPending) TooltipHandler.TipRegion(button, "FAF_PowerSwitchPending".Translate());
                }
                y += height + LayoutGap;
            }
        }

        private static float DrawLine(float x, float y, float width, string text, Color color)
        {
            float height = TextHeight(text, width);
            Label(new Rect(x, y, width, height), text, color);
            return y + height;
        }

        private static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            try
            {
                GUI.color = color;
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = true;
                Widgets.Label(rect, text);
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; }
        }
    }
}
