// SPDX-License-Identifier: MPL-2.0
using System;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>
    /// A native, draggable view of the cabinet's authoritative resource model.
    /// 可拖动的原生窗体，只展示柜体权威资源模型，不另行计算制冷或续航。
    /// </summary>
    public sealed partial class Window_CabinetThermostat : Window
    {
        private const string TemperatureControlName = "FAF.Thermostat.Target";
        private const string CoolantTargetControlName = "FAF.Thermostat.CoolantTarget";
        private static readonly Color PanelColor = new Color(0.105f, 0.13f, 0.155f);
        private static readonly Color MutedColor = new Color(0.73f, 0.79f, 0.84f);
        private static readonly Color CyanColor = new Color(0.47f, 0.88f, 0.95f);
        private static readonly Color LilacColor = new Color(0.77f, 0.70f, 0.96f);
        private static readonly Color BatteryFill = new Color(0.18f, 0.43f, 0.49f);
        private static readonly Color CoolantFill = new Color(0.35f, 0.29f, 0.47f);
        private static readonly Color EmptyFill = new Color(0.065f, 0.075f, 0.09f);

        private readonly Building_FlyingFridgeCabinet cabinet;
        private Vector2 scrollPosition;
        private string targetBuffer;
        private bool targetHadFocus;
        private bool targetBufferDirty;
        private TemperatureDisplayMode bufferTemperatureMode;
        private string coolantTargetBuffer;
        private bool coolantTargetHadFocus, coolantTargetBufferDirty;

        internal static string TitleFor(Building_FlyingFridgeCabinet cabinet)
        {
            return (cabinet.UpgradeProfile.HeatingUnlocked ? "FAF_ThermostatTitle" : "FAF_CoolingRegulatorTitle").Translate();
        }
        public static void OpenFor(Building_FlyingFridgeCabinet cabinet)
        {
            if (!CabinetAccess.CanManage(cabinet) || !cabinet.CanManageThermostat()) return;
            foreach (Window window in Find.WindowStack.Windows)
            {
                Window_CabinetThermostat existing = window as Window_CabinetThermostat;
                if (existing != null && existing.cabinet == cabinet && existing.CabinetIsValid)
                {
                    Find.WindowStack.Notify_ManuallySetFocus(existing);
                    return;
                }
            }
            Find.WindowStack.Add(new Window_CabinetThermostat(cabinet));
        }

        public Window_CabinetThermostat(Building_FlyingFridgeCabinet cabinet)
        {
            this.cabinet = cabinet;
            draggable = true;
            doCloseX = true;
            // WindowStack only dispatches Enter to closeOnAccept windows; our override submits without closing.
            // WindowStack 只向该标记为真的窗体派发 Enter；本类覆写只提交，不关闭。
            closeOnAccept = true;
            closeOnCancel = true;
            onlyOneOfTypeAllowed = true;
            closeOnClickedOutside = false;
            // Leave native HUD time controls and outside map clicks available without closing the panel.
            // 不拦截窗外地图点击或原生 HUD 时间控件；点击窗外也不关闭面板。
            absorbInputAroundWindow = false;
            forcePause = false;
        }

        private bool SmartTier { get { return cabinet.UpgradeProfile.HeatingUnlocked; } }

        // Native UI units, not physical pixels; clamp only the viewport on smaller screens.
        // 使用原生UI逻辑单位而非物理像素；小屏只限制视口，不缩小字号。
        public override Vector2 InitialSize
        {
            get
            {
                float height = SmartTier ? 820f : cabinet.UpgradeProfile.SupplyAutomationUnlocked ? 680f : Prefs.DevMode ? 620f : 560f;
                return new Vector2(Mathf.Min(SmartTier ? 880f : 600f, Verse.UI.screenWidth - 32f),
                    Mathf.Min(height, Verse.UI.screenHeight - 40f));
            }
        }

        private bool CabinetIsValid { get { return CabinetAccess.CanManage(cabinet); } }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!CabinetIsValid) { Close(); return; }
            Vector2 preferred = InitialSize;
            if (windowRect.width == preferred.x && windowRect.height == preferred.y) return;
            // Research, developer mode and UI-scale changes reflow the same window and real controls.
            // 科研、开发模式或UI缩放变化只重排同窗体的真实控件。
            windowRect.size = preferred;
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Verse.UI.screenWidth - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Verse.UI.screenHeight - windowRect.height));
            scrollPosition = Vector2.zero;
        }

        public override void PreClose()
        {
            // X, Close, Escape and WindowStack replacement all pass this native boundary.
            // X、关闭按钮、Escape 和 WindowStack 替换旧窗体都会经过此原生边界。
            if (CabinetIsValid) FinishEditing();
            ClearInputFocus();
            base.PreClose();
        }

        public override void Notify_ClickOutsideWindow()
        {
            if (CabinetIsValid) FinishEditing();
            ClearInputFocus();
            base.Notify_ClickOutsideWindow();
        }

        internal static bool TargetInputHasFocus
        {
            get
            {
                if (!IsTargetControl(GUI.GetNameOfFocusedControl())) return false;
                foreach (Window window in Find.WindowStack.Windows)
                {
                    Window_CabinetThermostat thermostat = window as Window_CabinetThermostat;
                    if (thermostat != null && thermostat.CabinetIsValid) return true;
                }
                return false;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!CabinetIsValid) { Close(); return; }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            CabinetThermostatSnapshot state = cabinet.GetThermostatSnapshot();
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                float headerHeight = HeaderHeight(inRect.width);
                float footerHeight = FooterHeight(inRect.width);
                Rect footer = new Rect(0f, Mathf.Max(headerHeight + LayoutGap, inRect.height - footerHeight),
                    inRect.width, footerHeight);
                Rect viewport = new Rect(0f, headerHeight + LayoutGap, inRect.width,
                    Mathf.Max(0f, footer.y - headerHeight - LayoutGap * 2f));
                DrawHeader(new Rect(0f, 0f, inRect.width, headerHeight));

                ThermostatLayout layout = BuildLayout(viewport.width, state);
                bool scrolling = layout.Height > viewport.height;
                float contentWidth = Mathf.Max(1f, viewport.width - (scrolling ? 18f : 0f));
                if (scrolling) layout = BuildLayout(contentWidth, state);
                else scrollPosition = Vector2.zero;
                // Only the body scrolls. Content never exceeds the viewport width, so no horizontal bar.
                // 仅正文滚动；正文宽不超出视口，因此不会出现横向滚动条。
                Rect content = new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, layout.Height));
                Widgets.BeginScrollView(viewport, ref scrollPosition, content);
                try { DrawBody(layout, state); }
                finally { Widgets.EndScrollView(); }
                DrawFooter(footer);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
            }
        }

        private bool CanRefuelThermal(CabinetThermostatSnapshot state)
        {
            return cabinet.CanRefuelFromContents().Accepted || (cabinet.CanManageThermostat().Accepted
                && state.CoolantBackupEnabled && cabinet.CanRefuelForLaunch
                && (cabinet.Spawned || (CabinetAccess.CanTransferItems(cabinet) && CabinetAccess.InventoryPawns(cabinet).Any())));
        }
        private void RefuelThermalCoolant()
        {
            if (!cabinet.CanManageThermostat()) return;
            if (cabinet.Spawned) cabinet.RequestCoolantRefuel();
            else
            {
                cabinet.RefuelThermalCoolantFromContents();
                if (cabinet.CoolantBackupEnabled && CabinetAccess.CanTransferItems(cabinet))
                    foreach (Pawn pawn in CabinetAccess.InventoryPawns(cabinet))
                        if (pawn.inventory != null) cabinet.RefuelLaunchCoolantFrom(pawn.inventory.innerContainer, int.MaxValue);
            }
        }

        private void DrawTargetField(Rect rect, CabinetThermostatSnapshot state)
        {
            bool focused = GUI.GetNameOfFocusedControl() == TemperatureControlName;
            // Match native text search focus release, including clicks already consumed by another widget.
            // 与原生搜索框一样释放输入焦点，也处理已被其他控件消费的点击。
            if (focused && OriginalEventUtility.EventType == EventType.MouseDown
                && !rect.Contains(Event.current.mousePosition))
            {
                FinishTargetEditing();
                GUI.FocusControl(null);
                targetHadFocus = false;
                focused = false;
            }
            // Do not replace '-', '1.' or a partially selected value with a fresh snapshot every frame.
            // 不用每帧快照覆盖“-”“1.”或正在编辑的值，避免输入中间态丢失。
            if (targetBuffer == null || bufferTemperatureMode != Prefs.TemperatureMode || (!focused && !targetHadFocus))
                ResetTargetBuffer(state.TargetTemperatureCelsius);
            if (targetHadFocus && !focused)
            {
                FinishTargetEditing();
            }
            GUI.SetNextControlName(TemperatureControlName);
            string edited = Widgets.TextField(rect, targetBuffer, 16);
            if (edited != targetBuffer)
            {
                targetBuffer = edited;
                targetBufferDirty = true;
                float celsius;
                if (TryReadTarget(state, out celsius) && cabinet.CanManageThermostat()) cabinet.SetTargetTemperatureCelsius(celsius);
            }
            targetHadFocus = GUI.GetNameOfFocusedControl() == TemperatureControlName;
        }

        public override void OnAcceptKeyPressed()
        {
            if (CabinetIsValid)
            {
                FinishEditing();
                ClearInputFocus();
            }
            Event.current.Use();
        }

        private void FinishEditing()
        {
            FinishTargetEditing();
            FinishCoolantTargetEditing();
        }

        private static bool IsTargetControl(string name)
        { return name == TemperatureControlName || name == CoolantTargetControlName; }

        private void ClearInputFocus()
        {
            if (IsTargetControl(GUI.GetNameOfFocusedControl())) GUI.FocusControl(null);
            targetHadFocus = false;
            coolantTargetHadFocus = false;
        }

        private void FinishTargetEditing()
        {
            CabinetThermostatSnapshot state = cabinet.GetThermostatSnapshot();
            float celsius;
            if (targetBufferDirty && TryReadTarget(state, out celsius) && cabinet.CanManageThermostat()) cabinet.SetTargetTemperatureCelsius(celsius);
            ResetTargetBuffer(cabinet.GetThermostatSnapshot().TargetTemperatureCelsius);
        }

        private bool TryReadTarget(CabinetThermostatSnapshot state, out float celsius)
        {
            float displayed;
            bool parsed = float.TryParse(targetBuffer, NumberStyles.Float, CultureInfo.CurrentCulture, out displayed)
                || float.TryParse(targetBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out displayed);
            celsius = parsed ? DisplayToCelsius(displayed, bufferTemperatureMode) : float.NaN;
            return !float.IsNaN(celsius) && !float.IsInfinity(celsius)
                && celsius >= state.MinimumTargetCelsius && celsius <= state.MaximumTargetCelsius;
        }

        private void ResetTargetBuffer(float celsius)
        {
            targetBuffer = FormatTarget(GenTemperature.CelsiusTo(celsius, Prefs.TemperatureMode));
            bufferTemperatureMode = Prefs.TemperatureMode;
            targetBufferDirty = false;
        }

        private static string FormatTarget(float displayed) { return displayed.ToString("0.##", CultureInfo.CurrentCulture); }

        private static float DisplayToCelsius(float displayed, TemperatureDisplayMode mode)
        {
            // Native absolute-zero offset plus native scale conversion also handles Fahrenheit and Kelvin.
            // 使用原生零点偏移和原生温差转换，同时支持华氏与开尔文，不复制换算公式。
            return GenTemperature.ConvertTemperatureOffset(displayed - GenTemperature.CelsiusTo(0f, mode),
                mode, TemperatureDisplayMode.Celsius);
        }

        private static string TemperatureUnit()
        {
            switch (Prefs.TemperatureMode)
            {
                case TemperatureDisplayMode.Fahrenheit: return "F";
                case TemperatureDisplayMode.Kelvin: return "K";
                default: return "C";
            }
        }

        internal static string EnvironmentStatus(CabinetThermostatSnapshot state)
        {
            string power = (state.LifeSupportPowered ? "FAF_EnvironmentPowered" : "FAF_EnvironmentUnpowered").Translate();
            if (state.HasActualOccupants) return power;
            return state.RestPreparationActive ? "FAF_EnvironmentPreparingKnown".Translate(power).ToString()
                : "FAF_EnvironmentStandby".Translate().ToString();
        }
        private void DrawCoolantTargetField(Rect rect, CabinetThermostatSnapshot state)
        {
            bool focused = GUI.GetNameOfFocusedControl() == CoolantTargetControlName;
            if (focused && OriginalEventUtility.EventType == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
            {
                FinishCoolantTargetEditing();
                GUI.FocusControl(null);
                coolantTargetHadFocus = false;
                focused = false;
            }
            if (coolantTargetHadFocus && !focused) FinishCoolantTargetEditing();
            if (coolantTargetBuffer == null || (!focused && !coolantTargetHadFocus))
                ResetCoolantTargetBuffer(state.CoolantTargetUnits);
            GUI.SetNextControlName(CoolantTargetControlName);
            string edited = Widgets.TextField(rect, coolantTargetBuffer, 16);
            if (edited != coolantTargetBuffer)
            {
                coolantTargetBuffer = edited;
                coolantTargetBufferDirty = true;
                if (TryReadCoolantTarget(state, out float units) && cabinet.CanManageThermostat())
                {
                    cabinet.SetCoolantTargetUnits(units);
                    // Valid keystrokes are already committed. Blur must not overwrite a newer Gizmo drag.
                    // 合法输入已立即提交；失焦不能把其旧文本回写，覆盖较新的Gizmo拖动。
                    coolantTargetBufferDirty = false;
                }
            }
            coolantTargetHadFocus = GUI.GetNameOfFocusedControl() == CoolantTargetControlName;
        }

        private bool TryReadCoolantTarget(CabinetThermostatSnapshot state, out float units)
        {
            bool parsed = float.TryParse(coolantTargetBuffer, NumberStyles.Float, CultureInfo.CurrentCulture, out units)
                || float.TryParse(coolantTargetBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out units);
            return parsed && !float.IsNaN(units) && !float.IsInfinity(units) && units >= 0f && units <= state.CoolantCapacityUnits;
        }

        private void FinishCoolantTargetEditing()
        {
            CabinetThermostatSnapshot state = cabinet.GetThermostatSnapshot();
            if (coolantTargetBufferDirty && TryReadCoolantTarget(state, out float units) && cabinet.CanManageThermostat())
                cabinet.SetCoolantTargetUnits(units);
            ResetCoolantTargetBuffer(cabinet.GetThermostatSnapshot().CoolantTargetUnits);
        }

        private void ResetCoolantTargetBuffer(float units)
        {
            coolantTargetBuffer = units.ToString("0.##", CultureInfo.CurrentCulture);
            coolantTargetBufferDirty = false;
        }

        private static string Remaining(float? days, float stored)
        {
            if (stored <= 0f) return "FAF_ThermostatEmpty".Translate();
            if (!days.HasValue || float.IsNaN(days.Value)) return "FAF_ThermostatNoSample".Translate();
            if (float.IsPositiveInfinity(days.Value)) return "FAF_ThermostatInfinite".Translate();
            return days.Value < 1f ? "FAF_ThermostatHours".Translate((Mathf.Max(0f, days.Value) * 24f).ToString("0.#"))
                : "FAF_ThermostatDays".Translate(days.Value.ToString("0.#"));
        }

    }

    // Native TimeControls runs before Window contents and does not guard ordinary text fields.
    // 原生 TimeControls 先于窗体内容执行，且不保护普通文本框。
    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    internal static class ThermostatTimeInputPatch
    {
        private static bool Prefix()
        {
            // Only leave focused temperature/supply target keystrokes for the real TextField. Mouse controls,
            // repaint and every unfocused time shortcut still use the unchanged native method.
            // 仅将聚焦温度／供液目标框时的按键留给真实 TextField；鼠标控件、重绘及失焦后的
            // 所有时间快捷键均继续执行未改动的原生方法。
            return Event.current.type != EventType.KeyDown
                || (!Window_CabinetThermostat.TargetInputHasFocus && !Window_FridgeAttitude.SpeedInputHasFocus);
        }
    }
}
