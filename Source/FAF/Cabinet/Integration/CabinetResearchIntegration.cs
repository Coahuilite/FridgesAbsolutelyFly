// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using FAF.Cabinet.Research;
using RimWorld;
using Verse;

namespace FAF.Cabinet.Integration
{
    public sealed class CabinetResearchVisibility : DefModExtension
    {
        public List<ResearchProjectDef> revealAfter = new List<ResearchProjectDef>();
    }

    /// <summary>Resolve the required public uplink API after managed/embedded Nivarian Def types have loaded. / 托管或嵌入式涅瓦莲Def类型加载后，解析必需的公开上行接口。</summary>
    [StaticConstructorOnStartup]
    internal static class CabinetUplinkResearchBridge
    {
        static CabinetUplinkResearchBridge()
        {
            Type utility = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "Nivarian_Race")
                {
                    utility = assembly.GetType("Nivarian_Race.Code.Helper.NivarianResearchUtility", false);
                    break;
                }
            MethodInfo visible = utility == null ? null : utility.GetMethod("IsProjectVisibleInStory",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ResearchProjectDef) }, null);
            if (visible == null || visible.ReturnType != typeof(bool))
                throw new MissingMethodException("[F.A.F.] Required Nivarian public uplink visibility API is unavailable.");
            new Harmony("coahuilite.fridgesabsolutelyfly").Patch(visible,
                postfix: new HarmonyMethod(typeof(CabinetUplinkResearchBridge), nameof(VisiblePostfix)));
        }
        private static void VisiblePostfix(ResearchProjectDef project, ref bool __result)
        {
            CabinetResearchVisibility visibility = project.GetModExtension<CabinetResearchVisibility>();
            if (!__result || visibility == null || project.IsFinished) return;
            foreach (ResearchProjectDef prerequisite in visibility.revealAfter)
                if (!prerequisite.IsFinished) { __result = false; return; }
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    internal static class CabinetResearchFinishedPatch
    {
        private static void Postfix(ResearchProjectDef proj)
        {
            if (proj.GetModExtension<CabinetResearchVisibility>() != null) CabinetResearch.Refresh();
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    internal static class CabinetReinforcedHitPointsPatch
    {
        private static void Postfix(StatRequest req, StatDef ___stat, ref float __result)
        {
            if (___stat == StatDefOf.MaxHitPoints && req.Thing is Building_FlyingFridgeCabinet cabinet)
                __result *= cabinet.UpgradeProfile.MaximumHitPointsFactor;
        }
    }
}
