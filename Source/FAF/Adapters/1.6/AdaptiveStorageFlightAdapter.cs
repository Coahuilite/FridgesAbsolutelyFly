// SPDX-License-Identifier: MPL-2.0
using System;
using System.Reflection;
using HarmonyLib;
using FAF.Rendering;
using UnityEngine;
using Verse;

namespace FAF.Adapters
{
    // EN: ASF's optimized renderer bypasses public Graphics.DrawMesh and submits through Unity's injected call.
    // 中：ASF 优化绘制绕过公共 Graphics.DrawMesh，直接使用 Unity 的 injected 提交入口。
    internal static class AdaptiveStorageFlightBridge
    {
        private static bool bindingsResolved;
        private static MethodBase cachedTarget;
        private static MethodBase scaledTarget;
        private static AccessTools.FieldRef<object, Matrix4x4> cachedMatrix;

        internal static MethodBase Resolve(bool cached)
        {
            if (!bindingsResolved) ResolveBindings();
            return cached ? cachedTarget : scaledTarget;
        }

        private static void ResolveBindings()
        {
            bindingsResolved = true;
            Type printDataType;
            try { printDataType = AccessTools.TypeByName("AdaptiveStorage.PrintDatas.OptimizedPrintData"); }
            catch (Exception exception)
            {
                Log.Warning("[F.A.F.] Optional ASF 1.6 direct-draw hooks were not installed: type binding failed.\n" + exception);
                return;
            }
            if (printDataType == null) return;
            string skipped = null;
            // Bind each optional overload independently; a broken cached path must not disable a valid scaled path.
            // 分别绑定可选重载；缓存路径漂移不能禁用仍匹配的缩放路径，也不能中断必需 patch 安装。
            try
            {
                MethodInfo method = AccessTools.DeclaredMethod(printDataType, "DrawAtInternal",
                    new[] { typeof(Vector3).MakeByRefType() });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                    throw new MissingMethodException("Expected instance void DrawAtInternal(in Vector3).");
                FieldInfo field = AccessTools.Field(printDataType, "_matrix");
                if (field == null || field.IsStatic || field.FieldType != typeof(Matrix4x4))
                    throw new MissingFieldException("Expected instance Matrix4x4 _matrix.");
                cachedMatrix = AccessTools.FieldRefAccess<Matrix4x4>(printDataType, "_matrix");
                cachedTarget = method;
            }
            catch (Exception exception) { skipped = "Cached overload skipped: " + exception; }
            try
            {
                MethodInfo method = AccessTools.DeclaredMethod(printDataType, "DrawAtInternal",
                    new[] { typeof(Vector3), typeof(Vector2), typeof(float) });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                    throw new MissingMethodException("Expected instance void DrawAtInternal(Vector3, Vector2, float).");
                scaledTarget = method;
            }
            catch (Exception exception) { skipped += "\nScaled overload skipped: " + exception; }
            if (skipped != null)
                Log.Warning("[F.A.F.] Unmatched optional ASF 1.6 direct-draw hooks were not installed. "
                    + "Required fridge integration and Print capture remain active; flight of this ASF version's direct-draw extras is not fully supported.\n" + skipped);
        }

        internal static ref Matrix4x4 CachedMatrix(object printData) { return ref cachedMatrix(printData); }

        internal struct DrawState
        {
            internal bool Applied;
            internal CompFridgeFlight Scope;
            internal Vector3 Position;
            internal Matrix4x4 Matrix;
            internal float Rotation;
        }
    }

    [HarmonyPatch]
    internal static class AdaptiveStorageCachedFlightDraw
    {
        private static MethodBase target;
        private static bool Prepare()
        {
            target = AdaptiveStorageFlightBridge.Resolve(true);
            return target != null;
        }
        private static MethodBase TargetMethod() { return target; }

        private static void Prefix(object __instance, ref Vector3 __0,
            out AdaptiveStorageFlightBridge.DrawState __state)
        {
            __state = default(AdaptiveStorageFlightBridge.DrawState);
            CompFridgeFlight flight = FlightDrawScope.Current;
            if (flight == null || !flight.Flying) return;

            ref Matrix4x4 cached = ref AdaptiveStorageFlightBridge.CachedMatrix(__instance);
            __state.Scope = flight;
            __state.Position = __0;
            __state.Matrix = cached;
            __state.Applied = true;
            Matrix4x4 visual = flight.VisualTransform;
            // EN: ASF replaces the cached matrix's translation with this location inside the original method.
            // 中：ASF 原方法会用此位置替换缓存矩阵的平移，因此位置和线性变换需同时处理。
            cached = visual * cached;
            __0 = visual.MultiplyPoint3x4(__0);
            // EN: This fast path owns the transform; any nested public submission must not apply it again.
            // 中：此快速路径负责本次变换，嵌套的公共提交入口不得再次变换。
            FlightDrawScope.Current = null;
        }

        private static Exception Finalizer(object __instance, ref Vector3 __0,
            AdaptiveStorageFlightBridge.DrawState __state, Exception __exception)
        {
            if (__state.Applied)
            {
                // EN: Restore the entire cache, not only its position, including when ASF throws.
                // 中：即使 ASF 抛出异常，也恢复整个缓存，而不只是位置，避免跨帧累积旋转。
                AdaptiveStorageFlightBridge.CachedMatrix(__instance) = __state.Matrix;
                __0 = __state.Position;
                FlightDrawScope.Current = __state.Scope;
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class AdaptiveStorageScaledFlightDraw
    {
        private static MethodBase target;
        private static bool Prepare()
        {
            target = AdaptiveStorageFlightBridge.Resolve(false);
            return target != null;
        }
        private static MethodBase TargetMethod() { return target; }

        private static void Prefix(ref Vector3 __0, ref float __2,
            out AdaptiveStorageFlightBridge.DrawState __state)
        {
            __state = default(AdaptiveStorageFlightBridge.DrawState);
            CompFridgeFlight flight = FlightDrawScope.Current;
            if (flight == null || !flight.Flying) return;
            __state.Scope = flight;
            __state.Position = __0;
            __state.Rotation = __2;
            __state.Applied = true;
            Matrix4x4 visual = flight.VisualTransform;
            // EN: The scaled overload builds a local TRS matrix, so preserve ASF's scale and add only flight pose.
            // 中：缩放重载构造局部 TRS 矩阵；保留 ASF 缩放，仅附加飞行位置和朝向。
            __0 = visual.MultiplyPoint3x4(__0);
            __2 += visual.rotation.eulerAngles.y;
            FlightDrawScope.Current = null;
        }

        private static Exception Finalizer(ref Vector3 __0, ref float __2,
            AdaptiveStorageFlightBridge.DrawState __state, Exception __exception)
        {
            if (__state.Applied)
            {
                __0 = __state.Position;
                __2 = __state.Rotation;
                FlightDrawScope.Current = __state.Scope;
            }
            return __exception;
        }
    }
}
