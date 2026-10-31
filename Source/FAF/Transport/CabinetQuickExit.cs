// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    // A shortcut to the existing per-pawn airlock action, not a second unloading or exit path.
    // 只为既有逐人离柜动作提供快捷入口，不另设卸货或出舱物理路径。
    internal static class CabinetQuickExit
    {
        internal static Command_Action CommandFor(Building_FlyingFridgeCabinet cabinet)
        {
            List<Pawn> residents = MapResidents(cabinet);
            if (residents.Count == 0) return null;
            bool revealed = cabinet.UpgradeProfile.WorldLaunchUnlocked;
            Command_Action command = new Command_Action {
                defaultLabel = (revealed ? "FAF_QuickDisembark" : "FAF_LeaveFreezer").Translate(),
                defaultDesc = (revealed ? "FAF_QuickDisembarkDesc" : "FAF_LeaveFreezerDesc").Translate(),
                groupable = false
            };
            // Independent buttons identify their exact originals, including same-named users/freezers.
            // 独立按钮明确绑定的原柜与原人；同名时仍可用编号区分，单选保留简短动作名。
            command.defaultDesc += "\n\n" + "FAF_QuickExitTargets".Translate(
                cabinet.LabelCap + " #" + cabinet.thingIDNumber,
                string.Join(", ", residents.Select(pawn => pawn.LabelShortCap + " #" + pawn.thingIDNumber)));
            if (Find.Selector.NumSelected > 1)
                command.defaultLabel += "\n" + (residents.Count == 1 ? residents[0].LabelShortCap + " " : "") + "#" + cabinet.thingIDNumber;
            CabinetCommandIcons.Apply(command, ContentFinder<Texture2D>.Get("FAF/UI/Commands/QuickDisembark"));
            if (residents.Count == 1)
            {
                Pawn original = residents[0];
                command.action = () => RequestOne(cabinet, original);
                AcceptanceReport report = CabinetOccupancy.CanExit(cabinet, original);
                if (!report) command.Disable(report.Reason);
            }
            else
            {
                // Historical over-capacity saves keep every original pawn; each click chooses one explicitly.
                // 历史超员保存保留所有原Pawn；每次明确选择一个人，不任意全弹或弃人。
                command.defaultLabel += "…";
                command.defaultDesc += "\n\n" + "FAF_QuickExitChooseOne".Translate(residents.Count);
                command.action = () => ChooseOne(cabinet);
                if (!residents.Any(pawn => CabinetOccupancy.CanExit(cabinet, pawn)))
                    command.Disable(CabinetOccupancy.CanExit(cabinet, residents[0]).Reason);
            }
            return command;
        }
        private static List<Pawn> MapResidents(Building_FlyingFridgeCabinet cabinet)
        {
            if (cabinet == null || !cabinet.Spawned || !CabinetOccupancy.CanReadCabinet(cabinet)) return new List<Pawn>();
            CabinetOccupancySnapshot state = CabinetOccupancy.Read(cabinet);
            return state.Phase == CabinetTravelPhase.Map
                ? state.ActualOccupants.Where(pawn => !pawn.Dead && !pawn.Destroyed).Distinct().ToList() : new List<Pawn>();
        }
        private static void ChooseOne(Building_FlyingFridgeCabinet cabinet)
        {
            List<Pawn> residents = MapResidents(cabinet);
            if (residents.Count == 0) { CabinetMapJump.Reject("FAF_NoSafeExit".Translate()); return; }
            if (residents.Count == 1) { RequestOne(cabinet, residents[0]); return; }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn resident in residents)
            {
                Pawn original = resident;
                AcceptanceReport report = CabinetOccupancy.CanExit(cabinet, original);
                options.Add(new FloatMenuOption(original.LabelShortCap + " #" + original.thingIDNumber + (report ? "" : " (" + report.Reason + ")"),
                    report ? (Action)(() => RequestOne(cabinet, original)) : null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        private static void RequestOne(Building_FlyingFridgeCabinet cabinet, Pawn original)
        {
            AcceptanceReport report = CabinetOccupancy.CanExit(cabinet, original);
            if (!report) { CabinetMapJump.Reject(report.Reason); return; }
            // RequestExit retains the existing temperature/vacuum confirmation and revalidates at commit.
            // RequestExit继续保留温度／真空危险确认，并在真正执行时重验当前条件。
            if (!CabinetOccupancy.RequestExit(cabinet, original)) CabinetMapJump.Reject("FAF_CrewActionFailed".Translate());
        }
    }
}
