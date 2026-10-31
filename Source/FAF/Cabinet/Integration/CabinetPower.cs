// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using FAF.Cabinet.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>One native battery connector supports both adjacent wires and the original automatic range. / 唯一原生电池接线点同时支持相邻导线和原有自动接线距离。</summary>
    public sealed class CompFridgeCabinetBattery : CompPowerBattery
    {
        private CompProperties_CabinetBattery instanceSettings;
        public override void Initialize(CompProperties properties)
        {
            if (instanceSettings == null) instanceSettings = ((CompProperties_CabinetBattery)properties).CopyForInstance();
            base.Initialize(instanceSettings);
        }
        internal void RefreshInstanceCapacity(CompProperties_Battery settings)
        {
            // Only the private configuration changes. Native storedEnergy remains the absolute saved authority.
            // 仅更新私有配置；原生storedEnergy仍是保存绝对电量的唯一权威，不按比例填充。
            settings.storedEnergyMax = Research.CabinetResearch.Current.BatteryCapacityWattDays;
        }
        internal float SelfDischargeWatts { get { return Research.CabinetResearch.Current.BatterySelfDischargeWatts; } }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Both custom comp classes are outside ConnectToPower's exact-type checks. Only this comp registers.
            // 两个自定义组件均不命中ConnectToPower的精确类型判断；只由本组件登记，避免重复。
            parent.Map.powerNetManager.Notify_ConnectorWantsConnect(this);
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.powerNetManager.Notify_ConnectorDespawned(this);
            base.PostDeSpawn(map, mode);
            parent.GetComp<CompFridgeCabinetPowerLoad>().PowerOn = false;
        }
        public override void LostConnectParent()
        {
            base.LostConnectParent();
            parent.GetComp<CompFridgeCabinetPowerLoad>().ClearDisconnectedState();
        }
        public override void CompTick()
        {
            float before = StoredEnergy;
            base.CompTick();
            ((Building_FlyingFridgeCabinet)parent).ObserveBatterySelfDischarge(Mathf.Max(0f, before - StoredEnergy));
            // A newly built nearby transmitter cannot discover this custom connector through the exact Def check.
            // 新导线无法通过原版Def精确类型判断发现此自定义接线点，未接网时低频重新请求原生接线。
            if (parent.Spawned && connectParent == null && parent.IsHashIntervalTick(250))
                parent.Map.powerNetManager.Notify_ConnectorWantsConnect(this);
        }
        public override void CompPrintForPowerGrid(SectionLayer layer)
        {
            base.CompPrintForPowerGrid(layer);
            PowerNetGraphics.PrintOverlayConnectorBaseFor(layer, parent);
        }
        public override void PostExposeData()
        {
            // Keep the native storage/connection fields scoped away from the former private battery.
            // 隔离原生储能和接线字段，避免与旧私有电池字段重叠。
            if (!Scribe.EnterNode("fafCabinetBattery")) return;
            try { base.PostExposeData(); }
            finally { Scribe.ExitNode(); }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Prefs.DevMode) yield break;
            Building_FlyingFridgeCabinet cabinet = (Building_FlyingFridgeCabinet)parent;
            yield return new Command_Action { defaultLabel = "FAF_DevFillBattery".Translate(), action = cabinet.DevFillBattery };
            yield return new Command_Action { defaultLabel = "FAF_DevEmptyBattery".Translate(), action = cabinet.DevEmptyBattery };
        }
    }

    /// <summary>The native network settles only this shared-model load; it never creates battery energy. / 原生电网只结算统一模型的此项负载，不会凭空生成电池能量。</summary>
    public sealed class CompFridgeCabinetPowerLoad : CompPowerTrader
    {
        private float registeredLifeSupportWatts;
        private bool nativeTickInProgress;
        private PowerNet nativeTickNet;
        private float settledThermalWattDays, settledLifeSupportWattDays;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SetUpPowerVars();
            ClearDisconnectedState();
        }
        public override void CompTick() { base.CompTick(); ClearDisconnectedState(); }
        internal void ClearDisconnectedState()
        {
            // Only native PowerNet may turn this load on. Off-grid refrigeration never forges PowerOn.
            // 只有原生PowerNet可以开启本负载；离网制冷绝不伪造PowerOn。
            if (((Building_FlyingFridgeCabinet)parent).ConnectedPowerNet == null) PowerOn = false;
        }
        public override void SetUpPowerVars() { base.SetUpPowerVars(); RefreshDemand(); }
        public override void ReceiveCompSignal(string signal) { base.ReceiveCompSignal(signal); RefreshDemand(); }
        public void RefreshDemand()
        {
            if (nativeTickInProgress) return;
            ((Building_FlyingFridgeCabinet)parent).RefreshElectricalLoad();
        }
        // The cabinet supplies its one tick snapshot; event callbacks can still refresh immediately.
        // 柜体传入同一tick的唯一需求快照；事件回调仍可立即刷新。
        public void RefreshDemand(float thermalWatts, float lifeSupportWatts)
        {
            if (nativeTickInProgress) return;
            registeredLifeSupportWatts = lifeSupportWatts;
            PowerOutput = -(thermalWatts + lifeSupportWatts);
        }
        internal void BeginNativeGridTick(PowerNet net)
        {
            Building_FlyingFridgeCabinet cabinet = (Building_FlyingFridgeCabinet)parent;
            if (cabinet.ConnectedPowerNet != net) return;
            nativeTickInProgress = false;
            nativeTickNet = net;
            settledThermalWattDays = 0f; settledLifeSupportWattDays = 0f;
            cabinet.PrepareNativeThermalTick(net, out float thermalWatts, out float lifeSupportWatts);
            RefreshDemand(thermalWatts, lifeSupportWatts);
            // Freeze this quote through native PowerOn signals until the real payment receipt completes.
            // 将本报价冻结至真实支付回执完成，原生PowerOn信号不在结算中途改报价。
            nativeTickInProgress = true;
        }
        internal void ObserveNativeGridSettlement(PowerNet net)
        {
            if (!nativeTickInProgress || nativeTickNet != net) return;
            float watts = PowerOn ? Mathf.Max(0f, -PowerOutput) : 0f;
            float lifeSupport = Mathf.Min(registeredLifeSupportWatts, watts);
            settledThermalWattDays = (watts - lifeSupport) * CompPower.WattsToWattDaysPerTick;
            settledLifeSupportWattDays = lifeSupport * CompPower.WattsToWattDaysPerTick;
        }
        internal void CompleteNativeGridTick(PowerNet net)
        {
            if (!nativeTickInProgress || nativeTickNet != net) return;
            nativeTickInProgress = false;
            nativeTickNet = null;
            // A brownout can return without ChangeStoredEnergy even while PowerOn remains true.
            // 欠压时即使PowerOn仍为true，也可能没有ChangeStoredEnergy支付，默认回执为零。
            ((Building_FlyingFridgeCabinet)parent).CompleteNativeThermalTick(
                settledThermalWattDays, settledLifeSupportWattDays);
        }

        internal CabinetCoolingSource GridSupplySource(PowerNet net)
        {
            if (PowerOn)
            {
                // CurrentEnergyGainRate already includes this running load once, as in PowerNetTick.
                // CurrentEnergyGainRate已经计入本运行负载一次，与PowerNetTick的结算条件一致。
                return net.CurrentStoredEnergy() + net.CurrentEnergyGainRate() >= -1E-07f
                    ? CabinetCoolingSource.Electricity : CabinetCoolingSource.Unavailable;
            }
            // CanPowerNow adds the proposed load; use it only while this load is not already on.
            // CanPowerNow会额外计入待开启负载，因此只能用于当前尚未开启的负载。
            return net.CanPowerNow(this) ? CabinetCoolingSource.WaitingForGrid : CabinetCoolingSource.Unavailable;
        }
        public override void PostExposeData()
        {
            if (!Scribe.EnterNode("fafRefrigerationPower")) return;
            try { base.PostExposeData(); }
            finally { Scribe.ExitNode(); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ClearDisconnectedState();
        }
        public override string CompInspectStringExtra()
        {
            return "FAF_CabinetLoad".Translate(((Building_FlyingFridgeCabinet)parent).CurrentElectricalDemandWatts.ToString("F1"));
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra() { yield break; }
    }
}
