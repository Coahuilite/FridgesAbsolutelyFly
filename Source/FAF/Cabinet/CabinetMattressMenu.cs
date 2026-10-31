// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Choose real map objects from a native menu rather than another targeting mode. / 用原生菜单选择地图真实物体，不再依赖指向模式。</summary>
    internal static class CabinetMattressMenu
    {
        internal static void Show(Building_FlyingFridgeCabinet cabinet)
        {
            if (!cabinet.Spawned || cabinet.HasMattress || cabinet.MattressInstallationPending)
            {
                Messages.Message("FAF_MattressInstallationUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<Thing> candidates = new List<Thing>();
            foreach (Thing built in cabinet.Map.listerThings.ThingsOfDef(CabinetDefOf.FAF_CabinetMattress))
                if (built.Spawned && !built.Destroyed && built.Faction == Faction.OfPlayer) candidates.Add(built);
            foreach (Thing packed in cabinet.Map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing))
                if (packed.Spawned && !packed.Destroyed && packed.GetInnerIfMinified() is Building_CabinetMattress)
                    candidates.Add(packed);
            if (candidates.Count == 0)
            {
                Messages.Message("FAF_NoMattressesOnMap".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            candidates.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(LabelFor(left), LabelFor(right)));
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Thing target in candidates)
            {
                string label = LabelFor(target);
                string disabledReason = null;
                if (target.IsForbidden(Faction.OfPlayer)) disabledReason = "FAF_MattressForbidden".Translate();
                else if (target.IsBurning()) disabledReason = "FAF_MattressBurning".Translate();
                else if (!CabinetPayloadCapacity.CanInstallAfterClearing(cabinet, target)) disabledReason = "FAF_OverMass".Translate();
                else if (cabinet.AvailableHaulerFor(target) == null)
                    disabledReason = (target is Building_CabinetMattress ? "FAF_NoMattressInstaller" : "FAF_NoMattressHauler").Translate();
                Action action = disabledReason == null ? (Action)(() => cabinet.TryOrderMattress(target)) : null;
                if (disabledReason != null) label += " — " + disabledReason;
                options.Add(new FloatMenuOption(label, action, target, Color.white, revalidateClickTarget: target));
            }
            Find.WindowStack.Add(new FloatMenu(options, "FAF_SelectMattress".Translate()));
        }

        internal static void ConfirmAndOrder(Building_FlyingFridgeCabinet cabinet, Thing target)
        {
            if (!cabinet.CanSelectMattressForInstallation(target))
            {
                Messages.Message("FAF_MattressNoLongerAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (cabinet.GetDirectlyHeldThings().Any)
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "FAF_MattressClearConfirm".Translate(target.GetInnerIfMinified().LabelCap),
                    () => cabinet.OrderConfirmedMattress(target, true)));
            else cabinet.OrderConfirmedMattress(target);
        }

        private static string LabelFor(Thing target)
        {
            Building_CabinetMattress mattress = (Building_CabinetMattress)target.GetInnerIfMinified();
            QualityCategory quality;
            mattress.TryGetQuality(out quality);
            string material = mattress.Stuff == null ? "FAF_UnknownMattressMaterial".Translate() : mattress.Stuff.LabelCap.ToString();
            string status = (target is MinifiedThing ? "FAF_MattressUninstalled" : "FAF_MattressBuilt").Translate();
            return "FAF_MattressCandidate".Translate(material, quality.GetLabel().CapitalizeFirst(), status,
                target.Position.x, target.Position.z);
        }
    }
}
