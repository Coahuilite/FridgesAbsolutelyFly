// SPDX-License-Identifier: MPL-2.0
using System;
using System.Reflection;
using HarmonyLib;
using FAF.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FAF.Adapters
{
    [HarmonyPatch]
    internal static class UnityFlightRenderAdapter
    {
        private static MethodBase TargetMethod()
        {
            // EN: This is Unity's final managed overload before native submission, avoiding double transforms.
            // 中：此为进入原生提交前的最后一个托管重载，避免多层重载造成重复变换。
            return AccessTools.DeclaredMethod(typeof(Graphics), "DrawMesh", new[]
            {
                typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int), typeof(Camera), typeof(int),
                typeof(MaterialPropertyBlock), typeof(ShadowCastingMode), typeof(bool), typeof(Transform),
                typeof(LightProbeUsage), typeof(LightProbeProxyVolume)
            });
        }

        private static void Prefix(ref Matrix4x4 __1)
        {
            Matrix4x4 visual;
            if (FlightDrawScope.TryGetVisualTransform(out visual)) __1 = visual * __1;
        }
    }
}
