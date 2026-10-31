// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FAF.Cabinet.Integration
{
    /// <summary>One native recursion authority while packed; inventory traversal outside DoTick is unchanged. / 打包时仍只有一个原生递归更新入口；DoTick之外的库存遍历不变。</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.DoTick))]
    internal static class CabinetHeldTickPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo children = AccessTools.Method(typeof(IThingHolder), nameof(IThingHolder.GetChildHolders));
            int matches = 0;
            foreach (CodeInstruction instruction in code) if (instruction.Calls(children)) matches++;
            if (matches != 1) throw new InvalidOperationException("[F.A.F.] Native held-item tick recursion changed; cabinet deduplication cannot be installed.");
            MethodInfo nativeTickChildren = AccessTools.Method(typeof(CabinetHeldTickPatch), nameof(GetTickChildren));
            foreach (CodeInstruction instruction in code)
                if (instruction.Calls(children))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = nativeTickChildren;
                }
            return code;
        }

        private static void GetTickChildren(IThingHolder holder, List<IThingHolder> children)
        {
            holder.GetChildHolders(children);
            MinifiedThing wrapper = holder as MinifiedThing;
            Building_FlyingFridgeCabinet cabinet = wrapper == null ? null : wrapper.InnerThing as Building_FlyingFridgeCabinet;
            // The wrapper's direct owner already calls cabinet.DoTick, including its contents and transport bay.
            // 包裹的直接容器已经调用cabinet.DoTick，包括柜内物及运输舱；不再从旁路重复进入柜内物。
            if (cabinet != null)
            {
                children.Remove(cabinet);
                // AppendThingHoldersFromThings also adds the inner thing's holder comps separately.
                // AppendThingHoldersFromThings还会独立加入内部物体的持有组件。
                for (int i = children.Count - 1; i >= 0; i--)
                    if (children[i] is ThingComp component && component.parent == cabinet) children.RemoveAt(i);
            }
            Building_FlyingFridgeCabinet contentsOwner = holder as Building_FlyingFridgeCabinet;
            if (contentsOwner == null) return;
            // Its direct items run their own native DoTick recursion (including pause/rare semantics).
            // 柜内直接物品已执行自身原生DoTick递归，包括暂停与Rare规则；不从子持有列表再进入一次。
            for (int i = children.Count - 1; i >= 0; i--)
            {
                Thing child = children[i] as Thing;
                ThingComp component = children[i] as ThingComp;
                if ((child != null && child.holdingOwner == contentsOwner.Contents)
                    || (component != null && component.parent.holdingOwner == contentsOwner.Contents))
                    children.RemoveAt(i);
            }
        }
    }
}
