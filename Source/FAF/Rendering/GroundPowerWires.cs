// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Rendering
{
    internal static class GroundPowerWires
    {
        // EN: Only this private Print destination excludes wires; ordinary map and power-grid layers stay native.
        // 中：仅此私有 Print 目标排除电线；普通地图层与电网图层继续使用原生逻辑。
        [System.ThreadStatic] internal static SectionLayer CapturingLayer;

        internal static void Initialize(Harmony harmony)
        {
            // EN: PowerNetGraphics owns a static wire material. Resolve its patch only after main-thread asset startup.
            // 中：PowerNetGraphics 持有静态电线材质；仅在主线程资源启动完成后绑定补丁。
            harmony.Patch(AccessTools.Method(typeof(PowerNetGraphics), nameof(PowerNetGraphics.PrintWirePieceConnecting)),
                new HarmonyMethod(typeof(ExcludeGroundWiresFromFlightSnapshot), "Prefix"));
        }

        internal static void PrintConnections(ThingWithComps building, SectionLayer groundLayer)
        {
            foreach (ThingComp comp in building.AllComps)
            {
                CompPower power = comp as CompPower;
                if (power == null || power.connectParent == null
                    || power.connectParent.parent.def == ThingDefOf.HiddenConduit) continue;
                // EN: Use the same public endpoint relationship and printer as CompPower.PostPrintOnto.
                // 中：沿用 CompPower.PostPrintOnto 的公开端点关系和原生打印入口，不猜测材质。
                PowerNetGraphics.PrintWirePieceConnecting(groundLayer, building,
                    power.connectParent.parent, false);
            }
        }
    }

    internal static class ExcludeGroundWiresFromFlightSnapshot
    {
        private static bool Prefix(SectionLayer layer)
        {
            return GroundPowerWires.CapturingLayer == null
                || !ReferenceEquals(layer, GroundPowerWires.CapturingLayer);
        }
    }
}
