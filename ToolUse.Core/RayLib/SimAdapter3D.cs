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

        public SimAdapter3D(World3D world, Agent3D seeker, Agent3D hider)
        {
            _world = world;
            _seeker = seeker;
            _hider = hider;
            _hider._seeker = _seeker;
            _hider._world = _world;

            var cfg = GameConfig.Load();

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

            _oldSeekerPosition = _seeker.Position;
            _oldHiderPosition = _hider.Position;
        }

        public State GetSeekerState()
        {
            int sector = (int)(MathF.Round(_seeker.Direction / 45f) % 8);
            bool[] knownWalls = _seeker.GetKnownWallsFlat(_world.Size);

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
            int sector = (int)(MathF.Round(_hider.Direction / 45f) % 8);
            bool[] knownWalls = _hider.GetKnownWallsFlat(_world.Size);
            bool isSeenBySeeker = _hider.IsSeenBy(_seeker, _world);

            return new State(
                _hider.GridX,
                _hider.GridZ,
                _seeker.GridX,
                _seeker.GridZ,
                sector,
                IsVisible(),
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
                proximityMultiplier = _proximityRewardMultiplierHider;
                rotationPenalty = _rotationPenaltyHider;
                noProgressPenalty = _noProgressPenaltyHider;

                reward += _hider.IsSeenBy(_seeker, _world) ? _rewardWhenVisible : _rewardWhenHidden;
            }
            else
            {
                return 0f; // Неизвестный агент
            }

            // 2. Награда за сближение
            float proximityReward = CalculateProximityReward() * proximityMultiplier;
            reward += proximityReward;

            return reward;
        }

        private float CalculateProximityReward()
        {
            float oldDistance = Vector3.Distance(_oldSeekerPosition, _oldHiderPosition);
            float newDistance = Vector3.Distance(_seeker.Position, _hider.Position);
            float proximityReward = oldDistance - newDistance;

            // 3. Штраф за отсутствие прогресса
            if (Math.Abs(newDistance - oldDistance) < 0.1f)
            {
                proximityReward -= _noProgressPenaltySeeker;
            }

            return proximityReward;
        }

        public void ApplyAction(Agent3D agent, long action)
        {
            _oldSeekerPosition = _seeker.Position;
            _oldHiderPosition = _hider.Position;

            switch (action)
            {
                case 0:
                    agent.Rotate(-10f); // Влево
                    break;
                case 1:
                    agent.Rotate(+10f); // Вправо
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