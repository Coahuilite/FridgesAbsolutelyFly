// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Unfiltered real corpse cargo, including nested holders. / 不受搜索隐藏的真实遗体货物，包含嵌套持有者。</summary>
    internal static class CabinetCorpseCargoPanel
    {
        internal static float Draw(float width, float y, IReadOnlyCollection<Corpse> corpses,
            Building_FlyingFridgeCabinet cabinet = null, Action<Corpse> takeFromMainStorage = null)
        {
            if (corpses.Count == 0) return y;
            Widgets.ListSeparator(ref y, width, "FAF_CorpseCargoTitle".Translate(corpses.Count));
            string hint = "FAF_CorpseCargoHint".Translate();
            float hintHeight = Mathf.Max(28f, Text.CalcHeight(hint, width));
            Widgets.Label(new Rect(0f, y, width, hintHeight), hint);
            y += hintHeight + 4f;
            bool canTransfer = cabinet != null && takeFromMainStorage != null && CabinetAccess.CanTransferItems(cabinet);
            foreach (Corpse corpse in corpses)
            {
                Widgets.ThingIcon(new Rect(0f, y, 36f, 36f), corpse);
                float textWidth = width - (cabinet == null ? 80f : 220f);
                float rowHeight = Mathf.Max(38f, Text.CalcHeight(corpse.LabelCap, textWidth));
                Widgets.Label(new Rect(44f, y, textWidth, rowHeight), corpse.LabelCap);
                Widgets.InfoCardButton(width - 24f, y, corpse);
                if (cabinet != null)
                {
                    bool direct = cabinet.Contents.Contains(corpse);
                    bool allowed = direct && canTransfer;
                    Rect actionRect = new Rect(width - 166f, y, 130f, 28f);
                    if (Widgets.ButtonText(actionRect, "FAF_TransferOut".Translate(), active: allowed)) takeFromMainStorage(corpse);
                    if (!allowed) TooltipHandler.TipRegion(actionRect,
                        (direct ? "FAF_CargoExternalReadOnly" : "FAF_CorpseCargoHeldElsewhere").Translate());
                }
                y += rowHeight + 8f;
                CabinetItemDetails.Draw(corpse, width, ref y, 44f);
                y += 8f;
            }
            return y + 8f;
        }
    }
}
