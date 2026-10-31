// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.Transport;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Two native ownership layers, never a merged virtual inventory. / 两层真实持有关系，不合并为虚拟库存。</summary>
    public sealed class Window_CabinetInventory : Window
    {
        private Building_FlyingFridgeCabinet cabinet;
        private readonly ActiveTransporterInfo unassignedPayload;
        private bool transportTab;
        private float contentHeight = 640f;
        private bool IsValid { get { return cabinet != null ? CabinetAccess.CanManage(cabinet) : CabinetOccupancy.CanReadPayload(unassignedPayload); } }
        internal bool HasSameSubject(Window_CabinetInventory other) { return cabinet == other.cabinet && unassignedPayload == other.unassignedPayload; }
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private Vector2 outsideScroll, insideScroll, viewportScroll;
        private float outsideHeight, insideHeight;
        public Window_CabinetInventory(Building_FlyingFridgeCabinet cabinet) { this.cabinet = cabinet; Configure(); }
        public Window_CabinetInventory(ActiveTransporterInfo payload) { unassignedPayload = payload; Configure(); }
        private void Configure()
        {
            draggable = true; doCloseX = true; closeOnCancel = true; onlyOneOfTypeAllowed = true;
            absorbInputAroundWindow = false; closeOnClickedOutside = false; forcePause = false;
        }
        public override Vector2 InitialSize { get { return new Vector2(Mathf.Min(920f, Verse.UI.screenWidth - 32f), Mathf.Min(700f, Verse.UI.screenHeight - 40f)); } }
        public override void WindowUpdate() { base.WindowUpdate(); if (!IsValid) Close(); }
        public override void DoWindowContents(Rect rect)
        {
            if (!IsValid) { Close(); return; }
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap;
            Color color = GUI.color; bool enabled = GUI.enabled;
            Rect content = new Rect(0f, 0f, Mathf.Max(820f, rect.width - 18f), Mathf.Max(contentHeight, rect.height - 18f));
            bool scrolling = content.width > rect.width || content.height > rect.height;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                if (scrolling) Widgets.BeginScrollView(rect, ref viewportScroll, content);
                try { DrawContents(content); }
                finally { if (scrolling) Widgets.EndScrollView(); }
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; GUI.enabled = enabled; }
        }
        private void DrawContents(Rect rect)
        {
            if (cabinet == null) { DrawUnassignedPayload(rect); return; }
            CabinetOccupancySnapshot view = CabinetOccupancy.Read(cabinet);
            Widgets.Label(new Rect(0f, 0f, rect.width - 300f, 30f), "FAF_InventoryTitle".Translate(cabinet.LabelCap, cabinet.thingIDNumber));
            if (Widgets.ButtonText(new Rect(rect.width - 290f, 0f, 140f, 30f), "FAF_SelectCabinet".Translate(),
                active: CabinetAccess.InInventory(CabinetAccess.InventoryPawns(cabinet)).Any())) SelectCabinet();
            CabinetCrewPanel.Button(new Rect(rect.width - 142f, 0f, 142f, 30f),
                cabinet.UpgradeProfile.HeatingUnlocked ? "FAF_ThermostatTitle" : "FAF_CoolingRegulatorTitle",
                cabinet.CanManageThermostat(), () => Window_CabinetThermostat.OpenFor(cabinet));
            Widgets.Label(new Rect(0f, 35f, rect.width, 28f), CabinetCrewPanel.PhaseLabel(view));
            float y = CabinetCrewPanel.Draw(rect.width, 68f, cabinet, view, false);
            if (view.GiftCabinetWaiting)
            {
                string pickup = "FAF_GiftPickupContents".Translate();
                float height = Mathf.Max(34f, Text.CalcHeight(pickup, rect.width));
                Widgets.Label(new Rect(0f, y, rect.width, height), pickup); y += height + 6f;
            }
            y = CabinetCorpseCargoPanel.Draw(rect.width, y, view.AssociatedCorpses, cabinet, corpse => ChooseCount(corpse, true));
            CabinetThermostatSnapshot thermal = cabinet.GetThermostatSnapshot();
            if (cabinet.UpgradeProfile.HeatingUnlocked)
            {
                Widgets.Label(new Rect(0f, y, rect.width, 28f), "FAF_CargoCabinStatus".Translate(
                    thermal.InternalTemperatureCelsius.ToStringTemperature("F1"),
                    Window_CabinetThermostat.EnvironmentStatus(thermal)));
                y += 32f;
            }
            CabinetCrewPanel.Button(new Rect(0f, y, 185f, 30f), "FAF_RefuelOwnContents", cabinet.CanRefuelFromContents(),
                () => cabinet.RefuelThermalCoolantFromContents());
            string mass = "FAF_CargoTotalMass".Translate(view.PayloadMass.ToString("0.##"), view.MassCapacity.ToString("0.##")).ToString();
            Widgets.Label(new Rect(195f, y, rect.width - 195f, 30f), mass);
            y += 38f;
            if (Prefs.DevMode) y = CabinetDevelopmentControls.Draw(rect.width, y, cabinet);
            Widgets.ListSeparator(ref y, rect.width, "FAF_CargoGoods".Translate());
            if (Widgets.ButtonText(new Rect(0f, y, 170f, 30f), "FAF_ColdStorage".Translate())) transportTab = false;
            if (cabinet.UpgradeProfile.WorldLaunchUnlocked && Widgets.ButtonText(new Rect(180f, y, 170f, 30f), "FAF_TransportCargo".Translate())) transportTab = true;
            if (!cabinet.UpgradeProfile.WorldLaunchUnlocked) transportTab = false;
            search.OnGUI(new Rect(rect.width - 260f, y, 260f, QuickSearchWidget.WidgetHeight)); y += 40f;
            if (transportTab)
            {
                bool canLoad = cabinet.Spawned && view.Phase == CabinetTravelPhase.Map && !view.HasOccupancyConflict;
                if (Widgets.ButtonText(new Rect(0f, y, 170f, 30f), "FAF_CargoArrangeLoading".Translate(), active: canLoad))
                    new Command_LoadToTransporter { transComp = cabinet.GetComp<CompCabinetTransport>() }.ProcessInput(Event.current);
                Widgets.Label(new Rect(180f, y, rect.width - 180f, 44f), "FAF_TransportCargoHint".Translate()); y += 48f;
                IEnumerable<Thing> goods = view.Payload != null ? view.Payload.innerContainer : cabinet.GetComp<CompCabinetTransport>().innerContainer;
                y = DrawReadOnlyThings(goods.Where(thing => IsGoods(thing) && !(thing is Corpse)
                    && thing.GetInnerIfMinified() != cabinet), rect.width, y);
                contentHeight = Mathf.Max(640f, y + 24f); return;
            }
            Widgets.Label(new Rect(0f, y, rect.width, 44f), "FAF_NativeInventoryHint".Translate()); y += 46f;
            bool hasOutside = CabinetAccess.InventoryPawns(cabinet).Any();
            float bodyHeight = 330f;
            if (hasOutside)
            {
                float half = (rect.width - 16f) * 0.5f;
                Widgets.Label(new Rect(0f, y, half, 30f), "FAF_OrdinaryInventory".Translate());
                Widgets.Label(new Rect(half + 16f, y, half, 30f), "FAF_CabinetInventory".Translate()); y += 32f;
                DrawPane(new Rect(0f, y, half, bodyHeight), false);
                DrawPane(new Rect(half + 16f, y, half, bodyHeight), true);
            }
            else
            {
                Widgets.Label(new Rect(0f, y, rect.width, 30f), "FAF_CabinetInventory".Translate()); y += 32f;
                DrawPane(new Rect(0f, y, rect.width, bodyHeight), true);
            }
            contentHeight = y + bodyHeight + 20f;
        }
        private void DrawUnassignedPayload(Rect rect)
        {
            CabinetRecoverySite site = unassignedPayload.parent as CabinetRecoverySite;
            bool giftPickup = site?.IsGiftPickup == true;
            Widgets.Label(new Rect(0f, 0f, rect.width, 30f), (giftPickup
                ? (site.GiftDeliveryFailed ? "FAF_GiftPickupInterrupted" : "FAF_GiftPickupWaiting") : "FAF_PayloadTitle").Translate());
            Widgets.Label(new Rect(0f, 36f, rect.width, 50f), (giftPickup ? "FAF_GiftPickupContents" : "FAF_PayloadReadOnly").Translate());
            float y = 92f;
            CabinetBiologicalPayload biological = CabinetBiologicalPayload.Read(unassignedPayload.innerContainer);
            foreach (Pawn pawn in biological.Living)
            {
                Widgets.ThingIcon(new Rect(0f, y, 38f, 38f), pawn);
                Widgets.Label(new Rect(46f, y, rect.width - 80f, 34f), pawn.LabelShortCap);
                Widgets.InfoCardButton(rect.width - 24f, y, pawn); y += 44f;
            }
            y = CabinetCorpseCargoPanel.Draw(rect.width, y, biological.Corpses);
            Widgets.ListSeparator(ref y, rect.width, "FAF_CargoGoods".Translate());
            y = DrawReadOnlyThings(unassignedPayload.innerContainer.Where(thing => !(thing is Pawn) && !(thing is Corpse))
                .Concat(biological.UncontainedDead.Cast<Thing>()), rect.width, y);
            contentHeight = Mathf.Max(640f, y + 24f);
        }
        private float DrawReadOnlyThings(IEnumerable<Thing> source, float width, float y)
        {
            bool any = false;
            foreach (Thing item in source)
            {
                if (item == null || (!(item is Corpse) && !(item is Pawn pawn && pawn.Dead)
                    && !search.filter.Matches(item.LabelNoCount))) continue;
                any = true;
                Widgets.ThingIcon(new Rect(0f, y, 32f, 32f), item);
                Widgets.Label(new Rect(38f, y, width - 74f, 32f), item.LabelCap);
                Widgets.InfoCardButton(width - 24f, y, item); y += 34f;
                Widgets.Label(new Rect(38f, y, width - 38f, 26f), "FAF_CargoItemMass".Translate((item.GetStatValue(StatDefOf.Mass) * item.stackCount).ToString("0.##"))); y += 30f;
            }
            if (!any) { Widgets.Label(new Rect(0f, y, width, 30f), "FAF_NoMatchingCargoHere".Translate()); y += 32f; }
            return y;
        }
        // A legacy dead Pawn remains inspectable cargo, never a living seat or a replacement corpse.
        // 历史死亡Pawn仍作为可查看货物保留，不计活席位，也不生成替代遗体。
        private static bool IsGoods(Thing thing) { return !(thing is Pawn pawn) || pawn.Dead; }
        private void SelectCabinet()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Building_FlyingFridgeCabinet candidate in CabinetAccess.InInventory(CabinetAccess.InventoryPawns(cabinet)))
            {
                Building_FlyingFridgeCabinet selected = candidate;
                options.Add(new FloatMenuOption(candidate.LabelCap + " #" + candidate.thingIDNumber,
                    () => { if (CabinetAccess.CanManage(selected)) { cabinet = selected; insideScroll = Vector2.zero; } }));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }
        private void DrawPane(Rect rect, bool inside)
        {
            List<Thing> things = inside ? cabinet.Contents.ToList() : CabinetAccess.InventoryPawns(cabinet)
                .Where(pawn => pawn.inventory != null).SelectMany(pawn => pawn.inventory.innerContainer).ToList();
            things.RemoveAll(item => item == null || inside && item is Corpse
                || (!(item is Corpse) && !search.filter.Matches(item.LabelNoCount)));
            float height = inside ? insideHeight : outsideHeight;
            Rect view = new Rect(0f, 0f, rect.width - 18f, Mathf.Max(rect.height, height));
            Vector2 scroll = inside ? insideScroll : outsideScroll;
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0f;
            bool canTransfer = CabinetAccess.CanTransferItems(cabinet);
            try
            {
                foreach (Thing item in things)
                {
                    Widgets.ThingIcon(new Rect(0f, y, 32f, 32f), item);
                    Widgets.Label(new Rect(38f, y, view.width - 68f, 40f), item.LabelCap);
                    Widgets.InfoCardButton(view.width - 24f, y, item);
                    y += 42f;
                    CabinetItemDetails.Draw(item, view.width, ref y, 0f);
                    string label = (inside ? "FAF_TransferOut" : "FAF_TransferIn").Translate();
                    bool allowed = canTransfer && (inside || cabinet.CanAcceptItem(item));
                    if (Widgets.ButtonText(new Rect(0f, y, 150f, 28f), label, active: allowed)) ChooseCount(item, inside);
                    if (!allowed) TooltipHandler.TipRegion(new Rect(0f, y, view.width, 28f), (canTransfer ? "FAF_StorageRejected" : "FAF_CargoExternalReadOnly").Translate());
                    y += 42f;
                }
                if (things.Count == 0) Widgets.Label(new Rect(0f, 0f, view.width, 70f), (inside ? "FAF_NoMatchingCargoHere" : "FAF_NoOrdinaryInventory").Translate());
            }
            finally
            {
                Widgets.EndScrollView();
                things.Clear();
                if (inside) { insideHeight = y; insideScroll = scroll; } else { outsideHeight = y; outsideScroll = scroll; }
            }
        }
        private void ChooseCount(Thing item, bool inside)
        {
            int maximum = inside ? item.stackCount : cabinet.CountCanAccept(item);
            if (maximum <= 0) { Messages.Message("FAF_StorageRejected".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            if (maximum == 1) { Transfer(item, inside, 1); return; }
            Find.WindowStack.Add(new Dialog_Slider(value => "FAF_TransferCount".Translate(item.LabelNoCount, value),
                1, maximum, count => Transfer(item, inside, count), maximum));
        }
        private void Transfer(Thing item, bool inside, int count)
        {
            if (!CabinetAccess.CanTransferItems(cabinet) || item == null || item.Destroyed || count <= 0) return;
            int transferred = 0;
            List<Pawn> pawns = CabinetAccess.InventoryPawns(cabinet).ToList();
            if (inside && cabinet.Contents.Contains(item))
            {
                Pawn recipient = pawns.FirstOrDefault(pawn => pawn.inventory != null);
                if (recipient != null) transferred = cabinet.TryTransferOut(item, recipient.inventory.innerContainer, count);
                else if (cabinet.MapHeld != null && cabinet.Eject(item, count)) transferred = count;
            }
            else if (!inside && pawns.Any(pawn => pawn.inventory != null && pawn.inventory.innerContainer.Contains(item)))
                transferred = cabinet.TryStore(item, count);
            if (transferred == 0) Messages.Message("FAF_StorageRejected".Translate(), MessageTypeDefOf.RejectInput, false);
            cabinet.GetCaravan()?.RecacheInventory();
        }
    }
}
