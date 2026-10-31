// SPDX-License-Identifier: MPL-2.0
using System.Linq;
using FAF.Cabinet;
using FAF.Transport;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>Independent cargo entry points; no thermostat is needed to open them. / 独立货物入口，不依赖先打开温控。</summary>
    public static class CabinetCargoUI
    {
        public static void Open(Building_FlyingFridgeCabinet cabinet)
        {
            if (!CabinetAccess.CanManage(cabinet)) return;
            OpenWindow(new Window_CabinetInventory(cabinet));
        }
        public static void OpenPayload(ActiveTransporterInfo payload)
        {
            if (!CabinetOccupancy.CanReadPayload(payload)) return;
            Building_FlyingFridgeCabinet[] cabinets = payload.innerContainer.OfType<MinifiedThing>()
                .Select(wrapper => wrapper.InnerThing).OfType<Building_FlyingFridgeCabinet>().ToArray();
            if (cabinets.Length == 1 && CabinetAccess.CanManage(cabinets[0])) Open(cabinets[0]);
            else OpenWindow(new Window_CabinetInventory(payload));
        }
        private static void OpenWindow(Window_CabinetInventory candidate)
        {
            foreach (Window window in Find.WindowStack.Windows)
                if (window is Window_CabinetInventory existing && existing.HasSameSubject(candidate))
                { Find.WindowStack.Notify_ManuallySetFocus(existing); return; }
            Find.WindowStack.Add(candidate);
        }
        public static Command_Action CommandFor(Building_FlyingFridgeCabinet cabinet)
        {
            Command_Action command = new Command_Action {
                defaultLabel = "FAF_CargoManagement".Translate(), defaultDesc = "FAF_CargoManagementDesc".Translate(),
                action = () => Open(cabinet)
            };
            CabinetCommandIcons.ApplyCabinet(command, cabinet);
            return command;
        }
        public static Command_Action PayloadCommandFor(ActiveTransporterInfo payload)
        {
            Command_Action command = new Command_Action {
                defaultLabel = "FAF_CargoManagement".Translate(), defaultDesc = "FAF_CargoManagementDesc".Translate(),
                action = () => OpenPayload(payload)
            };
            CabinetCommandIcons.Apply(command, CabinetVisualRenderer.CurrentIcon);
            if (!CabinetOccupancy.CanReadPayload(payload)) command.Disable("FAF_InvalidCabinet".Translate());
            return command;
        }
    }
}
