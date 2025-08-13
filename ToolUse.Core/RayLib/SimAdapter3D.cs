using System;
using System.Numerics;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;
using ToolUse.Core.Config;

namespace ToolUse.Core.RaylibThreeD
{
    public class SimAdapter3D
    {
        private readonly World3D _world;
        private readonly Agent3D _seeker;
        private readonly Agent3D _hider;

        private Vector3 _oldSeekerPosition;
        private Vector3 _oldHiderPosition;

        // === Награды, считываемые из GameConfig ===
        private readonly float _proximityRewardMultiplierSeeker;
        private readonly float _rotationPenaltySeeker;
        private readonly float _noProgressPenaltySeeker;

        private readonly float _proximityRewardMultiplierHider;
        private readonly float _rotationPenaltyHider;
        private readonly float _noProgressPenaltyHider;

        // === Базовые награды из GameConfig ===
        private readonly float _rewardWhenHiderVisible;
        private readonly float _rewardWhenHiderHidden;
        private readonly float _rewardWhenVisible;
        private readonly float _rewardWhenHidden;

        // Шаг поворота из конфига
        private readonly float _rotStepSeeker;
        private readonly float _rotStepHider;

        public SimAdapter3D(World3D world, Agent3D seeker, Agent3D hider)
        {
            _world = world;
            _seeker = seeker;
            _hider = hider;
            // Adapter remains pure: no mutations of agent internals here.

            var cfg = GameConfig.Instance;

            // === Загружаем параметры из GameConfig ===
            _proximityRewardMultiplierSeeker = cfg.Seeker.ProximityRewardMultiplier;
            _rotationPenaltySeeker = cfg.Seeker.RotationPenalty;
            _noProgressPenaltySeeker = cfg.Seeker.NoProgressPenalty;

            _proximityRewardMultiplierHider = cfg.Hider.ProximityRewardMultiplier;
            _rotationPenaltyHider = cfg.Hider.RotationPenalty;
            _noProgressPenaltyHider = cfg.Hider.NoProgressPenalty;

            // === Загружаем базовые награды ===
            _rewardWhenHiderVisible = cfg.Seeker.RewardWhenHiderVisible;
            _rewardWhenHiderHidden = cfg.Seeker.RewardWhenHiderHidden;
            _rewardWhenVisible = cfg.Hider.RewardWhenVisible;
            _rewardWhenHidden = cfg.Hider.RewardWhenHidden;

            // Шаги поворота для ролей
            _rotStepSeeker = cfg.Seeker.RotationStepDegrees;
            _rotStepHider  = cfg.Hider.RotationStepDegrees;

            _oldSeekerPosition = _seeker.Position;
            _oldHiderPosition = _hider.Position;
        }

        public State GetSeekerState()
        {
            int sector = (int)MathF.Floor((((_seeker.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _seeker.TeamBoard != null
                ? _seeker.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _seeker.GetKnownWallsFlat(_world.Size);

            return new State(
                _seeker.GridX,
                _seeker.GridZ,
                _hider.GridX,
                _hider.GridZ,
                sector,
                IsVisible(),
                knownWalls,
                false // seeker не проверяет, видят ли его
            );
        }

        public State GetHiderState()
        {
            int sector = (int)MathF.Floor((((_hider.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _hider.TeamBoard != null
                ? _hider.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _hider.GetKnownWallsFlat(_world.Size);
            bool isSeenBySeeker = _hider.IsSeenBy(_seeker, _world);

            return new State(
                _hider.GridX,
                _hider.GridZ,
                _seeker.GridX,
                _seeker.GridZ,
                sector,
                _hider.CanSee(_seeker, _world),
                knownWalls,
                isSeenBySeeker
            );
        }

        public bool IsVisible()
        {
            return _seeker.CanSee(_hider, _world);
        }

        public float GetReward(Agent3D agent)
        {
            float reward = 0f;

            // === Выбираем параметры в зависимости от агента ===
            float proximityMultiplier, rotationPenalty, noProgressPenalty;

            if (agent == _seeker)
            {
                proximityMultiplier = _proximityRewardMultiplierSeeker;
                rotationPenalty = _rotationPenaltySeeker;
                noProgressPenalty = _noProgressPenaltySeeker;

                reward += IsVisible() ? _rewardWhenHiderVisible : _rewardWhenHiderHidden;
            }
            else if (agent == _hider)
            {
                // Для Hider поощряем увеличение дистанции: множитель должен быть отрицательным
                proximityMultiplier = _proximityRewardMultiplierHider > 0 ? -_proximityRewardMultiplierHider : _proximityRewardMultiplierHider;
                rotationPenalty = _rotationPenaltyHider;
                noProgressPenalty = _noProgressPenaltyHider;

                // При видимости — штраф, при скрытности — награда
                reward += _hider.IsSeenBy(_seeker, _world) ? -_rewardWhenVisible : _rewardWhenHidden;
            }
            else
            {
                return 0f; // Неизвестный агент
            }

            // Награда за сближение/удаление (положительно, если дистанция уменьшилась)
            float proximityDelta = CalculateProximityDelta();
            reward += proximityDelta * proximityMultiplier;

            // Штраф за отсутствие прогресса — используем penalty соответствующей роли
            if (Math.Abs(proximityDelta) < 0.1f)
            {
                reward -= noProgressPenalty;
            }

            return reward;
        }

        private float CalculateProximityDelta()
        {
            float oldDistance = Vector3.Distance(_oldSeekerPosition, _oldHiderPosition);
            float newDistance = Vector3.Distance(_seeker.Position, _hider.Position);
            return oldDistance - newDistance;
        }

        public void ApplyAction(Agent3D agent, long action)
        {
            _oldSeekerPosition = _seeker.Position;
            _oldHiderPosition = _hider.Position;

            float rot = agent.IsSeeker ? _rotStepSeeker : _rotStepHider;

            switch (action)
            {
                case 0:
                    agent.Rotate(-rot); // Влево
                    break;
                case 1:
                    agent.Rotate(+rot); // Вправо
                    break;
                case 2:
                    // Вперёд (движение происходит отдельно)
                    break;
            }
        }

        public void UpdatePositions()
        {
            _oldSeekerPosition = _seeker.Position;
            _oldHiderPosition = _hider.Position;
        }
    }
}
