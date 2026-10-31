// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using HarmonyLib;
using FAF.Rendering;
using UnityEngine;
using Waylith.Rendering;

namespace FAF.Adapters
{
    [HarmonyPatch]
    internal static class WaylithFlightRenderAdapter
    {
        private static MethodBase TargetMethod()
        {
            // EN: Native Waylith submission covers both native draws and the game's Unity compatibility layer.
            // 中：Waylith 原生提交同时覆盖原生绘制与游戏的 Unity 兼容层。
            return AccessTools.DeclaredMethod(typeof(MeshDrawSubmission), "Enqueue",
                new[] { typeof(MeshDrawCommand).MakeByRefType() });
        }

        private static void Prefix(ref MeshDrawCommand __0)
        {
            Matrix4x4 visual;
            if (!FlightDrawScope.TryGetVisualTransform(out visual)) return;
            // EN: UnityStubs.ToNumerics preserves its column-vector layout; prepend the world-space transform.
            // 中：UnityStubs.ToNumerics 保留列向量布局，因此将世界空间变换乘在原矩阵左侧。
            __0 = new MeshDrawCommand(__0.Mesh, __0.Material, __0.PropertyRange,
                Matrix4x4.ToNumerics(visual) * __0.Matrix, __0.SubmeshIndex, __0.PassId,
                __0.Instancing, __0.RenderQueueOverride, __0.Layer, __0.Storage);
        }
    }
}
