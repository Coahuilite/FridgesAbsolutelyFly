// SPDX-License-Identifier: MPL-2.0
using FAF.Transport;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FAF.Cabinet
{
    /// <summary>Off-map meals use actual food and native ingestion, not a second hunger simulation. / 离图用餐使用实体食品和原生进食，不另建饥饿模拟。</summary>
    internal static class CabinetResidentNeeds
    {
        internal static void FeedHeldOccupants(Building_FlyingFridgeCabinet cabinet, CabinetOccupancySnapshot occupancy)
        {
            if (!cabinet.IsHashIntervalTick(150)) return;
            foreach (Pawn pawn in occupancy.ActualOccupants)
            {
                // Spawned proxy sleepers retain the upstream snack policy and its user setting.
                // 已生成的proxy睡者保留前置偷吃策略及其用户设置。
                if (pawn.Spawned || pawn.Dead || pawn.Destroyed || pawn.needs?.food == null
                    || pawn.needs.food.CurCategory == HungerCategory.Fed) continue;
                Thing food = BestFood(pawn.inventory?.innerContainer, pawn)
                    ?? BestFood(cabinet.Contents, pawn)
                    ?? BestFood(cabinet.GetComp<CompCabinetTransport>().innerContainer, pawn)
                    ?? BestFood(occupancy.Payload?.innerContainer, pawn);
                if (food == null) continue;
                ThingOwner owner = food.holdingOwner;
                int before = food.stackCount;
                float nutrition = food.Ingested(pawn, pawn.needs.food.NutritionWanted);
                pawn.needs.food.CurLevel += nutrition;
                if (food.Destroyed || food.stackCount != before)
                {
                    cabinet.NotifyContentsMassChanged();
                    cabinet.GetComp<CompCabinetTransport>().InvalidateCargoMass();
                    if (owner?.Owner is Pawn_InventoryTracker inventory) inventory.pawn.GetCaravan()?.RecacheInventory();
                }
            }
        }

        private static Thing BestFood(ThingOwner owner, Pawn pawn)
        {
            if (owner == null) return null;
            Thing best = null;
            float bestScore = float.MinValue;
            foreach (Thing item in owner)
            {
                // The Thing overload additionally checks ingredients/venerated meat and the real food policy.
                // Thing重载额外检查配料/受崇敬动物肉与实际饮食政策。
                if (item.Destroyed || !CaravanPawnsNeedsUtility.CanEatForNutritionNow(item, pawn)
                    || !pawn.WillEat(item) || FoodUtility.NutritionForEater(pawn, item) <= 0f) continue;
                float score = CaravanPawnsNeedsUtility.GetFoodScore(item, pawn);
                if (score > bestScore) { best = item; bestScore = score; }
            }
            return best;
        }
    }
}
