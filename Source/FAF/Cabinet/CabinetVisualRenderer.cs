// SPDX-License-Identifier: MPL-2.0
using FAF.Cabinet.Core;
using FAF.Cabinet.Research;
using HarmonyLib;
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Draw held objects between the interior and the transparent frame. / 将持有物绘制在内底与透明窗框之间。</summary>
    [StaticConstructorOnStartup]
    internal static class CabinetVisualRenderer
    {
        // One fixed-camera top projection for items, the mattress and our cabinet's sleeper.
        // 物品、床垫及本柜睡眠者共用同一个固定镜头顶面投影；不得先随逻辑朝向旋转。
        internal static readonly Vector3 TopSurfaceOffset = new Vector3(0f, 0f, 0.20f);

        // Load materials during the game's main-thread startup phase. / 在游戏主线程启动阶段加载材质。
        private static readonly Graphic[] Frames = {
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinet", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white),
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinetPepperFrame", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white),
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinetSaltFrame", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white)
        };
        private static readonly Graphic[] Interiors = {
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinetInterior", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white),
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinetPepperInterior", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white),
            GraphicDatabase.Get<Graphic_Multi>("FAF/Buildings/FlyingCabinetSaltInterior", ShaderDatabase.Cutout, new Vector2(4f, 4f), Color.white)
        };
        private static readonly Texture2D[] Icons = {
            ContentFinder<Texture2D>.Get("FAF/UI/FlyingCabinet"),
            ContentFinder<Texture2D>.Get("FAF/UI/FlyingCabinetPepper"),
            ContentFinder<Texture2D>.Get("FAF/UI/FlyingCabinetSalt")
        };
        private static readonly Graphic[] PackedGraphics = CreatePackedGraphics();
        internal static readonly Graphic DepartingGraphic = GraphicDatabase.Get<Graphic_Single>(
            "FAF/UI/FlyingCabinetSalt", ShaderDatabase.CutoutFlying,
            new Vector2(2.2f * Icons[2].width / Icons[2].height, 2.2f), Color.white);
        private static Graphic[] CreatePackedGraphics()
        {
            string[] paths = { "FAF/UI/FlyingCabinet", "FAF/UI/FlyingCabinetPepper", "FAF/UI/FlyingCabinetSalt" };
            Graphic[] graphics = new Graphic[paths.Length];
            for (int index = 0; index < paths.Length; index++)
            {
                float ratio = (float)Icons[index].width / Icons[index].height;
                Vector2 size = ratio < 1f ? new Vector2(1.1f * ratio, 1.1f) : new Vector2(1.1f, 1.1f / ratio);
                graphics[index] = GraphicDatabase.Get<Graphic_Single>(paths[index], ShaderDatabase.Cutout, size, Color.white);
            }
            return graphics;
        }
        internal static Graphic PackedFor(Building_FlyingFridgeCabinet cabinet) { return PackedGraphics[cabinet.UpgradeProfile.VisualStage]; }
        internal static Graphic BodyFor(Building_FlyingFridgeCabinet cabinet) { return Interiors[cabinet.UpgradeProfile.VisualStage]; }
        internal static Texture2D CurrentIcon { get { return Icons[CabinetResearch.Current.VisualStage]; } }
        public static Texture2D IconFor(Building_FlyingFridgeCabinet cabinet) { return Icons[cabinet.UpgradeProfile.VisualStage]; }
        public static bool IsCabinetIcon(Texture texture) { return Array.IndexOf(Icons, texture) >= 0; }

        static CabinetVisualRenderer()
        {
            CabinetResearch.Changed += profile => {
                // Graphics/materials are cached; only native instance Print caches need invalidation.
                // Graphic及材质已缓存；只使原生实例Print缓存失效，不改共享Def。
                foreach (Map map in Find.Maps)
                    foreach (Thing thing in map.listerThings.AllThings)
                        if (thing is Building_FlyingFridgeCabinet || (thing as MinifiedThing)?.InnerThing is Building_FlyingFridgeCabinet)
                            thing.DirtyMapMesh(map);
            };
        }
        internal static void DrawContentsAndFrame(Building_FlyingFridgeCabinet cabinet, Vector3 location)
        {
            Building_CabinetMattress mattress = cabinet.Mattress;
            if (mattress != null)
            {
                Vector3 bedLocation = location + TopSurfaceOffset;
                bedLocation.y = AltitudeLayer.Building.AltitudeFor() + 0.025f;
                mattress.Graphic.Draw(bedLocation, cabinet.MattressDrawRotation, mattress);
            }
            else
            {
                ThingOwner items = cabinet.GetDirectlyHeldThings();
                for (int index = 0; index < items.Count; index++)
                {
                    Thing item = items[index];
                    // Capacity-derived rows retain every real stack inside the same window. / 行数随真实容量派生，所有物品仍位于同一窗口内。
                    int column = index % 3;
                    int row = index / 3;
                    int rows = Mathf.CeilToInt(cabinet.ItemStackCapacity / 3f);
                    float cellHeight = 1.5f / rows;
                    Vector3 localOffset = new Vector3((column - 1) * 0.235f, 0f, 0.75f - cellHeight * (row + 0.5f));
                    Vector3 itemLocation = location + cabinet.Rotation.AsQuat * localOffset + TopSurfaceOffset;
                    itemLocation.y = AltitudeLayer.Building.AltitudeFor() + 0.045f + index * 0.0001f;
                    Graphic graphic = item.Graphic;
                    Vector2 size = graphic.drawSize;
                    float scale = Mathf.Min(0.215f / Mathf.Max(size.x, 0.01f), Mathf.Min(0.33f, cellHeight * 0.88f) / Mathf.Max(size.y, 0.01f));
                    Matrix4x4 matrix = Matrix4x4.TRS(itemLocation,
                        cabinet.Rotation.AsQuat, new Vector3(scale, 1f, scale));
                    Graphics.DrawMesh(graphic.MeshAt(Rot4.North), matrix, graphic.MatAt(Rot4.North, item), 0);
                }
            }
            FAF.Transport.CompCabinetTransport bay = cabinet.GetComp<FAF.Transport.CompCabinetTransport>();
            if (bay != null)
                foreach (Pawn passenger in FAF.Transport.CabinetOccupancy.Read(cabinet).ActualOccupants)
                    if (!passenger.Destroyed && !passenger.Spawned)
                    {
                        Vector3 passengerLocation = location + TopSurfaceOffset;
                        passengerLocation.y = bay.HeldPawnDrawPos_Y;
                        PawnRenderer renderer = passenger.Drawer.renderer;
                        renderer.EnsureGraphicsInitialized();
                        renderer.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw, passengerLocation, Rot4.South, neverAimWeapon: true);
                        renderer.RenderPawnAt(passengerLocation, Rot4.South, neverAimWeapon: true);
                    }
            // The window itself is alpha-zero; only the surrounding rim is above the sleeper.
            // 窗口本身完全透明，仅外围窗框位于睡眠者上方。
            Vector3 frameLocation = location;
            frameLocation.y = AltitudeLayer.Pawn.AltitudeFor() + 0.15f;
            Frames[cabinet.UpgradeProfile.VisualStage].Draw(frameLocation, cabinet.Rotation, cabinet);
        }
    }
}




namespace FAF.Cabinet
{
    [HarmonyPatch(typeof(Thing), "get_Graphic")]
    internal static class CabinetBodyGraphicPatch
    {
        private static void Postfix(Thing __instance, ref Graphic __result)
        {
            Building_FlyingFridgeCabinet cabinet = __instance as Building_FlyingFridgeCabinet;
            if (cabinet != null) __result = CabinetVisualRenderer.BodyFor(cabinet);
        }
    }
    [HarmonyPatch(typeof(MinifiedThing), "get_Graphic")]
    internal static class PackedCabinetGraphicPatch
    {
        private static void Postfix(MinifiedThing __instance, ref Graphic __result)
        {
            Building_FlyingFridgeCabinet cabinet = __instance.InnerThing as Building_FlyingFridgeCabinet;
            if (cabinet != null) __result = CabinetVisualRenderer.PackedFor(cabinet);
        }
    }
}
