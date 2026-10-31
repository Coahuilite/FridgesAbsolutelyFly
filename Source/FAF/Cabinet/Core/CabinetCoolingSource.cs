// SPDX-License-Identifier: MPL-2.0
namespace FAF.Cabinet.Core
{
    /// <summary>Resource decisions are domain states, not translation identifiers. / 资源决策使用领域状态，而非翻译标识。</summary>
    internal enum CabinetCoolingSource
    {
        Off,
        Idle,
        Electricity,
        Coolant,
        Unavailable,
        WaitingForGrid,
        HeatingUnavailable,
        Mixed
    }
}
