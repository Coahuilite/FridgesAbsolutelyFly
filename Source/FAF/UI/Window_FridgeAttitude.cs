// SPDX-License-Identifier: MPL-2.0
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Signed display units over the existing per-tick flight setting. / 在既有每tick设置上使用带符号的显示单位。</summary>
    public sealed class Window_FridgeAttitude : Window
    {
        private const string SpeedControlName = "FAF.Flight.AngularSpeed";
        private const float MaximumDegreesPerSecond = 90f;
        private readonly CompFridgeFlight flight;
        private string speedBuffer;
        private bool hadFocus, bufferDirty;
        private Vector2 scrollPosition;
        private bool IsValid { get { return flight != null && flight.parent != null && !flight.parent.Destroyed && flight.parent.Spawned; } }
        private float DegreesPerSecond { get { return flight.AngularSpeed * GenTicks.TicksPerRealSecond; } }

        public Window_FridgeAttitude(CompFridgeFlight flight)
        {
            this.flight = flight;
            draggable = true;
            doCloseX = true;
            closeOnAccept = true;
            closeOnCancel = true;
            onlyOneOfTypeAllowed = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            forcePause = false;
        }
        public override Vector2 InitialSize { get { return new Vector2(Mathf.Min(440f, Verse.UI.screenWidth - 32f), Mathf.Min(306f, Verse.UI.screenHeight - 40f)); } }
        public override void WindowUpdate() { base.WindowUpdate(); if (!IsValid) Close(); }
        public override void PreClose()
        {
            if (IsValid) FinishEditing();
            ClearFocus();
            base.PreClose();
        }
        public override void Notify_ClickOutsideWindow()
        {
            if (IsValid) FinishEditing();
            ClearFocus();
            base.Notify_ClickOutsideWindow();
        }
        internal static bool SpeedInputHasFocus
        {
            get
            {
                if (GUI.GetNameOfFocusedControl() != SpeedControlName) return false;
                foreach (Window window in Find.WindowStack.Windows)
                    if (window is Window_FridgeAttitude attitude && attitude.IsValid) return true;
                return false;
            }
        }
        public override void DoWindowContents(Rect rect)
        {
            if (!IsValid) { Close(); return; }
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap;
            Color color = GUI.color;
            bool verticalScroll = rect.height < 270f;
            Rect content = new Rect(0f, 0f, Mathf.Max(400f, rect.width - (verticalScroll ? 18f : 0f)), 270f);
            bool scrolling = verticalScroll || content.width > rect.width;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                if (scrolling) Widgets.BeginScrollView(rect, ref scrollPosition, content);
                try { DrawControls(content); }
                finally { if (scrolling) Widgets.EndScrollView(); }
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }
        private void DrawControls(Rect rect)
        {
            Widgets.Label(new Rect(0f, 0f, rect.width, 28f), "FAF_AngularSpeed".Translate());
            Widgets.Label(new Rect(0f, 35f, rect.width - 135f, 28f), "FAF_AngularSpeedValue".Translate(DegreesPerSecond.ToString("0.##")));
            DrawSpeedField(new Rect(rect.width - 125f, 32f, 125f, 30f));
            float before = DegreesPerSecond;
            float selected = Widgets.HorizontalSlider(new Rect(0f, 78f, rect.width, 24f), before,
                -MaximumDegreesPerSecond, MaximumDegreesPerSecond, middleAlignment: true);
            if (!Mathf.Approximately(before, selected)) SetSpeed(selected);
            Widgets.Label(new Rect(0f, 105f, rect.width / 3f, 48f), "FAF_RotateCounterclockwise".Translate());
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.width / 3f, 105f, rect.width / 3f, 48f), "FAF_RotateHold".Translate());
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(rect.width * 2f / 3f, 105f, rect.width / 3f, 48f), "FAF_RotateClockwise".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            bool invalid = bufferDirty && !TryReadSpeed(out _);
            GUI.color = invalid ? ColorLibrary.RedReadable : Color.white;
            Widgets.Label(new Rect(0f, 166f, rect.width, 48f),
                (invalid ? "FAF_AngularInvalidHint" : "FAF_AngularZeroHint").Translate());
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(0f, 228f, 100f, 30f), "FAF_RotateSetZero".Translate())) SetSpeed(0f);
            if (Widgets.ButtonText(new Rect(rect.width - 100f, 228f, 100f, 30f), "CloseButton".Translate())) Close();
        }
        private void DrawSpeedField(Rect rect)
        {
            bool focused = GUI.GetNameOfFocusedControl() == SpeedControlName;
            if (focused && OriginalEventUtility.EventType == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
            {
                FinishEditing(); ClearFocus(); focused = false;
            }
            if (hadFocus && !focused) FinishEditing();
            if (speedBuffer == null || (!focused && !hadFocus)) ResetBuffer();
            GUI.SetNextControlName(SpeedControlName);
            string edited = Widgets.TextField(rect, speedBuffer, 18);
            if (edited != speedBuffer)
            {
                speedBuffer = edited; bufferDirty = true;
                if (TryReadSpeed(out float value)) flight.AngularSpeed = value / GenTicks.TicksPerRealSecond;
            }
            hadFocus = GUI.GetNameOfFocusedControl() == SpeedControlName;
        }
        public override void OnAcceptKeyPressed()
        {
            if (IsValid) FinishEditing();
            ClearFocus();
            Event.current.Use();
        }
        private void FinishEditing()
        {
            if (bufferDirty)
            {
                if (TryReadSpeed(out float value)) flight.AngularSpeed = value / GenTicks.TicksPerRealSecond;
                else Messages.Message("FAF_AngularInvalidHint".Translate(), MessageTypeDefOf.RejectInput, false);
            }
            ResetBuffer();
        }
        private bool TryReadSpeed(out float value)
        {
            bool parsed = float.TryParse(speedBuffer, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || float.TryParse(speedBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value)
                && value >= -MaximumDegreesPerSecond && value <= MaximumDegreesPerSecond;
        }
        private void SetSpeed(float value) { flight.AngularSpeed = value / GenTicks.TicksPerRealSecond; ResetBuffer(); ClearFocus(); }
        private void ResetBuffer() { speedBuffer = DegreesPerSecond.ToString("0.##", CultureInfo.CurrentCulture); bufferDirty = false; }
        private void ClearFocus() { if (GUI.GetNameOfFocusedControl() == SpeedControlName) GUI.FocusControl(null); hadFocus = false; }
    }
}
