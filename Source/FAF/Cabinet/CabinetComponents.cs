// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using FAF.Cabinet.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>XML-configurable conversion of the shared thermal load. / 可用XML配置的统一热负荷换算参数。</summary>
    public sealed class CompProperties_FridgeCabinetTemperature : CompProperties_TempControl
    {
        public float wattsPerDegree = 4.8f;
        public float coolantWattDaysPerUnit = 360f;
        // Strictly below this actual internal temperature, upstream extra recovery is available.
        // 实际柜内温度严格低于此值时，才允许前置的额外恢复。
        public float icyCoreMaximumTemperature = 10f;
        // Small single-seat appliance balance; rates use game ticks and normalized vacuum, not real gas units.
        // 小型单人家电的平衡参数；速率以游戏tick与归一化真空度表示，并非真实气体单位。
        public float lifeSupportWatts = 80f;
        public float vacuumLeakTimeConstantTicks = 3600f;
        public float vacuumRecoveryTimeConstantTicks = 300f;
        public CompProperties_FridgeCabinetTemperature() { compClass = typeof(CompFridgeCabinetTemperature); }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (wattsPerDegree <= 0f || coolantWattDaysPerUnit <= 0f)
                yield return "Thermal resource conversion coefficients must be positive.";
            if (float.IsNaN(icyCoreMaximumTemperature) || float.IsInfinity(icyCoreMaximumTemperature))
                yield return "Icy-core recovery temperature must be finite.";
            foreach (float value in new[] { lifeSupportWatts, vacuumLeakTimeConstantTicks, vacuumRecoveryTimeConstantTicks })
                if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                    yield return "Cabin life-support power and pressure time constants must be finite and positive.";
        }
    }

    public sealed class CompFridgeCabinetTemperature : CompTempControl
    {
        public CompProperties_FridgeCabinetTemperature ThermalProps { get { return (CompProperties_FridgeCabinetTemperature)props; } }
        public override void Initialize(CompProperties properties)
        {
            base.Initialize(properties);
            targetTemperature = ((CompProperties_TempControl)properties).defaultTargetTemperature;
        }
        public override float TargetTemperature
        {
            get { return Mathf.Clamp(targetTemperature, Props.minTargetTemperature, ((Building_FlyingFridgeCabinet)parent).MaximumTargetTemperature); }
            set { targetTemperature = Mathf.Clamp(value, Props.minTargetTemperature, ((Building_FlyingFridgeCabinet)parent).MaximumTargetTemperature); }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Replace the six legacy buttons with the one authoritative thermostat panel. / 用同一权威温控面板替代旧六个按钮。
            if (parent.Faction != Faction.OfPlayer) yield break;
            Building_FlyingFridgeCabinet cabinet = (Building_FlyingFridgeCabinet)parent;
            yield return new Command_Action
            {
                defaultLabel = (cabinet.UpgradeProfile.HeatingUnlocked ? "FAF_ThermostatTitle" : "FAF_CoolingRegulatorTitle").Translate(),
                defaultDesc = (cabinet.UpgradeProfile.HeatingUnlocked ? "FAF_ThermostatOpenDesc" : "FAF_CoolingRegulatorDesc").Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TempReset"),
                action = () => FAF.UI.Window_CabinetThermostat.OpenFor((Building_FlyingFridgeCabinet)parent)
            };
            if (cabinet.UpgradeProfile.RapidThermalControlUnlocked)
                yield return new Command_Toggle
                {
                    defaultLabel = "FAF_RapidThermalControl".Translate(),
                    defaultDesc = "FAF_RapidThermalControlDesc".Translate(cabinet.UpgradeProfile.RapidThermalMultiplier.ToString("0.##")),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/TempReset"),
                    isActive = () => cabinet.RapidThermalControlEnabled,
                    toggleAction = () => cabinet.SetRapidThermalControlEnabled(!cabinet.RapidThermalControlEnabled)
                };
        }
        public override string CompInspectStringExtra()
        {
            return "TargetTemperature".Translate() + ": " + TargetTemperature.ToStringTemperature("F0");
        }
    }

    /// <summary>The standalone mattress remains a normal quality-bearing bedroll. / 单独安装的床垫仍为带品质的普通睡袋。</summary>
    public sealed class Building_CabinetMattress : Building_Bed
    {
        // Widgets unwraps MinifiedThing before requesting an icon; keep the change on this bed class only.
        // Widgets会先展开MinifiedThing再请求图标，因此只在本床类上提供打包图标。
        public override Texture UIIconOverride
        {
            get
            {
                return Integration.CabinetPackedMattress.IsPacked(this)
                    ? Integration.CabinetPackedMattress.Texture : base.UIIconOverride;
            }
        }
    }

    [DefOf]
    public static class CabinetDefOf
    {
        public static ThingDef FAF_FlyingFridgeCabinet;
        public static ThingDef FAF_CabinetMattress;
        public static ThingDef Nivarian_CryoCoolant;
        public static JobDef FAF_LoadCabinet;
        public static JobDef FAF_InstallCabinetMattress;
        public static JobDef FAF_LoadCabinetMattressClearingConfirmed;
        public static JobDef FAF_InstallCabinetMattressClearingConfirmed;
        public static JobDef FAF_RefuelCabinetForLaunch;
        public static JobDef FAF_RefuelCabinetAutomatically;
        public static JobDef FAF_RefuelCabinetManually;
        static CabinetDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CabinetDefOf)); }
    }
}
