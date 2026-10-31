// SPDX-License-Identifier: MPL-2.0
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Native-style cooldown presentation over transport-owned live ticks. / 原生风格冷却显示，只读取运输权威的实时tick。</summary>
    [StaticConstructorOnStartup]
    public sealed class CabinetCooldownCommand : Command_Action
    {
        private readonly Func<int> remainingTicks;
        private readonly Func<int> totalTicks;
        private static readonly Texture2D CooldownBar = SolidColorMaterials.NewSolidColorTexture(new Color32(9, 203, 4, 64));

        private CabinetCooldownCommand(Func<int> remainingTicks, Func<int> totalTicks)
        {
            this.remainingTicks = remainingTicks;
            this.totalTicks = totalTicks;
        }

        public static Command_Action Create(Func<int> remainingTicks, Func<int> totalTicks,
            string label, string description, Texture2D icon, Action action)
        {
            CabinetCooldownCommand command = new CabinetCooldownCommand(remainingTicks, totalTicks) {
                defaultLabel = label, defaultDesc = description, action = action
            };
            CabinetCommandIcons.Apply(command, icon);
            return command;
        }

        public override bool Disabled
        {
            get { return base.Disabled || remainingTicks() > 0; }
            set { base.Disabled = value; }
        }
        public override string TopRightLabel
        {
            get
            {
                int remaining = remainingTicks();
                return remaining > 0 ? remaining.ToStringTicksToPeriod(shortForm: true, canUseDecimalsShortForm: true) : base.TopRightLabel;
            }
        }
        public override string Desc
        {
            get
            {
                int remaining = remainingTicks();
                return remaining > 0 ? base.Desc + "\n\n" + "FAF_CooldownTimeRemaining".Translate(
                    remaining.ToStringTicksToPeriod(), remaining) : base.Desc;
            }
        }

        public override float GetWidth(float maxWidth)
        {
            float width = base.GetWidth(maxWidth);
            if (remainingTicks() <= 0 || hotKey == null || hotKey.MainKey == KeyCode.None) return width;
            GameFont font = Text.Font;
            try
            {
                Text.Font = GameFont.Tiny;
                // Preserve native key text at the left and the timer at the right without overlap.
                // 原生热键留在左上、计时留在右上；较长按键名称仅扩宽按钮，不压缩文字。
                return Mathf.Max(width, Text.CalcSize(hotKey.MainKey.ToStringReadable()).x + Text.CalcSize(TopRightLabel).x + 18f);
            }
            finally { Text.Font = font; }
        }

        protected override GizmoResult GizmoOnGUIInt(Rect butRect, GizmoRenderParms parms)
        {
            bool previousDisabled = disabled;
            string previousReason = disabledReason;
            if (remainingTicks() > 0)
            {
                disabled = true;
                if (disabledReason.NullOrEmpty()) disabledReason = "FAF_Cooldown".Translate();
            }
            try { return base.GizmoOnGUIInt(butRect, parms); }
            finally { disabled = previousDisabled; disabledReason = previousReason; }
        }

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            int remaining = remainingTicks();
            if (remaining <= 0) { base.DrawIcon(rect, buttonMat, parms); return; }
            Rect iconRect = rect;
            // Use the native command header/label geometry, leaving both text rows clear of the icon.
            // 沿原生命令的顶部／底部标签几何留白，计时、图标与按钮标签互不覆盖。
            iconRect.yMin += Text.LineHeight + 6f;
            if (!parms.shrunk && !LabelCap.NullOrEmpty())
                iconRect.yMax = Mathf.Max(iconRect.yMin, rect.yMax - Text.CalcHeight(LabelCap, rect.width + 0.1f) + 10f);
            base.DrawIcon(iconRect, buttonMat, parms);
            // This is the native ability tint/progress primitive, not an Ability or a second cooldown clock.
            // 只复用原生能力的色罩／进度控件，不创建Ability或第二个冷却时钟。
            float progress = Mathf.InverseLerp(totalTicks(), 0f, remaining);
            Widgets.FillableBar(rect, Mathf.Clamp01(progress), CooldownBar, null, doBorder: false);
        }
    }
}
