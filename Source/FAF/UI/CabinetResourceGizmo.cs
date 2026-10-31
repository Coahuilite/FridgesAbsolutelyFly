// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>One native-height card over one cabinet's real resource components. / 一个原生高度面板，只读取本柜真实资源组件。</summary>
    [StaticConstructorOnStartup]
    public sealed class CabinetResourceGizmo : Gizmo
    {
        private const float Width = 220f;
        private readonly Building_FlyingFridgeCabinet cabinet;
        // Gesture state survives recreated Gizmos without retaining an old game's cabinet/map.
        // 手势可跨Gizmo重建延续，但不强持旧游戏的柜体／地图，也不缓存目标或余额。
        private static System.WeakReference<Building_FlyingFridgeCabinet> draggingCabinet;
        private static int draggingFrame = -1;
        private static readonly Texture2D BatteryBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.18f, 0.43f, 0.49f));
        private static readonly Texture2D CoolantBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.29f, 0.47f));
        private static readonly Texture2D CoolantHighlight = SolidColorMaterials.NewSolidColorTexture(new Color(0.45f, 0.38f, 0.59f));
        private static readonly Texture2D EmptyBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.065f, 0.075f, 0.09f));
        private static readonly Texture2D TargetMarker = SolidColorMaterials.NewSolidColorTexture(new Color(0.74f, 0.97f, 0.8f));

        private CabinetResourceGizmo(Building_FlyingFridgeCabinet cabinet)
        {
            this.cabinet = cabinet;
            Order = -100f;
        }
        public static Gizmo For(Building_FlyingFridgeCabinet cabinet) { return new CabinetResourceGizmo(cabinet); }
        public override bool Visible { get { return CabinetAccess.CanManage(cabinet); } }
        public override float GetWidth(float maxWidth) { return Width; }
        // Multi-selection keeps an explicitly identified card for each original cabinet, never a summed reservoir.
        // 多选时逐柜显示明确编号，不把不同原柜的资源加成一个虚拟液仓。
        public override bool GroupsWith(Gizmo other) { return false; }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Building_FlyingFridgeCabinet dragOwner = ReadDragOwner();
            if (!CabinetAccess.CanManage(cabinet)) return new GizmoResult(GizmoState.Clear);
            CabinetResourceSnapshot state = cabinet.GetResourceSnapshot();
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), Height);
                Widgets.DrawWindowBackground(rect);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = false; GUI.color = Color.white;
                Rect title = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 92f, 18f);
                string identity = "FAF_ResourceGizmoTitle".Translate(cabinet.thingIDNumber);
                Widgets.Label(title, identity.Truncate(title.width));
                TooltipHandler.TipRegion(title, cabinet.LabelCap + " #" + cabinet.thingIDNumber);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(rect.xMax - 88f, rect.y + 3f, 80f, 18f),
                    "FAF_ResourceTargetValue".Translate(state.CoolantTargetUnits.ToString("0.##")));

                Rect batteryRect = new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, 22f);
                Widgets.FillableBar(batteryRect, Fraction(state.BatteryStoredWattDays, state.BatteryCapacityWattDays),
                    BatteryBar, EmptyBar, doBorder: true);
                DrawAmount(batteryRect, "FAF_ResourceBatteryRow".Translate(
                    state.BatteryStoredWattDays.ToString("0.#"), state.BatteryCapacityWattDays.ToString("0.#")));
                TooltipHandler.TipRegion(batteryRect, "FAF_ResourceBatteryTip".Translate());

                Rect coolantRect = new Rect(rect.x + 8f, rect.y + 47f, rect.width - 16f, 22f);
                float target = Fraction(state.CoolantTargetUnits, state.CoolantCapacityUnits);
                float before = target;
                bool wasDragging = dragOwner == cabinet;
                bool dragging = wasDragging;
                Widgets.DraggableBar(coolantRect, CoolantBar, CoolantHighlight, EmptyBar, TargetMarker,
                    ref dragging, Fraction(state.CoolantStoredUnits, state.CoolantCapacityUnits), ref target);
                if (dragging)
                {
                    if (!wasDragging) draggingCabinet = new System.WeakReference<Building_FlyingFridgeCabinet>(cabinet);
                    draggingFrame = Time.frameCount;
                }
                else if (wasDragging) ClearDrag();
                if (!Mathf.Approximately(before, target)) cabinet.SetCoolantTargetUnits(target * state.CoolantCapacityUnits);
                DrawAmount(coolantRect, "FAF_ResourceCoolantRow".Translate(
                    state.CoolantStoredUnits.ToString("0.##"), state.CoolantCapacityUnits.ToString("0.#")));
                TooltipHandler.TipRegion(coolantRect, "FAF_CoolantTargetDragHint".Translate() + "\n\n"
                    + (state.AutomaticCoolantSupplyAvailable ? "FAF_CoolantTargetSharedHint" : "FAF_CoolantTargetManualHint").Translate());
                if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
                return new GizmoResult(GizmoState.Clear);
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        // Run once at the grid boundary, before a bar can start this MouseDown gesture.
        // 在原生网格入口、任何条开始本次按下手势之前，只清理本类的过期归属，不消费输入。
        internal static void BeginGizmoGrid()
        {
            if (Event.current.rawType == EventType.MouseDown && Event.current.button == 0) ClearDrag();
            ReadDragOwner();
        }

        private static Building_FlyingFridgeCabinet ReadDragOwner()
        {
            if (draggingCabinet == null) return null;
            bool nativeRelease = Event.current.type == EventType.MouseUp && Event.current.button == 0;
            if (!draggingCabinet.TryGetTarget(out Building_FlyingFridgeCabinet owner)
                || !Application.isFocused || (!Input.GetMouseButton(0) && !nativeRelease)
                || !IsSelectedHere(owner) || !CabinetAccess.CanManage(owner)
                || Time.frameCount < draggingFrame || Time.frameCount > draggingFrame + 1)
            {
                ClearDrag();
                return null;
            }
            return owner;
        }

        private static bool IsSelectedHere(Building_FlyingFridgeCabinet owner)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;
            Thing selected = owner.Spawned ? owner : owner.ParentHolder as MinifiedThing;
            return selected != null && selected.Spawned && selected.MapHeld == Find.CurrentMap && Find.Selector.IsSelected(selected);
        }

        private static void ClearDrag() { draggingCabinet = null; draggingFrame = -1; }

        private static float Fraction(float amount, float capacity) { return capacity > 0f ? Mathf.Clamp01(amount / capacity) : 0f; }
        private static void DrawAmount(Rect rect, string amount)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, amount);
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
    internal static class CabinetResourceGestureLifetime
    {
        private static void Prefix() { CabinetResourceGizmo.BeginGizmoGrid(); }
    }
}
