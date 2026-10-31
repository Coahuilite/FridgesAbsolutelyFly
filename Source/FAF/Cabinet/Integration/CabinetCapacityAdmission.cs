// SPDX-License-Identifier: MPL-2.0
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>Keep native split/restore atomic without counting the same cabinet's moved stack twice. / 保留原生拆叠与恢复原子边界，同柜移交不重复计重。</summary>
    internal static class CabinetCapacityTransfer
    {
        internal sealed class Frame
        {
            internal ThingOwner Origin, Destination;
            internal Thing Original;
            internal Building_FlyingFridgeCabinet Cabinet;
            internal int Count;
            internal bool Internal;
        }
        [ThreadStatic] internal static Frame Current;

        internal static bool PreservesExisting(Building_FlyingFridgeCabinet cabinet, Thing item)
        {
            Frame frame = Current;
            return frame != null && frame.Cabinet == cabinet && item != null
                && item.def == frame.Original.def && item.Stuff == frame.Original.Stuff
                && item.stackCount <= frame.Count && (frame.Internal
                    || CabinetPayloadCapacity.CabinetFor(frame.Origin) == cabinet);
        }
        internal static bool Restoring(ThingOwner owner, Thing item)
        {
            return Current?.Origin == owner && PreservesExisting(CabinetPayloadCapacity.CabinetFor(owner), item);
        }
    }

    [HarmonyPatch]
    internal static class CabinetTransferCapacity
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.TryTransferToContainer), new[] {
                typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing).MakeByRefType(), typeof(bool) });
        }
        private static void Prefix(ThingOwner __instance, Thing item, ThingOwner otherContainer, ref int count,
            bool canMergeWithExistingStacks, out CabinetCapacityTransfer.Frame __state)
        {
            __state = CabinetCapacityTransfer.Current;
            CabinetCapacityTransfer.Current = null;
            Building_FlyingFridgeCabinet destination = CabinetPayloadCapacity.CabinetFor(otherContainer);
            Building_FlyingFridgeCabinet origin = CabinetPayloadCapacity.CabinetFor(__instance);
            if (item == null || destination == null && origin == null) return;
            bool rearranging = destination != null && CabinetPayloadCapacity.AlreadyCounted(destination, item);
            CabinetCapacityTransfer.Current = new CabinetCapacityTransfer.Frame {
                Origin = __instance, Destination = otherContainer, Original = item,
                Cabinet = rearranging ? destination : origin, Count = count, Internal = rearranging
            };
            if (destination != null && !CabinetPayloadCapacity.PreservingOriginal(destination, item))
                count = Math.Min(count, otherContainer.GetCountCanAccept(item, canMergeWithExistingStacks));
        }
        private static Exception Finalizer(Exception __exception, CabinetCapacityTransfer.Frame __state)
        {
            CabinetCapacityTransfer.Current = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ThingOwner<Thing>), nameof(ThingOwner<Thing>.TryAdd), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    internal static class CabinetCountedAddCapacity
    {
        private static void Prefix(ThingOwner __instance, Thing item, ref int count, bool canMergeWithExistingStacks)
        {
            Building_FlyingFridgeCabinet cabinet = CabinetPayloadCapacity.CabinetFor(__instance);
            if (cabinet != null && !CabinetPayloadCapacity.PreservingOriginal(cabinet, item)
                && !CabinetCapacityTransfer.Restoring(__instance, item))
                count = Math.Min(count, __instance.GetCountCanAccept(item, canMergeWithExistingStacks));
        }
    }

    [HarmonyPatch(typeof(ThingOwner<Thing>), nameof(ThingOwner<Thing>.TryAdd), new[] { typeof(Thing), typeof(bool) })]
    internal static class CabinetWholeStackCapacity
    {
        private static bool Prefix(ThingOwner __instance, Thing item, bool canMergeWithExistingStacks, ref bool __result)
        {
            Building_FlyingFridgeCabinet cabinet = CabinetPayloadCapacity.CabinetFor(__instance);
            if (cabinet == null || item == null || CabinetPayloadCapacity.PreservingOriginal(cabinet, item)
                || CabinetCapacityTransfer.Restoring(__instance, item)) return true;
            // Native bool TryAdd checks only "any" acceptance; an unsplittable caller must fit the whole stack.
            // 原生bool TryAdd只检查“能否接纳任意数量”；不拆叠调用必须整叠放得下，否则原物留给调用者。
            if (__instance.GetCountCanAccept(item, canMergeWithExistingStacks) >= item.stackCount) return true;
            __result = false;
            return false;
        }
    }
}
