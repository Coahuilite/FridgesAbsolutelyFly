// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using Verse;

namespace FAF.Cabinet.Research
{
    /// <summary>One XML balance record; no global ThingDef mutation. / 唯一XML平衡配置，不修改全局建筑Def。</summary>
    public sealed class CabinetUpgradeSettingsDef : Def
    {
        public int baseCapacityStacks = 12;
        public int expandedCapacityStacks = 24;
        public float insulationExchangeFactor = 0.5f;
        public float flotationHullMassDivisor = 9f;
        public float reinforcedHitPointsFactor = 1.5f;
        public float baseCoolantCapacity = 5f;
        public float expandedCoolantCapacity = 8f;
        public float baseBatteryCapacityWattDays = 360f;
        public float upgradedBatteryCapacityWattDays = 720f;
        public float baseBatterySelfDischargeWatts = 5f;
        public float upgradedBatterySelfDischargeWatts = 4f;
        public float heatingMaximumTarget = 40f;
        public float rapidThermalMultiplier = 3f;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (baseCapacityStacks < 1 || expandedCapacityStacks < baseCapacityStacks)
                yield return "Cabinet capacities must be positive and upgrades cannot reduce capacity.";
            if (insulationExchangeFactor <= 0f || insulationExchangeFactor > 1f
                || float.IsNaN(flotationHullMassDivisor) || float.IsInfinity(flotationHullMassDivisor) || flotationHullMassDivisor < 1f
                || reinforcedHitPointsFactor < 1f || rapidThermalMultiplier < 1f
                || baseCoolantCapacity <= 0f || expandedCoolantCapacity < baseCoolantCapacity)
                yield return "Cabinet upgrade factors are outside their supported ranges.";
            if (baseBatteryCapacityWattDays <= 0f || upgradedBatteryCapacityWattDays < baseBatteryCapacityWattDays
                || baseBatterySelfDischargeWatts < 0f || upgradedBatterySelfDischargeWatts < 0f
                || upgradedBatterySelfDischargeWatts > baseBatterySelfDischargeWatts)
                yield return "Cabinet battery capacities must be positive and self-discharge cannot increase after upgrading.";
            foreach (float value in new[] { baseBatteryCapacityWattDays, upgradedBatteryCapacityWattDays,
                baseBatterySelfDischargeWatts, upgradedBatterySelfDischargeWatts })
                if (float.IsNaN(value) || float.IsInfinity(value))
                    yield return "Cabinet battery capacities and self-discharge must be finite.";
        }
    }

    /// <summary>Immutable view of native completed research, never saved as a second progression state. / 原生已完成科研的不可变视图，不保存第二份进度状态。</summary>
    public sealed class CabinetUpgradeProfile
    {
        public bool FlotationUnlocked { get; }
        public bool FineAttitudeControlUnlocked { get; }
        public bool RapidThermalControlUnlocked { get; }
        public bool MapJumpUnlocked { get; }
        public bool WorldLaunchUnlocked { get; }
        public bool AutonomyUnlocked { get; }
        public bool SupplyAutomationUnlocked { get; }
        public bool HeatingUnlocked { get; }
        public int VisualStage { get; }
        public int CapacityStacks { get; }
        public float PassiveExchangeFactor { get; }
        public float HullMassFactor { get; }
        public float MaximumHitPointsFactor { get; }
        public float CoolantCapacityUnits { get; }
        public float BatteryCapacityWattDays { get; }
        public float BatterySelfDischargeWatts { get; }
        public float MaximumTargetTemperature { get; }
        public float RapidThermalMultiplier { get; }

        internal CabinetUpgradeProfile(CabinetUpgradeSettingsDef settings, bool tofu = false, bool chive = false,
            bool walnut = false, bool onion = false, bool basil = false, bool cream = false,
            bool garlic = false, bool pepper = false, bool hops = false, bool salt = false,
            bool dill = false)
        {
            FlotationUnlocked = tofu;
            HullMassFactor = tofu ? 1f / settings.flotationHullMassDivisor : 1f;
            FineAttitudeControlUnlocked = garlic;
            RapidThermalControlUnlocked = pepper;
            MapJumpUnlocked = hops;
            WorldLaunchUnlocked = salt;
            AutonomyUnlocked = salt && dill;
            SupplyAutomationUnlocked = dill;
            HeatingUnlocked = cream;
            VisualStage = salt ? 2 : pepper ? 1 : 0;
            CapacityStacks = onion ? settings.expandedCapacityStacks : settings.baseCapacityStacks;
            PassiveExchangeFactor = chive ? settings.insulationExchangeFactor : 1f;
            MaximumHitPointsFactor = walnut ? settings.reinforcedHitPointsFactor : 1f;
            CoolantCapacityUnits = basil ? settings.expandedCoolantCapacity : settings.baseCoolantCapacity;
            BatteryCapacityWattDays = garlic ? settings.upgradedBatteryCapacityWattDays : settings.baseBatteryCapacityWattDays;
            BatterySelfDischargeWatts = garlic ? settings.upgradedBatterySelfDischargeWatts : settings.baseBatterySelfDischargeWatts;
            MaximumTargetTemperature = cream ? settings.heatingMaximumTarget : Core.CabinetThermalModel.MaximumTarget;
            RapidThermalMultiplier = pepper ? settings.rapidThermalMultiplier : 1f;
        }
    }
}
