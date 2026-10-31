// SPDX-License-Identifier: MPL-2.0
using System;
using HarmonyLib;
using Verse;

namespace FAF
{
    public sealed class FridgeFlightMod : Mod
    {
        public FridgeFlightMod(ModContentPack content) : base(content)
        {
            // EN: Fail visibly if the required upstream contract changed; never invent a second fridge detector.
            // 中：前置契约变化时明确报错；绝不另造一套冰箱判定规则。
            Integration.UpstreamFridges.Initialize();
            new Harmony("coahuilite.fridgesabsolutelyfly").PatchAll(typeof(FridgeFlightMod).Assembly);
        }
    }

    [StaticConstructorOnStartup]
    internal static class FridgeFlightBootstrap
    {
        static FridgeFlightBootstrap()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                Integration.UpstreamFridges.PrepareDef(def);
            Rendering.GroundPowerWires.Initialize(new Harmony("coahuilite.fridgesabsolutelyfly"));
        }
    }
}
