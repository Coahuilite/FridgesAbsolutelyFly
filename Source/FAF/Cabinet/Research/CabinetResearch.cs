// SPDX-License-Identifier: MPL-2.0
using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Cabinet.Research
{
    [DefOf]
    public static class CabinetResearchDefOf
    {
        public static ResearchProjectDef FAF_SPICE_Cabinet, FAF_SPICE_TOFU,
            FAF_SPICE_CHIVE, FAF_SPICE_WALNUT, FAF_SPICE_ONION, FAF_SPICE_BASIL, FAF_SPICE_CREAM,
            FAF_SPICE_GARLIC, FAF_SPICE_PEPPER, FAF_SPICE_HOPS, FAF_SPICE_SALT, FAF_SPICE_DILL;
        public static CabinetUpgradeSettingsDef FAF_SPICE_Profile;
        static CabinetResearchDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CabinetResearchDefOf)); }
    }

    public static class CabinetResearch
    {
        private static readonly CabinetUpgradeProfile noGameProfile = new CabinetUpgradeProfile(new CabinetUpgradeSettingsDef());
        /// <summary>Process-lifetime static observers only; do not subscribe save-owned instances. / 仅供进程级静态观察者订阅；不得注册存档实例。</summary>
        public static event Action<CabinetUpgradeProfile> Changed;
        public static CabinetUpgradeProfile Current
        {
            get { return Verse.Current.Game == null ? noGameProfile : Verse.Current.Game.GetComponent<CabinetResearchGameComponent>().Profile; }
        }
        internal static CabinetUpgradeProfile ReadNativeProgress()
        {
            return new CabinetUpgradeProfile(CabinetResearchDefOf.FAF_SPICE_Profile,
                CabinetResearchDefOf.FAF_SPICE_TOFU.IsFinished, CabinetResearchDefOf.FAF_SPICE_CHIVE.IsFinished,
                CabinetResearchDefOf.FAF_SPICE_WALNUT.IsFinished, CabinetResearchDefOf.FAF_SPICE_ONION.IsFinished,
                CabinetResearchDefOf.FAF_SPICE_BASIL.IsFinished, CabinetResearchDefOf.FAF_SPICE_CREAM.IsFinished,
                CabinetResearchDefOf.FAF_SPICE_GARLIC.IsFinished, CabinetResearchDefOf.FAF_SPICE_PEPPER.IsFinished,
                CabinetResearchDefOf.FAF_SPICE_HOPS.IsFinished, CabinetResearchDefOf.FAF_SPICE_SALT.IsFinished,
                CabinetResearchDefOf.FAF_SPICE_DILL.IsFinished);
        }
        internal static void Refresh()
        {
            if (Verse.Current.Game != null) Verse.Current.Game.GetComponent<CabinetResearchGameComponent>().Refresh();
        }
        internal static void NotifyChanged(CabinetUpgradeProfile profile) { Changed?.Invoke(profile); }
    }

    /// <summary>Only a derived cache belongs to the game; ResearchManager remains the saved authority. / 游戏仅持有派生缓存；保存权威仍为ResearchManager。</summary>
    public sealed class CabinetResearchGameComponent : GameComponent
    {
        private CabinetUpgradeProfile profile;
        public CabinetResearchGameComponent(Game game) { }
        public CabinetUpgradeProfile Profile { get { return profile ?? (profile = CabinetResearch.ReadNativeProgress()); } }
        public override void FinalizeInit() { Refresh(); }
        internal void Refresh()
        {
            profile = CabinetResearch.ReadNativeProgress();
            // A single research event invalidates native stat caches, never edits hit points or shared Defs.
            // 仅在科研事件中清理原生属性缓存，绝不修改当前耐久或共享Def。
            StatDefOf.MaxHitPoints.Worker.TryClearCache();
            StatDefOf.Mass.Worker.TryClearCache();
            // Existing carried freezers benefit immediately; do not require repacking or reopening a caravan.
            // 已在携带中的冷柜立即获得科研减重，不要求重新打包或重开远行队。
            foreach (Caravan caravan in Find.WorldObjects.Caravans) caravan.RecacheInventory();
            CabinetResearch.NotifyChanged(profile);
        }
    }
}
