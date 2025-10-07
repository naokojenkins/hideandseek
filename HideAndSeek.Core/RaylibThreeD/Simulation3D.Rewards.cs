using System;
using System.Numerics;
using System.Collections.Generic;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Логика вычисления наград вынесена сюда.
    public partial class Simulation3D
    {
        private float ComputeSeekerRewardFor(Agent3D s, int newPhysical, int newVisual, bool seesAny, bool isSeenByOpponent)
        {
            // Unified exploration: visual or physical discovery both count as exploration with the same unit reward.
            // Remove separate visual/physical rewards and any additional shaping tied to exploration variety.
            float r = 0f;

            int newlyExploredTotal = newPhysical + newVisual;
            if (newlyExploredTotal > 0)
            {
                // Единый пер‑клеточный бонус за исследование: применяется как к «визуальному» открытию клетки,
                // так и к физическому достижению/проходу. Историческое имя поля в конфиге — PhysicalExploreReward.
                // Это фактически ExploreRewardPerCell: каждая новая клетка даёт одинаковую награду.
                float perCell = Config.Seeker.PhysicalExploreReward;
                float bonus = newlyExploredTotal * perCell;
                r += bonus;
                ExplorationScore += bonus;
            }

            // Видимостьные награды для RL — по явному флагу из конфига, чтобы не путать с HUD-очками
            if (Config.Seeker.ApplyVisibilityRewardsToRL)
            {
                float vis = seesAny ? Config.Seeker.RewardWhenHiderVisible : Config.Seeker.RewardWhenHiderHidden;
                r += vis * Config.Seeker.VisibilityRewardScaleRL;
            }

            // Keep distance-based shaping as is to not affect chasing behavior.
            var hidersList = (Hiders != null && Hiders.Count > 0) ? Hiders : new List<Agent3D> { Hider };
            var nearestH = GetNearestOpponent(s, hidersList);
            float curDist = Vector3.Distance(s.Position, nearestH.Position);
            float lastDist = _lastDistToNearestHider.TryGetValue(s, out var prevDist) ? prevDist : curDist;

            if (isSeenByOpponent)
            {
                // Штраф за то, что меня видят, и поощрение за увеличение дистанции от ближайшего Hider
                r += Config.Seeker.SeenByOpponentPenaltyPerStep; // обычно отрицательный
                float distDeltaAway = curDist - lastDist; // >0 если удалился
                float fleeMul = Config.Seeker.FleeDistanceRewardMultiplierWhenSeen;
                r += distDeltaAway * fleeMul;
            }
            else if (Config.Seeker.UsePotentialShaping)
            {
                float shaping = lastDist - MathF.Max(0f, Config.Model.Gamma) * curDist;
                r += shaping;
            }
            else
            {
                float distDeltaToward = lastDist - curDist; // >0 if moved closer
                r += distDeltaToward * MathF.Max(0f, Config.Seeker.ProximityRewardMultiplier);
            }

            if (float.IsNaN(r) || float.IsInfinity(r))
                throw new Exception($"[NaN/Inf] ComputeSeekerRewardFor: {r}");
            return r;
        }

        private float ComputeHiderRewardFor(Agent3D h, List<Agent3D> seekers, bool visibleNow)
        {
            float reward = 0f;
            // Базовая видимость: по явному флагу (иначе вклад 0, чтобы избежать конфликта с HUD-очками)
            if (Config.Hider.ApplyVisibilityRewardsToRL)
            {
                if (visibleNow)
                {
                    float seen = Config.Hider.RewardWhenSeenBySeeker;
                    float baseVis = (seen != 0f) ? seen : Config.Hider.RewardWhenVisible;
                    reward += baseVis * Config.Hider.VisibilityRewardScaleRL;
                }
                else
                {
                    reward += Config.Hider.RewardWhenHidden * Config.Hider.VisibilityRewardScaleRL;
                }
            }

            // расстояние до ближайшего seeker
            var nearest = GetNearestOpponent(h, seekers);
            float currentDistance = Vector3.Distance(nearest.Position, h.Position);
            float lastDist = _lastDistToNearestSeeker.TryGetValue(h, out var prev) ? prev : currentDistance;

            // Вклад за изменение дистанции (положительный при удалении)
            float distDeltaAway = currentDistance - lastDist; // >0 если удалился
            reward += distDeltaAway * MathF.Max(0f, Config.Hider.ProximityRewardMultiplier);

            if (currentDistance > lastDist) reward += Config.Hider.RewardWhenIncreasingDistance;
            else if (currentDistance <= lastDist + _noProgressDistanceEps) reward -= MathF.Max(0f, Config.Hider.NoProgressPenalty);
            _lastDistToNearestSeeker[h] = currentDistance;

            // Бонус за «скрыт за стеной»: проверяем окклюзию с точки зрения наблюдателя (seeker), а не хайдера
            if (!visibleNow && IsOccludedByWall(nearest, h))
                reward += Config.Hider.RewardWhenHiddenBehindWall;

            if (_wasHiderVisiblePrevMap.TryGetValue(h, out var wasVisible) && wasVisible && !visibleNow)
                reward += Config.Hider.EscapeBonus;

            if (float.IsNaN(reward) || float.IsInfinity(reward))
                throw new Exception($"[NaN/Inf] ComputeHiderRewardFor: {reward}");
            return reward;
        }
    }
}
