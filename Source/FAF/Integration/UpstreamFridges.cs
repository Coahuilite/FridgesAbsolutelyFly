// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NivarianSleepInFridges;
using Verse;

namespace FAF.Integration
{
    internal static class UpstreamFridges
    {
        private delegate bool FridgePredicate(ThingDef def, out string reason);
        private static FridgePredicate predicate;
        // EN: Def-load metadata only; no map, pawn or save-owned objects are cached.
        // 中：只缓存 Def 加载周期的元数据，不持有地图、角色或存档对象。
        private static readonly Dictionary<ThingDef, bool> fridgeDefs = new Dictionary<ThingDef, bool>();

        internal static void Initialize()
        {
            if (predicate != null) return;
            // EN: Upstream exposes its sleep comp but keeps the predicate internal. Bind the exact method once.
            // 中：前置公开睡眠组件，却将判定器设为 internal，因此仅在此绑定一次真实方法。
            Type utility = typeof(CompFridgeSleep).Assembly.GetType("NivarianSleepInFridges.FridgeDefUtility", true);
            MethodInfo method = utility.GetMethod("IsFridgeDef", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(ThingDef), typeof(string).MakeByRefType() }, null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new InvalidOperationException("[F.A.F.] Required Nivarian Sleep In Fridges predicate is unavailable. Update both mods together.");
            predicate = (FridgePredicate)Delegate.CreateDelegate(typeof(FridgePredicate), method);
        }

        internal static bool IsFridge(ThingDef def)
        {
            if (def == null) return false;
            bool result;
            if (fridgeDefs.TryGetValue(def, out result)) return result;
            Initialize();
            string reason;
            result = predicate(def, out reason);
            fridgeDefs.Add(def, result);
            return result;
        }

        internal static void PrepareDef(ThingDef def)
        {
            if (!IsFridge(def)) return;
            if (def.comps == null) def.comps = new List<CompProperties>();
            if (!def.HasComp(typeof(CompFridgeFlight)))
                def.comps.Add(new CompProperties_FridgeFlight());
            // EN: Keep the original Print/Draw division, shared by every instance of this Def.
            // 中：保留此 Def 所有实例共用的原始 Print/Draw 职责，绝不修改 drawerType。
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), "InitializeComps")]
    internal static class PrepareDynamicFridgeDef
    {
        private static void Prefix(ThingWithComps __instance)
        {
            UpstreamFridges.PrepareDef(__instance.def);
        }
    }
}
