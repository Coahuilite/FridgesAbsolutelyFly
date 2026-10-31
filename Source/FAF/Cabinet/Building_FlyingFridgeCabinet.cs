// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Text;
using FAF.Cabinet.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FAF.Cabinet
{
    /// <summary>A real container that retains upstream Building_Storage identity. / 保留上游储存建筑身份的真正容器。</summary>
    public sealed partial class Building_FlyingFridgeCabinet : Building_Storage, IThingHolder,
        ISearchableContents, IHaulSource, IHaulDestination, IHaulEnroute, IThingHolderEvents<Thing>
    {
        private ThingOwner<Thing> contents;
        private float internalTemperature = 21f;
        // Read only for the one-time migration from the original private battery. / 仅用于旧私有电池的一次性迁移。
        private float storedWattDays;
        private bool nativeBatteryMigrated;
        private bool temperatureInitialized;
        private bool automaticLoading = true;
        private Rot4 mattressDrawRotation;

        public Building_FlyingFridgeCabinet()
        {
            contents = new ThingOwner<Thing>(this, false);
        }

        public float InternalTemperature { get { return internalTemperature; } }
        public float StoredWattDays { get { return Battery.StoredEnergy; } }
        private CompPowerBattery Battery { get { return GetComp<CompPowerBattery>(); } }
        public ThingOwner SearchableContents { get { return contents; } }
        public bool HaulSourceEnabled { get { return Spawned; } }
        bool IHaulDestination.HaulDestinationEnabled { get { return Spawned && (FAF.Transport.CabinetTransportLoading.IsFor(this) || !HasMattress && !MattressInstallationPending); } }
        // Transport loading uses its real bay; ordinary hauling retains the cold-container rules.
        // 运输装载使用真实运输舱；普通搬运仍遵守冷藏容器规则。
        bool IHaulDestination.Accepts(Thing thing)
        {
            return FAF.Transport.CabinetTransportLoading.IsFor(this)
                ? CabinetPayloadCapacity.CountCanAdd(this, thing, thing?.stackCount ?? 0) > 0 : CanAcceptItem(thing);
        }
        int IHaulEnroute.SpaceRemainingFor(ThingDef itemDef)
        {
            Thing carried = FAF.Transport.CabinetTransportLoading.Current?.ThingToCarry;
            return FAF.Transport.CabinetTransportLoading.IsFor(this) && carried?.def == itemDef
                ? CabinetPayloadCapacity.CountCanAdd(this, carried, carried.stackCount) : CountCanAcceptDef(itemDef);
        }

        public Building_CabinetMattress Mattress
        {
            get
            {
                if (contents.Count != 1) return null;
                return contents[0].GetInnerIfMinified() as Building_CabinetMattress;
            }
        }
        public bool HasMattress { get { return Mattress != null; } }
        internal Rot4 MattressDrawRotation { get { return mattressDrawRotation; } }
        internal void SetTransitRotation(Rot4 rotation)
        {
            if (Spawned) throw new InvalidOperationException("Transit facing cannot rewrite a spawned cabinet.");
            Rotation = rotation; mattressDrawRotation = Integration.UpstreamCabinetBedLayout.RotationFor(this);
        }
        public bool AutomaticLoading { get { return automaticLoading && !HasMattress && !MattressInstallationPending; } }

        public ThingOwner GetDirectlyHeldThings() { return contents; }
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);
            // Native Map/MinifiedThing enumeration already appends holder comps as siblings.
            // 原生Map/MinifiedThing已经将持有组件作为兄弟加入；再添加会重复枚举乘员。
        }

        // No map-cell storage: held items must actually be inside ThingOwner. / 不使用地面格储存：物品必须真正属于容器。
        public override IEnumerable<IntVec3> AllSlotCells() { yield break; }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (!temperatureInitialized)
            {
                internalTemperature = GenTemperature.GetTemperatureForCell(Position, map);
                temperatureInitialized = true;
            }
            base.SpawnSetup(map, respawningAfterLoad);
            InitializeCabinEnvironment();
            mattressDrawRotation = Integration.UpstreamCabinetBedLayout.RotationFor(this);
            InvalidateBatteryFlowSample();
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            FAF.Transport.CompCabinetTransport bay = GetComp<FAF.Transport.CompCabinetTransport>();
            Pawn preparing = bay.RestPreparationTarget;
            if (preparing != null) FAF.Transport.CabinetOccupancy.CancelRestPreparation(this, preparing);
            bool previous = bay.Transferring;
            Pawn resident = bay.ResidentRestPawn;
            if (!previous && resident != null && FAF.Transport.CabinetRestHandoff.Capture(resident) == this)
                resident.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            // A forced despawn of a sealed resident is not an explicit airlock/exit request.
            // 封闭住客的强制取消生成不是明确开舱请求；已交回舱的原角色不能顺带弹出。
            if (resident != null && bay.innerContainer.Contains(resident)) bay.Transferring = true;
            try { base.DeSpawn(mode); }
            finally { bay.Transferring = previous; }
            // A portable appliance has no map worker who can fulfil a pending flick request.
            // 便携设备没有地图工人可执行待办拨动，按原生已保存意图同步实际开关。
            if (!Destroyed) Integration.CabinetPowerSwitch.ApplyPortableRequest(this);
        }

        // Resource availability is tick-local; do not let camera zoom batch it over past ticks.
        // 资源可用性只属于当前tick，不允许相机缩放将其批量追认到过去的tick。
        protected override int MaxTickIntervalRate { get { return 1; } }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            TickThermalResources(delta);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            ExposeCabinEnvironment();
            Scribe_Deep.Look(ref contents, "fafContents", this);
            Scribe_Values.Look(ref internalTemperature, "fafInternalTemperature", 21f);
            Scribe_Values.Look(ref storedWattDays, "fafStoredWattDays", 0f);
            Scribe_Values.Look(ref nativeBatteryMigrated, "fafNativeBatteryMigrated", false);
            Scribe_Values.Look(ref temperatureInitialized, "fafTemperatureInitialized", false);
            Scribe_Values.Look(ref automaticLoading, "fafAutomaticLoading", true);
            Scribe_Values.Look(ref coolantBackupEnabled, "fafCoolantBackupEnabled", false);
            Scribe_Values.Look(ref rapidThermalControlRequested, "fafRapidThermalControlRequested", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (contents == null) contents = new ThingOwner<Thing>(this, false);
                CoolantTank.allowAutoRefuel = coolantBackupEnabled;
                if (ParentHolder is MinifiedThing) Integration.CabinetPowerSwitch.ApplyPortableRequest(this);
                if (!nativeBatteryMigrated)
                {
                    // Transfer old watt-days one-for-one once; all future saves use native storedPower.
                    // 旧瓦日储能仅按一比一迁移一次；后续存档只以原生storedPower为准。
                    float energy = Mathf.Clamp(Battery.StoredEnergy + storedWattDays, 0f, Battery.Props.storedEnergyMax);
                    Battery.SetStoredEnergyPct(energy / Battery.Props.storedEnergyMax);
                    storedWattDays = 0f;
                    nativeBatteryMigrated = true;
                }
            }
        }

        public bool CanAcceptItem(Thing item)
        {
            if (item == null || item == this || item.Destroyed || HasMattress) return false;
            if (!FAF.Transport.CabinetBiologicalPayload.CanAcceptCargo(this, item)
                || MattressInstallationPending && !IsPendingMattressTarget(item)) return false;
            if (item.GetInnerIfMinified() is Building_CabinetMattress)
                return contents.Count == 0 && item is MinifiedThing && CountCanAccept(item) > 0;
            if (item.def.category != ThingCategory.Item || item is MinifiedThing)
                return false;
            return GetStoreSettings().AllowedToAccept(item) && CountCanAccept(item) > 0;
        }

        public bool CanInstallMattress(Thing target)
        {
            return CanSelectMattressForInstallation(target);
        }

        public int CountCanAccept(Thing item)
        {
            return CountCanAccept(item, true);
        }

        internal int CountCanAccept(Thing item, bool canMerge)
        {
            if (item == null || item.Destroyed || !FAF.Transport.CabinetBiologicalPayload.CanAcceptCargo(this, item)
                || MattressInstallationPending && !IsPendingMattressTarget(item)) return 0;
            if (item.GetInnerIfMinified() is Building_CabinetMattress)
                return contents.Count == 0 ? CabinetPayloadCapacity.CountCanAdd(this, item, 1) : 0;
            if (HasMattress) return 0;
            int free = Math.Max(0, ItemStackCapacity - contents.Count) * item.def.stackLimit;
            for (int i = 0; i < contents.Count; i++)
                if (canMerge && contents[i].CanStackWith(item)) free += Math.Max(0, item.def.stackLimit - contents[i].stackCount);
            return CabinetPayloadCapacity.CountCanAdd(this, item, Math.Min(item.stackCount, free));
        }

        private int CountCanAcceptDef(ThingDef itemDef)
        {
            if (HasMattress || MattressInstallationPending) return 0;
            if (typeof(Corpse).IsAssignableFrom(itemDef.thingClass))
            {
                var state = FAF.Transport.CabinetOccupancy.Read(this);
                if (state.ActualOccupants.Count > 0 || state.PendingBoarders.Count > 0 || state.RestPreparationActive) return 0;
            }
            int free = Math.Max(0, ItemStackCapacity - contents.Count) * itemDef.stackLimit;
            for (int i = 0; i < contents.Count; i++)
                if (contents[i].def == itemDef) free += Math.Max(0, itemDef.stackLimit - contents[i].stackCount);
            float unitMass = itemDef.GetStatValueAbstract(StatDefOf.Mass);
            float room = CabinetPayloadCapacity.Limit - CabinetPayloadCapacity.PayloadMass(this) - CabinetPayloadCapacity.ReservedMass(this);
            return room < 0f ? 0 : unitMass > 0f
                ? Math.Min(free, Math.Max(0, Mathf.FloorToInt((room + 0.00001f) / unitMass))) : free;
        }

        public bool TryStoreCarried(Pawn pawn)
        {
            Thing item = pawn.carryTracker.CarriedThing;
            return TryStore(item, item == null ? 0 : item.stackCount) > 0;
        }

        public void Notify_ItemAdded(Thing item)
        {
            if (MapHeld != null) MapHeld.listerHaulables.Notify_AddedThing(item);
            NotifyContentsMassChanged();
        }
        public void Notify_ItemRemoved(Thing item) { NotifyContentsMassChanged(); }

        public bool Eject(Thing item, int count)
        {
            Map map = MapHeld;
            if (Destroyed || map == null || (!Spawned && !(ParentHolder is MinifiedThing))
                || count <= 0 || item == null || !contents.Contains(item)) return false;
            Thing dropped;
            bool result = contents.TryDrop(item, Spawned ? InteractionCell : PositionHeld, map, ThingPlaceMode.Near, count, out dropped);
            if (result && dropped != null) dropped.SetForbidden(true, false);
            if (result) NotifyContentsMassChanged();
            return result;
        }

        public void EjectAll()
        {
            for (int i = contents.Count - 1; i >= 0; i--) Eject(contents[i], contents[i].stackCount);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Reinstallation despawns without destroying; preserve energy and held items in that path. / 重新安装只取消生成，不销毁；该路径保留能量和内容。
            Map previousMap = MapHeld; IntVec3 previousCell = PositionHeld;
            GetComp<FAF.Transport.CompCabinetTransport>().ResidentRestPawn = null;
            if (previousMap != null) contents.TryDropAll(previousCell, previousMap, ThingPlaceMode.Near);
            base.Destroy(mode);
            // Destruction is a breach, not a free emergency launch or an immortal powered cabin.
            // 摧毁意味着破舱，不是免费应急发射或永不失压的供电棺材。
            FAF.Transport.CompCabinetTransport bay = GetComp<FAF.Transport.CompCabinetTransport>();
            if (previousMap != null)
            {
                contents.TryDropAll(previousCell, previousMap, ThingPlaceMode.Near);
                bay.innerContainer.TryDropAll(previousCell, previousMap, ThingPlaceMode.Near);
                foreach (Thing remainder in new List<Thing>(bay.innerContainer))
                    if (remainder is Pawn pawn && !pawn.Destroyed)
                    {
                        bay.innerContainer.Remove(pawn);
                        try { GenSpawn.Spawn(pawn, previousCell, previousMap); }
                        catch (Exception exception)
                        {
                            bool previousTransfer = bay.Transferring; bay.Transferring = true;
                            try { if (!pawn.Spawned) bay.innerContainer.TryAdd(pawn, false); }
                            finally { bay.Transferring = previousTransfer; }
                            Log.Error("[F.A.F.] Breach pawn placement failed; preserving the original pawn in unprotected local debris.\n" + exception);
                        }
                    }
                if (contents.Any || bay.innerContainer.Any)
                {
                    FAF.Transport.CabinetBreach breach = (FAF.Transport.CabinetBreach)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("FAF_CabinetBreach"));
                    GenSpawn.Spawn(breach, previousCell, previousMap);
                    breach.AcceptRemainders(contents); breach.AcceptRemainders(bay.innerContainer);
                }
            }
        }

        protected override void DrawAt(Vector3 drawLocation, bool flip = false)
        {
            base.DrawAt(drawLocation, flip);
            CabinetVisualRenderer.DrawContentsAndFrame(this, drawLocation);
        }
        public override Texture UIIconOverride { get { return CabinetVisualRenderer.IconFor(this); } }

        public override string GetInspectString()
        {
            StringBuilder text = new StringBuilder(base.GetInspectString());
            if (text.Length > 0) text.AppendLine();
            text.AppendLine("FAF_CabinetTemperature".Translate(internalTemperature.ToStringTemperature("F1")));

            text.AppendLine(GetThermostatSnapshot().CoolingSourceKey.Translate());
            text.Append(HasMattress ? "FAF_CabinetBedMode".Translate(Mattress.LabelCap)
                : "FAF_CabinetStored".Translate(contents.Count, ItemStackCapacity));
            return text.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                // Preserve the native icon/wanted state, but use the same map/portable command entry point.
                // 保留原生图标与期望状态，但地图和便携操作统一走同一指令入口。
                if (gizmo is Command_Toggle toggle && toggle.hotKey == KeyBindingDefOf.Command_TogglePower)
                    toggle.toggleAction = ToggleThermalPower;
                yield return gizmo;
            }
            if (Faction != Faction.OfPlayer || !Spawned) yield break;
            yield return FAF.UI.CabinetResourceGizmo.For(this);
            yield return new Command_Toggle
            {
                defaultLabel = "FAF_AutoLoad".Translate(), defaultDesc = "FAF_AutoLoadDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject"),
                isActive = () => automaticLoading, toggleAction = () => automaticLoading = !automaticLoading
            };
            Command_Action load = new Command_Action
            {
                defaultLabel = "FAF_LoadItem".Translate(), defaultDesc = "FAF_LoadItemDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject"), action = BeginLoadTarget
            };
            if (HasMattress) load.Disable("FAF_CabinetMattressExclusive".Translate());
            yield return load;
            Command_Action mattress = new Command_Action
            {
                defaultLabel = "FAF_InstallMattress".Translate(), defaultDesc = "FAF_InstallMattressDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("FAF/UI/Commands/InstallSleepingPad"), action = () => CabinetMattressMenu.Show(this)
            };
            if (HasMattress) mattress.Disable("FAF_CabinetMattressExclusive".Translate());
            yield return mattress;
            yield return FAF.UI.CabinetCargoUI.CommandFor(this);
            if (contents.Any)
                yield return new Command_Action
                {
                    defaultLabel = "FAF_EjectContents".Translate(), defaultDesc = "FAF_EjectContentsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject"), action = EjectAll
                };
        }

        private void BeginLoadTarget()
        {
            // Ordinary-item loading keeps its map targeter; mattress installation uses a candidate menu.
            // 普通物品仍可在地图点选；床垫安装使用候选菜单。
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetItems = true, canTargetPawns = false, canTargetBuildings = false,
                validator = target => target.HasThing && (target.Thing.GetInnerIfMinified() is Building_CabinetMattress
                    ? CanInstallMattress(target.Thing) : CanAcceptItem(target.Thing))
            };
            Find.Targeter.BeginTargeting(parameters, target => {
                if (target.Thing.GetInnerIfMinified() is Building_CabinetMattress) TryOrderMattress(target.Thing);
                else TryOrderLoading(target.Thing, false);
            });
        }

        internal Pawn AvailableHaulerFor(Thing target)
        {
            if (!Spawned || target == null || !target.Spawned || target.Map != Map || target.IsForbidden(Faction.OfPlayer)) return null;
            Pawn worker = null;
            float distance = float.MaxValue;
            foreach (Pawn candidate in Map.mapPawns.FreeColonistsSpawned)
            {
                if (candidate.Downed || candidate.Drafted || candidate.InMentalState
                    || candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)
                    || !candidate.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    || (target is Building_CabinetMattress && candidate.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                    || !candidate.CanReserveAndReach(target, PathEndMode.ClosestTouch, Danger.Some)
                    || !candidate.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some)) continue;
                float d = candidate.Position.DistanceToSquared(target.Position);
                if (d < distance) { distance = d; worker = candidate; }
            }
            return worker;
        }

        internal void TryOrderMattress(Thing target) { CabinetMattressMenu.ConfirmAndOrder(this, target); }

        private void TryOrderLoading(Thing target, bool mattress, bool clearingConfirmed = false)
        {
            if (!Spawned || target == null || !target.Spawned || target.Map != Map
                || !(mattress ? CanInstallMattress(target) : CanAcceptItem(target)))
            {
                Messages.Message((mattress ? "FAF_MattressNoLongerAvailable" : "FAF_ItemNoLongerAvailable").Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Pawn worker = AvailableHaulerFor(target);
            if (worker == null)
            {
                string reason = target is Building_CabinetMattress ? "FAF_NoMattressInstaller"
                    : mattress ? "FAF_NoMattressHauler" : "FAF_NoCabinetHauler";
                Messages.Message(reason.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Job loadingJob = MakeLoadJob(target);
            if (mattress) ConfigureMattressInstallation(loadingJob, worker, clearingConfirmed);
            if (!worker.jobs.TryTakeOrderedJob(loadingJob, JobTag.Misc))
            {
                Messages.Message("FAF_CabinetOrderRejected".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (mattress)
                Messages.Message("FAF_MattressOrderAccepted".Translate(worker.LabelShort, target.GetInnerIfMinified().LabelShortCap),
                    MessageTypeDefOf.NeutralEvent, false);
        }

        public Job MakeLoadJob(Thing item)
        {
            // Built mattresses use vanilla uninstall work before the existing carrying path. / 已建床垫先执行原版卸下工作，再进入现有搬运路径。
            if (item is Building_CabinetMattress)
            {
                Job install = JobMaker.MakeJob(CabinetDefOf.FAF_InstallCabinetMattress, item, this);
                install.count = 1;
                return install;
            }
            Job job = JobMaker.MakeJob(CabinetDefOf.FAF_LoadCabinet, item, this);
            job.count = item.GetInnerIfMinified() is Building_CabinetMattress ? 1 : CountCanAccept(item);
            return job;
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(pawn)) yield return option;
            foreach (FloatMenuOption option in HaulSourceUtility.GetFloatMenuOptions(this, pawn)) yield return option;
            if (UpgradeProfile.WorldLaunchUnlocked)
            {
                AcceptanceReport report = FAF.Transport.CabinetOccupancy.CanAdmit(this, pawn);
                yield return report ? new FloatMenuOption("FAF_EnterCabinet".Translate(), () => FAF.Transport.CabinetOccupancy.RequestEnter(this, pawn))
                    : new FloatMenuOption("FAF_EnterCabinet".Translate() + ": " + report.Reason, null);
            }
        }
    }
}
