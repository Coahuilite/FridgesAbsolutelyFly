// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using RimWorld;
using FAF.Cabinet.Research;
using UnityEngine;
using Verse;

namespace FAF
{
    public sealed class CompProperties_FridgeFlight : CompProperties
    {
        public CompProperties_FridgeFlight() { compClass = typeof(CompFridgeFlight); }
    }

    public sealed class CompFridgeFlight : ThingComp
    {
        private bool flying;
        private bool rotating = true;
        private float liftFraction;
        private float visualAngle;
        private float angularVelocity;
        private float bobAmplitude = 0.12f;
        private bool poseInitialized;
        private bool steadyAttitude;
        private float angularSpeed = 0.35f;
        private Rendering.FlyingPrintLayer printedAppearance;
        private bool printDirty = true;
        private bool printRebuildBlocked;
        private bool visualFailureLogged;
        private const float BaseLift = 0.48f;
        private const float BobAmplitude = 0.12f;
        public bool Flying { get { return parent.Spawned && (flying || liftFraction > 0f || Mathf.Abs(Mathf.DeltaAngle(visualAngle, 0f)) > 0.001f || Mathf.Abs(angularVelocity) > 0.001f); } }
        public bool Rotating { get { return rotating; } }
        internal float CurrentVisualAngle { get { return visualAngle; } }
        internal float CurrentVisualLift
        {
            get
            {
                float ticks = Find.TickManager == null ? 0f : Find.TickManager.TicksGame;
                return Mathf.SmoothStep(0f, 1f, liftFraction) * (BaseLift + bobAmplitude
                    * Mathf.Sin(ticks * 0.035f + parent.thingIDNumber * 0.61803399f));
            }
        }
        public float AngularSpeed { get { return angularSpeed; } set { if (!float.IsNaN(value) && !float.IsInfinity(value)) angularSpeed = Mathf.Clamp(value, -1.5f, 1.5f); } }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref flying, "fafFlying", false);
            Scribe_Values.Look(ref rotating, "fafRotating", true);
            Scribe_Values.Look(ref liftFraction, "fafLiftFraction", 0f);
            Scribe_Values.Look(ref visualAngle, "fafVisualAngle", 0f);
            Scribe_Values.Look(ref angularVelocity, "fafAngularVelocity", 0f);
            Scribe_Values.Look(ref bobAmplitude, "fafBobAmplitude", 0.12f);
            Scribe_Values.Look(ref poseInitialized, "fafPoseInitialized", false);
            Scribe_Values.Look(ref steadyAttitude, "fafSteadyAttitude", false);
            Scribe_Values.Look(ref angularSpeed, "fafAngularSpeed", 0.35f);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!poseInitialized)
            {
                // Preserve the previous full-height saved pose, then let the normal transition land it if locked.
                // 保留旧存档的完整高度，再由正常过渡处理未研究时的回正降落。
                liftFraction = flying && respawningAfterLoad ? 1f : 0f;
                visualAngle = flying && rotating && respawningAfterLoad
                    ? (Find.TickManager.TicksGame * 0.35f + parent.thingIDNumber * 17f) % 360f : 0f;
                poseInitialized = true;
            }
            if (!respawningAfterLoad) liftFraction = visualAngle = angularVelocity = 0f;
            if (Flying) parent.Map.GetComponent<Rendering.FlightMapComponent>().Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.GetComponent<Rendering.FlightMapComponent>().Unregister(this);
            ReleasePrintedAppearance();
            base.PostDeSpawn(map, mode);
        }

        internal void InvalidatePrintedAppearance()
        {
            printDirty = true;
            printRebuildBlocked = false;
        }

        internal void ReleasePrintedAppearance()
        {
            if (printedAppearance != null) printedAppearance.Dispose();
            printedAppearance = null;
            printDirty = true;
            printRebuildBlocked = false;
            visualFailureLogged = false;
        }

        internal void DrawPrintedAppearance(CellRect view)
        {
            if (!Flying || parent.def.drawerType == DrawerType.RealtimeOnly || parent.def.drawerType == DrawerType.None) return;
            if (!PrintedAppearanceInView(view)) return;
            if (printDirty && !printRebuildBlocked) RebuildPrintedAppearance();
            if (!Flying || printedAppearance == null) return;
            CompFridgeFlight previous = Rendering.FlightDrawScope.Current;
            try
            {
                Rendering.FlightDrawScope.Current = this;
                printedAppearance.DrawLayer();
            }
            finally { Rendering.FlightDrawScope.Current = previous; }
        }

        private bool PrintedAppearanceInView(CellRect view)
        {
            if (parent.def.drawOffscreen) return true;
            Vector2 size = parent.DrawSize;
            Vector3 offset = parent.Graphic.DrawOffset(parent.Rotation);
            float radius = 0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y)
                + Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            if (printedAppearance != null) radius = Mathf.Max(radius, printedAppearance.DrawRadius);
            // A rotation-invariant envelope includes all declared graphics, known Print layers and the full bob.
            // 与旋转无关的包围范围覆盖声明绘制尺寸、已知 Print 附加层和完整浮动幅度。
            radius += BaseLift + BobAmplitude;
            Vector3 pivot = parent.DrawPos;
            CellRect bounds = CellRect.FromLimits(Mathf.FloorToInt(pivot.x - radius), Mathf.FloorToInt(pivot.z - radius),
                Mathf.CeilToInt(pivot.x + radius), Mathf.CeilToInt(pivot.z + radius));
            return view.Overlaps(bounds);
        }

        private void RebuildPrintedAppearance()
        {
            Rendering.FlyingPrintLayer candidate = null;
            try
            {
                candidate = new Rendering.FlyingPrintLayer(parent.Map.mapDrawer.SectionAt(parent.Position), parent);
                candidate.Regenerate();
                Rendering.FlyingPrintLayer previous = printedAppearance;
                printedAppearance = candidate;
                candidate = null;
                printDirty = false;
                visualFailureLogged = false;
                if (previous != null) previous.Dispose();
            }
            catch (Exception exception)
            {
                // Never publish a partially printed layer. Retry only after a real native dirty notification.
                // 不发布未完成的印刷层；只在真实原生脏标记通知之后重试。
                printRebuildBlocked = true;
                LogVisualFailure(exception, printedAppearance != null
                    ? "Keeping the last valid snapshot; the next native Things invalidation will retry."
                    : "No valid snapshot exists; returning this instance to native grounded rendering.");
                if (printedAppearance == null) EndFlightAfterFailure();
            }
            finally { if (candidate != null) candidate.Dispose(); }
        }

        internal void HandleDrawFailure(Exception exception)
        {
            LogVisualFailure(exception, "Returning this instance to native grounded rendering after a draw failure.", true);
            EndFlightAfterFailure();
        }

        private void LogVisualFailure(Exception exception, string recovery, bool force = false)
        {
            if (visualFailureLogged && !force) return;
            visualFailureLogged = true;
            Log.Error("[F.A.F.] Flying visual failed for " + parent + " (" + parent.def.defName + "). "
                + recovery + "\n" + exception);
        }

        private void EndFlightAfterFailure()
        {
            flying = false;
            liftFraction = visualAngle = angularVelocity = 0f;
            Map map = parent.Map;
            if (map != null) map.GetComponent<Rendering.FlightMapComponent>().Unregister(this);
            ReleasePrintedAppearance();
            if (map != null) parent.DirtyMapMesh(map);
        }

        private void ToggleFlight()
        {
            if (!parent.Spawned || !CabinetResearch.Current.FlotationUnlocked) return;
            flying = !flying;
            parent.Map.GetComponent<Rendering.FlightMapComponent>().Register(this);
            parent.DirtyMapMesh(parent.Map);
        }

        // MapComponent ticks this even for original fridge defs with tickerType Never.
        // 由MapComponent更新，原冰箱即使tickerType为Never也能完成平滑过渡。
        internal void AdvancePose()
        {
            if (!CabinetResearch.Current.FlotationUnlocked) flying = false;
            bool fineControl = parent is Cabinet.Building_FlyingFridgeCabinet
                && CabinetResearch.Current.FineAttitudeControlUnlocked;
            // Stability reduces bob only; rotation and signed zero-speed hold remain independent.
            // 稳定只降低浮动幅度；旋转与有符号零速保持互相独立。
            bool spin = flying && rotating && liftFraction >= 1f;
            float delta = Mathf.DeltaAngle(visualAngle, 0f);
            const float acceleration = 0.015f;
            float targetVelocity = spin ? (fineControl ? angularSpeed : 0.35f)
                : Mathf.Sign(delta) * Mathf.Min(2f, Mathf.Sqrt(2f * acceleration * Mathf.Abs(delta)));
            angularVelocity = Mathf.MoveTowards(angularVelocity, targetVelocity, acceleration);
            // Brake through an angle-continuous return. Only a sub-pixel final step is clamped to the exact heading.
            // 先连续制动再回正；只有最后不可见的小步才钳至精确朝向。
            if (!spin && Mathf.Abs(delta) < 0.015f && Mathf.Abs(angularVelocity) < 0.03f)
                visualAngle = angularVelocity = 0f;
            else visualAngle = Mathf.Repeat(visualAngle + angularVelocity, 360f);
            bool upright = visualAngle == 0f && angularVelocity == 0f;
            if (flying) liftFraction = Mathf.MoveTowards(liftFraction, 1f, 1f / 90f);
            else if (upright) liftFraction = Mathf.MoveTowards(liftFraction, 0f, 1f / 90f);
            bobAmplitude = Mathf.MoveTowards(bobAmplitude, fineControl && steadyAttitude ? 0.03f : BobAmplitude, 0.001f);
            if (!Flying && parent.Spawned)
            {
                parent.Map.GetComponent<Rendering.FlightMapComponent>().Unregister(this);
                ReleasePrintedAppearance();
                parent.DirtyMapMesh(parent.Map);
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned || !CabinetResearch.Current.FlotationUnlocked) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "FAF_Fly".Translate(),
                defaultDesc = "FAF_FlyDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("FAF/UI/Commands/Fly"),
                isActive = () => flying,
                toggleAction = ToggleFlight
            };
            Command_Toggle rotation = new Command_Toggle
            {
                defaultLabel = "FAF_Rotate".Translate(),
                defaultDesc = "FAF_RotateDesc".Translate(),
                icon = TexUI.RotRightTex,
                isActive = () => rotating,
                toggleAction = () => rotating = !rotating
            };
            if (!flying) rotation.Disable("FAF_RotationRequiresFlight".Translate());
            yield return rotation;
            if (parent is Cabinet.Building_FlyingFridgeCabinet && CabinetResearch.Current.FineAttitudeControlUnlocked)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "FAF_Steady".Translate(), defaultDesc = "FAF_SteadyDesc".Translate(),
                    icon = TexUI.RotRightTex, isActive = () => steadyAttitude,
                    toggleAction = () => steadyAttitude = !steadyAttitude
                };
                yield return new Command_Action
                {
                    defaultLabel = "FAF_AngularSpeed".Translate(), defaultDesc = "FAF_AngularSpeedDesc".Translate(),
                    icon = TexUI.RotRightTex, action = () => Find.WindowStack.Add(new UI.Window_FridgeAttitude(this))
                };
            }
        }

        internal Matrix4x4 VisualTransform
        {
            get
            {
                // EN: Game ticks keep animation deterministic and paused with the simulation; no logical rotation changes.
                // 中：游戏 tick 让动画跟随暂停并保持确定性；不改变建筑的逻辑朝向。
                float lift = CurrentVisualLift;
                float angle = visualAngle;
                Vector3 pivot = parent.DrawPos;
                Vector3 raised = pivot + new Vector3(0f, 0.06f * liftFraction, lift);
                return Matrix4x4.Translate(raised) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up))
                    * Matrix4x4.Translate(-pivot);
            }
        }
    }
}
