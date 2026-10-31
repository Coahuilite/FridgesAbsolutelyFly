// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    internal static class CabinetDevelopmentControls
    {
        internal static float Draw(float width, float y, Building_FlyingFridgeCabinet cabinet)
        {
            if (!Prefs.DevMode) return y;
            AcceptanceReport allowed = cabinet.CanUseDevelopmentResources();
            float part = (width - 24f) / 4f;
            CabinetCrewPanel.Button(new Rect(0f, y, part, 28f), "FAF_DevFillBattery", allowed, cabinet.DevFillBattery);
            CabinetCrewPanel.Button(new Rect(part + 8f, y, part, 28f), "FAF_DevEmptyBattery", allowed, cabinet.DevEmptyBattery);
            CabinetCrewPanel.Button(new Rect((part + 8f) * 2f, y, part, 28f), "FAF_DevFillCoolant", allowed, cabinet.DevFillCoolant);
            CabinetCrewPanel.Button(new Rect((part + 8f) * 3f, y, part, 28f), "FAF_DevEmptyCoolant", allowed, cabinet.DevEmptyCoolant);
            return y + 36f;
        }
    }
}
