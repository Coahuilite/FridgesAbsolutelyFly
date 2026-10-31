// SPDX-License-Identifier: MPL-2.0
using System.Collections.Generic;
using FAF.Cabinet;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Transport
{
    public sealed class CompProperties_CabinetTransport : CompProperties_Transporter
    {
        public CompProperties_CabinetTransport()
        {
            compClass = typeof(CompCabinetTransport);
            shouldTickContents = true;
            canChangeAssignedThingsAfterStarting = true;
        }
    }
    public sealed class CompCabinetTransport : CompTransporter, IThingHolderWithDrawnPawn
    {
        // Native reclining transport posture is not a bed and never sets CurrentBed or a sleep job.
        // 原生运输躺姿不是床，不设置CurrentBed或睡眠任务。
        public float HeldPawnDrawPos_Y { get { return AltitudeLayer.Pawn.AltitudeFor() + 0.025f; } }
        public float HeldPawnBodyAngle
        {
            get { Rot4 rotation = Cabinet.MattressDrawRotation; rotation.AsInt += 2; return rotation.AsAngle; }
        }
        public PawnPosture HeldPawnPosture { get { return PawnPosture.LayingOnGroundFaceUp; } }
        internal bool Transferring;
        public bool RemoteStationKeeping;
        internal bool AutonomousMission;
        internal Pawn RestPreparationTarget;
        internal Pawn ResidentRestPawn;
        public RimWorld.Planet.PlanetTile RecoveryAnchor = RimWorld.Planet.PlanetTile.Invalid;
        internal int LastManeuverTick = -999999;
        public override bool RequiresFuelingPort { get { return false; } }
        internal Building_FlyingFridgeCabinet Cabinet { get { return (Building_FlyingFridgeCabinet)parent; } }
        // Both commands read the existing saved maneuver tick; no UI or per-ability clock is introduced.
        // 两种指令只读取既有保存的机动tick，不新增UI或各能力独立时钟。
        internal int ManeuverCooldownTotalTicks { get { return CabinetTravelDef.Current.cooldownTicks; } }
        internal int ManeuverCooldownRemainingTicks
        {
            get { return Mathf.Max(0, ManeuverCooldownTotalTicks - (Find.TickManager.TicksGame - LastManeuverTick)); }
        }
        internal bool CoolingDown { get { return ManeuverCooldownRemainingTicks > 0; } }
        internal void InvalidateCargoMass() { Notify_ThingRemoved(null); }
        internal void CancelArrangements(Map map)
        {
            List<CompCabinetTransport> bays = groupID < 0 || map == null ? new List<CompCabinetTransport> { this }
                : new List<CompCabinetTransport>(System.Linq.Enumerable.OfType<CompCabinetTransport>(TransportersInGroup(map)));
            if (map != null) TryRemoveLord(map);
            foreach (CompCabinetTransport bay in bays) { bay.leftToLoad?.Clear(); bay.groupID = -1; }
            if (map == null) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                Thing destination = pawn.CurJob?.def == JobDefOf.EnterTransporter ? pawn.CurJob.targetA.Thing
                    : (pawn.jobs?.curDriver as JobDriver_HaulToTransporter)?.Container;
                if (destination != null && bays.Exists(b => b.parent == destination))
                    pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            }
        }
        public override void Initialize(CompProperties properties)
        {
            base.Initialize(properties);
            massCapacityOverride = CabinetTravelDef.Current.massCapacity;
        }
        public override void PostExposeData()
        {
            bool legacyWithoutBay = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?["innerContainer"] == null;
            // Native saving temporarily removes/re-adds held world pawns. Restoring those original pawns is
            // not new boarding, even when an older saved load is overweight or has conflicting occupants.
            // 原生保存会暂时移出再加入被持有的世界Pawn；恢复原人不是新增登舱，旧超重/冲突也必须完整保留。
            bool previousTransfer = Transferring;
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.PostLoadInit) Transferring = true;
            try { base.PostExposeData(); }
            finally { Transferring = previousTransfer; }
            if (legacyWithoutBay) { innerContainer = new ThingOwner<Thing>(this); groupID = -1; }
            Scribe_Values.Look(ref LastManeuverTick, "fafLastManeuverTick", -999999);
            Scribe_Values.Look(ref RemoteStationKeeping, "fafRemoteStationKeeping", false);
            Scribe_Values.Look(ref AutonomousMission, "fafAutonomousMission", false);
            Scribe_Values.Look(ref RecoveryAnchor, "fafRecoveryAnchor");
            Scribe_References.Look(ref RestPreparationTarget, "fafRestPreparationTarget");
            Scribe_References.Look(ref ResidentRestPawn, "fafResidentRestPawn");
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RefreshLoadingCapacity();
        }
        internal void RefreshLoadingCapacity()
        {
            // Native bay loading shares the net payload budget with the other held compartment and sleeper.
            // 原生运输舱装载与另一实际持有仓及睡眠者共用净载荷预算，本柜壳不占额度。
            massCapacityOverride = Mathf.Max(0.001f, CabinetPayloadCapacity.Limit - CabinetPayloadCapacity.PayloadMass(Cabinet)
                + CollectionsMassCalculator.MassUsage(innerContainer, IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true));
        }
        public override void CompTick()
        {
            base.CompTick();
            CabinetRestHandoff.ValidateIntent(this);
            CabinetRestPreparation.TickHeld(this);
            if (parent.Spawned && parent.IsHashIntervalTick(250)) RefreshLoadingCapacity();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            // The commit already moved every bay entity; native cancellation must not unload sibling bays.
            // 提交已转移全部装载实体；此时原生取消不能顺带卸下同组其他柜。
            if (!Transferring)
            {
                groupID = -1; leftToLoad?.Clear();
                innerContainer.TryDropAll(parent.Position, map, ThingPlaceMode.Near);
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!parent.Spawned || parent.Faction != Faction.OfPlayer) yield break;
            Command_Action exit = CabinetQuickExit.CommandFor(Cabinet);
            if (exit != null) yield return exit;
            if (Cabinet.UpgradeProfile.MapJumpUnlocked)
            {
                Command_Action jump = FAF.UI.CabinetCooldownCommand.Create(
                    () => ManeuverCooldownRemainingTicks, () => ManeuverCooldownTotalTicks,
                    "FAF_MapJump".Translate(), (Cabinet.UpgradeProfile.WorldLaunchUnlocked ? "FAF_MapJumpBoardedDesc" : "FAF_MapJumpDesc").Translate(CabinetTravelDef.Current.mapRange),
                    ContentFinder<Texture2D>.Get("FAF/UI/Commands/NearFieldPulse"), () => CabinetMapJump.Choose(Cabinet));
                jump.groupable = false;
                if (CoolingDown) jump.Disable("FAF_Cooldown".Translate());
                yield return jump;
            }
            if (Cabinet.UpgradeProfile.MapJumpUnlocked) yield return new Command_Action {
                defaultLabel = "FAF_RefuelLaunch".Translate(), defaultDesc = "FAF_RefuelLaunchDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("FAF/UI/Commands/RefillCoolant"), action = () => Cabinet.RequestLaunchCoolantRefuel()
            };
            if (!Cabinet.UpgradeProfile.WorldLaunchUnlocked) yield break;
            if (RemoteStationKeeping && parent.Tile.LayerDef.isSpace)
                yield return new Command_Action {
                    defaultLabel = "FAF_AbandonStation".Translate(), defaultDesc = "FAF_AbandonStationDesc".Translate(),
                    icon = CancelLoadCommandTex, action = () => {
                        if (Cabinet.Contents.Any || innerContainer.Any || CabinetOccupancy.Read(Cabinet).RestPreparationActive)
                        { CabinetMapJump.Reject("FAF_EmptyBeforeAbandon".Translate()); return; }
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("FAF_AbandonStationDesc".Translate(), () => {
                            if (parent.Spawned && !Cabinet.Contents.Any && !innerContainer.Any && !CabinetOccupancy.Read(Cabinet).RestPreparationActive)
                                RemoteStationKeeping = false;
                        }));
                    }
                };
            RefreshLoadingCapacity();
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                if (gizmo is Command_Action cancel && cancel.icon == CancelLoadCommandTex)
                {
                    cancel.defaultDesc = "FAF_CancelArrangementsDesc".Translate();
                    cancel.action = () => CancelArrangements(parent.Map);
                }
                yield return gizmo;
            }
            Command_Action launch = FAF.UI.CabinetCooldownCommand.Create(
                () => CabinetWorldTravel.MapCooldownRemainingTicks(this), () => ManeuverCooldownTotalTicks,
                "FAF_Launch".Translate(), "FAF_LaunchDesc".Translate(CabinetTravelDef.Current.massCapacity, CabinetTravelDef.Current.worldRange),
                ContentFinder<Texture2D>.Get("FAF/UI/Commands/LaunchCabinet"), () => CabinetWorldTravel.ChooseFromMap(this));
            launch.groupable = false;
            if (CabinetWorldTravel.MapCooldownRemainingTicks(this) > 0) launch.Disable("FAF_Cooldown".Translate());
            yield return launch;
        }
    }
}
