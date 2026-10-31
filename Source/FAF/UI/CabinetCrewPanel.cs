// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using FAF.Cabinet;
using FAF.Transport;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.UI
{
    /// <summary>People are an occupancy view, never item rows or a second passenger list. / 人员只读自占用视图，不是物品行或第二乘员列表。</summary>
    internal static class CabinetCrewPanel
    {
        internal static string PhaseLabel(CabinetTravelPhase phase) { return ("FAF_CargoPhase_" + phase).Translate(); }
        internal static string PhaseLabel(CabinetOccupancySnapshot view)
        {
            return view.GiftCabinetWaiting ? (view.GiftDeliveryFailed ? "FAF_GiftPickupInterrupted" : "FAF_GiftPickupWaiting").Translate()
                : PhaseLabel(view.Phase);
        }
        internal static float Draw(float width, float y, Building_FlyingFridgeCabinet cabinet, CabinetOccupancySnapshot view, bool compact)
        {
            bool revealed = cabinet.UpgradeProfile.WorldLaunchUnlocked;
            y = DrawSummary(width, y, view, compact, revealed);
            foreach (Pawn pawn in view.ActualOccupants)
            {
                if (pawn == null) continue;
                y = DrawOccupant(width, y, cabinet, view, pawn, compact, revealed);
            }
            if (view.ActualOccupants.Count == 0)
            {
                y = DrawText(width, y, (revealed && view.AutonomyUnlocked ? "FAF_CrewEmptyAutonomous" : "FAF_CrewEmpty").Translate(), 30f);
            }
            y = DrawRestSection(width, y, cabinet, view, compact, revealed);
            if (!compact && revealed) y = DrawBoardingSection(width, y, cabinet, view);
            return y;
        }
        private static float DrawSummary(float width, float y, CabinetOccupancySnapshot view, bool compact, bool revealed)
        {
            Widgets.ListSeparator(ref y, width, (revealed ? "FAF_CrewTitle" : "FAF_RestingTitle").Translate());
            Widgets.Label(new Rect(0f, y, width, 28f), (revealed ? "FAF_CrewCount" : "FAF_RestingCount").Translate(view.ActualOccupants.Count));
            y += 30f;
            if (view.OverCapacity)
            {
                Color before = GUI.color; GUI.color = ColorLibrary.RedReadable;
                y = DrawText(width, y, (revealed ? "FAF_CrewOverCapacity" : "FAF_RestingOverCapacity").Translate(), 46f);
                GUI.color = before;
            }
            else if (view.HasOccupancyConflict)
            {
                Color before = GUI.color; GUI.color = ColorLibrary.RedReadable;
                y = DrawText(width, y, "FAF_CrewOccupancyConflict".Translate(), 46f);
                GUI.color = before;
            }
            if (revealed && !compact)
            {
                string mode = view.AutonomousMission ? "FAF_CrewAutonomousMission"
                    : view.AutonomyUnlocked ? "FAF_CrewAutonomyReady" : "FAF_CrewPresetRoute";
                y = DrawText(width, y, mode.Translate(), 30f);
            }
            return y;
        }
        private static float DrawOccupant(float width, float y, Building_FlyingFridgeCabinet cabinet,
            CabinetOccupancySnapshot view, Pawn pawn, bool compact, bool revealed)
        {
            Widgets.ThingIcon(new Rect(0f, y, 38f, 38f), pawn);
            string state = pawn.Dead ? "FAF_CrewDead" : pawn.Downed ? "FAF_CrewDowned"
                : view.BedSleeper == pawn ? "FAF_CrewResting" : revealed ? "FAF_CrewAboard" : "FAF_RestingPresent";
            string heading = pawn.LabelShortCap + " · " + state.Translate();
            float headingHeight = Mathf.Max(38f, Text.CalcHeight(heading, width - 76f));
            Widgets.Label(new Rect(46f, y, width - 76f, headingHeight), heading);
            Widgets.InfoCardButton(width - 24f, y, pawn);
            y += headingHeight + 2f;
            if (!compact)
            {
                string detail = revealed && pawn.RaceProps.Animal ? "FAF_CrewAnimal".Translate().ToString() : pawn.def.LabelCap.ToString();
                if (!pawn.Dead && pawn.needs?.food != null)
                    detail += " · " + "FAF_CrewFood".Translate(pawn.needs.food.CurLevelPercentage.ToString("P0"));
                float detailHeight = Mathf.Max(28f, Text.CalcHeight(detail, width - 46f));
                Widgets.Label(new Rect(46f, y, width - 46f, detailHeight), detail); y += detailHeight + 2f;
                y = DrawOccupantActions(y, cabinet, view.BedSleeper == pawn, pawn, revealed);
            }
            return y;
        }
        private static float DrawOccupantActions(float y, Building_FlyingFridgeCabinet cabinet, bool resting, Pawn pawn, bool revealed)
        {
            if (resting)
                Button(new Rect(46f, y, 130f, 28f), "FAF_CrewEndRest", CabinetOccupancy.CanEndRest(cabinet, pawn),
                    () => Run(cabinet, () => CabinetOccupancy.CanEndRest(cabinet, pawn), () => CabinetOccupancy.RequestEndRest(cabinet, pawn)));
            else
                Button(new Rect(46f, y, 130f, 28f), "FAF_CrewRest", CabinetOccupancy.CanRest(cabinet, pawn),
                    () => Run(cabinet, () => CabinetOccupancy.CanRest(cabinet, pawn), () => CabinetOccupancy.RequestRest(cabinet, pawn)));
            if (revealed)
                Button(new Rect(184f, y, 110f, 28f), "FAF_CrewExit", CabinetOccupancy.CanExit(cabinet, pawn),
                    () => Run(cabinet, () => CabinetOccupancy.CanExit(cabinet, pawn), () => CabinetOccupancy.RequestExit(cabinet, pawn)));
            if (pawn.Spawned && Widgets.ButtonText(new Rect(302f, y, 110f, 28f), "FAF_CrewLocate".Translate()))
                CameraJumper.TryJumpAndSelect(pawn);
            y += 34f;
            return y;
        }
        private static float DrawRestSection(float width, float y, Building_FlyingFridgeCabinet cabinet,
            CabinetOccupancySnapshot view, bool compact, bool revealed)
        {
            if (cabinet.HasMattress || view.BedSleeper != null)
            {
                y = DrawText(width, y, (revealed ? "FAF_CrewBedRule" : "FAF_RestingBedRule").Translate(), 42f);
            }
            Pawn preparing = view.RestPreparationPawn;
            if (preparing != null)
            {
                string preparationLabel = "FAF_RestPreparationPending".Translate(preparing.LabelShortCap);
                float labelWidth = width - (compact ? 34f : 194f);
                float preparationHeight = Mathf.Max(30f, Text.CalcHeight(preparationLabel, labelWidth));
                Widgets.Label(new Rect(0f, y, labelWidth, preparationHeight), preparationLabel);
                Widgets.InfoCardButton(width - 24f, y, preparing);
                if (!compact)
                    Button(new Rect(width - 186f, y, 150f, 28f), "FAF_CancelRestPreparation",
                        CabinetOccupancy.CanCancelRestPreparation(cabinet, preparing),
                        () => Run(cabinet, () => CabinetOccupancy.CanCancelRestPreparation(cabinet, preparing),
                            () => CabinetOccupancy.CancelRestPreparation(cabinet, preparing)));
                y += preparationHeight + 4f;
            }
            if (!compact && view.ActualOccupants.Count == 0 && preparing == null)
            {
                bool onMap = cabinet.Spawned && view.Phase == CabinetTravelPhase.Map;
                if (Widgets.ButtonText(new Rect(0f, y, 160f, 30f), "FAF_CrewArrangeRest".Translate(), active: onMap)) ChooseUse(cabinet, true);
                if (!onMap) TooltipHandler.TipRegion(new Rect(0f, y, 160f, 30f), "FAF_CrewRestMapOnly".Translate());
                y += 38f;
            }
            return y;
        }
        private static float DrawBoardingSection(float width, float y, Building_FlyingFridgeCabinet cabinet, CabinetOccupancySnapshot view)
        {
            foreach (Pawn pending in view.PendingBoarders)
            {
                string pendingLabel = "FAF_CrewPending".Translate(pending.LabelShortCap);
                float pendingHeight = Mathf.Max(30f, Text.CalcHeight(pendingLabel, width - 150f));
                Widgets.Label(new Rect(0f, y, width - 150f, pendingHeight), pendingLabel);
                Building_FlyingFridgeCabinet targetCabinet = cabinet;
                Button(new Rect(width - 142f, y, 142f, 28f), "FAF_CrewCancelBoarding",
                    CabinetOccupancy.CanCancelBoarding(cabinet, pending), () => Run(targetCabinet, () => CabinetOccupancy.CanCancelBoarding(targetCabinet, pending), () => CabinetOccupancy.CancelBoarding(targetCabinet, pending)));
                y += pendingHeight + 4f;
            }
            bool mayArrange = cabinet.Spawned && view.Phase == CabinetTravelPhase.Map;
            if (Widgets.ButtonText(new Rect(0f, y, 160f, 30f), "FAF_CrewArrangeEntry".Translate(), active: mayArrange)) ChooseUse(cabinet, false);
            if (!mayArrange) TooltipHandler.TipRegion(new Rect(0f, y, 160f, 30f), "FAF_CrewEntryMapOnly".Translate());
            y += 38f;
            return y;
        }
        private static float DrawText(float width, float y, string text, float minimumHeight)
        {
            float height = Mathf.Max(minimumHeight, Text.CalcHeight(text, width));
            Widgets.Label(new Rect(0f, y, width, height), text);
            return y + height + 2f;
        }
        private static void ChooseUse(Building_FlyingFridgeCabinet cabinet, bool rest)
        {
            if (!CabinetAccess.CanManage(cabinet) || !cabinet.Spawned) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            CabinetOccupancySnapshot current = CabinetOccupancy.Read(cabinet);
            foreach (Pawn pawn in cabinet.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction != Faction.OfPlayer) continue;
                if (!rest && (current.ActualOccupants.Contains(pawn) || current.PendingBoarders.Contains(pawn))) continue;
                Pawn selected = pawn;
                AcceptanceReport report = rest ? CabinetOccupancy.CanRest(cabinet, selected) : CabinetOccupancy.CanAdmit(cabinet, selected);
                options.Add(report ? new FloatMenuOption(selected.LabelShortCap, () => Run(cabinet, () => rest ? CabinetOccupancy.CanRest(cabinet, selected) : CabinetOccupancy.CanAdmit(cabinet, selected),
                        () => rest ? CabinetOccupancy.RequestRest(cabinet, selected) : CabinetOccupancy.RequestEnter(cabinet, selected)))
                    : new FloatMenuOption(selected.LabelShortCap + ": " + report.Reason, null));
            }
            if (options.Count == 0) Messages.Message("FAF_CrewNoCandidates".Translate(), MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(options));
        }
        private static void Run(Building_FlyingFridgeCabinet cabinet, Func<AcceptanceReport> canExecute, Func<bool> execute)
        {
            if (!CabinetAccess.CanManage(cabinet)) return;
            AcceptanceReport report = canExecute();
            if (!report) Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, false);
            else if (!execute()) Messages.Message("FAF_CrewActionFailed".Translate(), MessageTypeDefOf.RejectInput, false);
        }
        internal static void Button(Rect rect, string labelKey, AcceptanceReport report, Action action)
        {
            if (Widgets.ButtonText(rect, labelKey.Translate(), active: report.Accepted)) action();
            if (!report.Accepted) TooltipHandler.TipRegion(rect, report.Reason.NullOrEmpty() ? "FAF_CargoActionUnavailable".Translate() : report.Reason);
        }
    }
}
