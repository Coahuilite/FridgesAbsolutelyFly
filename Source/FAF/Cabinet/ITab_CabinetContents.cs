// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using RimWorld;
using FAF.UI;
using FAF.Transport;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Extend native rows with live preservation readback, without another decay simulation. / 在原生行下补充实时保鲜读数，不另行模拟腐烂。</summary>
    public sealed class ITab_CabinetContents : ITab_ContentsBase
    {
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private readonly List<Thing> visibleItems = new List<Thing>();
        private Building_FlyingFridgeCabinet Cabinet { get { return SelThing as Building_FlyingFridgeCabinet; } }
        public override IList<Thing> container { get { return Cabinet.GetDirectlyHeldThings(); } }
        public override bool UseDiscardMessage { get { return false; } }
        public override bool VisibleInBlueprintMode { get { return false; } }
        public ITab_CabinetContents()
        {
            size = new Vector2(560f, 520f);
            labelKey = "FAF_CabinetContentsTab";
            containedItemsKey = "FAF_CabinetContentsTab";
        }

        protected override void FillTab()
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                // The native base reserves 30 pixels above its scrolling contents. / 原生基类在滚动内容上方预留30像素。
                search.OnGUI(new Rect(size.x - 260f, 3f, 248f, QuickSearchWidget.WidgetHeight));
                base.FillTab();
            }
            finally
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
                GUI.color = color;
            }
        }

        protected override void DoItemsLists(Rect inRect, ref float curY)
        {
            Widgets.BeginGroup(inRect);
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                CabinetOccupancySnapshot occupancy = CabinetOccupancy.Read(Cabinet);
                curY = CabinetCrewPanel.Draw(inRect.width, curY, Cabinet, occupancy, true);
                curY = CabinetCorpseCargoPanel.Draw(inRect.width, curY, occupancy.AssociatedCorpses, Cabinet,
                    corpse => Cabinet.Eject(corpse, corpse.stackCount));
                if (Widgets.ButtonText(new Rect(0f, curY, 180f, 30f), "FAF_CargoManagement".Translate())) CabinetCargoUI.Open(Cabinet);
                curY += 38f;
                CabinetItemDetails.DrawLine(inRect.width, ref curY, "FAF_PreservationGuide".Translate());
                Widgets.ListSeparator(ref curY, inRect.width, containedItemsKey.Translate());
                // A drop may change the live container while its native row is being drawn.
                // 原生行的取出操作可能改变容器；本次绘制使用短期引用列表，内容状态仍从实体读取。
                visibleItems.Clear();
                foreach (Thing item in container)
                    if (item != null && !(item is Corpse) && search.filter.Matches(item.LabelNoCount)) visibleItems.Add(item);
                search.noResultsMatched = search.filter.Active && visibleItems.Count == 0 && occupancy.AssociatedCorpses.Count == 0;
                foreach (Thing item in visibleItems)
                {
                    tmpSingleThing.Clear();
                    tmpSingleThing.Add(item);
                    // Keep native info cards, selection, partial/full ejection and item icons.
                    // 保留原生信息卡、选择、部分或全部取出以及物品图标。
                    DoThingRow(item.def, item.stackCount, tmpSingleThing, inRect.width, ref curY,
                        count => OnDropThing(item, count));
                    tmpSingleThing.Clear();
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.WordWrap = true;
                    if (container.Contains(item)) CabinetItemDetails.Draw(item, inRect.width, ref curY);
                    curY += 12f;
                }
                if (visibleItems.Count == 0) Widgets.NoneLabel(ref curY, inRect.width);
            }
            finally
            {
                tmpSingleThing.Clear();
                visibleItems.Clear();
                Widgets.EndGroup();
            }
        }

        protected override void OnDropThing(Thing thing, int count) { Cabinet.Eject(thing, count); }
    }
}
