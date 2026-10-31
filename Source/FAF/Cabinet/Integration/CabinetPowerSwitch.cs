// SPDX-License-Identifier: MPL-2.0
using HarmonyLib;
using RimWorld;

namespace FAF.Cabinet.Integration
{
    /// <summary>One native actual/wanted switch pair, including when no map exists for a flick job. / 只使用原生实际与期望开关状态，也适用于没有地图拨动任务的便携场景。</summary>
    internal static class CabinetPowerSwitch
    {
        private static readonly AccessTools.FieldRef<CompFlickable, bool> wanted =
            AccessTools.FieldRefAccess<CompFlickable, bool>("wantSwitchOn");
        internal static bool Requested(CompFlickable component) { return wanted(component); }
        internal static void Request(Building_FlyingFridgeCabinet cabinet, bool requestedOn)
        {
            CompFlickable component = cabinet.GetComp<CompFlickable>();
            wanted(component) = requestedOn;
            if (cabinet.Spawned) FlickUtility.UpdateFlickDesignation(cabinet);
            else component.SwitchIsOn = requestedOn;
        }
        internal static void ApplyPortableRequest(Building_FlyingFridgeCabinet cabinet)
        {
            CompFlickable component = cabinet.GetComp<CompFlickable>();
            // Public setter sends native FlickedOn/Off signals without calling the map-only DoFlick sound path.
            // 公开setter发送原生FlickedOn/Off信号，不调用仅适用于地图的DoFlick音效路径。
            component.SwitchIsOn = wanted(component);
        }
    }
}
