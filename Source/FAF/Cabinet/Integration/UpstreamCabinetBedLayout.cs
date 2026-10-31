// SPDX-License-Identifier: MPL-2.0
using System;
using System.Reflection;
using HarmonyLib;
using NivarianSleepInFridges;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Read the upstream bed layout instead of duplicating its direction rules. / 读取前置真实床位布局，不复制其方向规则。</summary>
    internal static class UpstreamCabinetBedLayout
    {
        private static readonly MethodInfo forMethod;
        private static readonly PropertyInfo rotationProperty;

        static UpstreamCabinetBedLayout()
        {
            Type layout = typeof(CompFridgeSleep).Assembly.GetType("NivarianSleepInFridges.FridgeBedLayout", true);
            forMethod = AccessTools.Method(layout, "For", new[] { typeof(ThingWithComps) });
            rotationProperty = AccessTools.Property(layout, "Rotation");
            if (forMethod == null || rotationProperty == null)
                throw new InvalidOperationException("[F.A.F.] Required N.S.I.F. bed layout API is unavailable. Update both mods together.");
        }

        internal static Rot4 RotationFor(ThingWithComps cabinet)
        {
            // Called on spawn/reinstallation, not for every frame. / 仅在生成或重新安装时调用，不逐帧反射。
            object layout = forMethod.Invoke(null, new object[] { cabinet });
            return (Rot4)rotationProperty.GetValue(layout, null);
        }
    }
}
