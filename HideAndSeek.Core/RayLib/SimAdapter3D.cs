using System;
using System.Numerics;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RL;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Тонкий адаптер: только преобразование текущей сцены в состояния для ролей
    // + применение действий на основе конфигурации.
    public class SimAdapter3D
    {
        private readonly World3D _world;
        private readonly Agent3D _seeker;
        private readonly Agent3D _hider;
        private readonly System.Collections.Generic.IReadOnlyList<Agent3D>? _seekers;

        public SimAdapter3D(World3D world, Agent3D seeker, Agent3D hider)
        {
            _world = world;
            _seeker = seeker;
            _hider = hider;
        }

        public SimAdapter3D(World3D world, System.Collections.Generic.IReadOnlyList<Agent3D> seekers, Agent3D hider)
        {
            _world = world;
            _seekers = seekers;
            _seeker = seekers.Count > 0 ? seekers[0] : throw new ArgumentException("seekers must contain at least one element");
            _hider = hider;
        }

        public State GetSeekerState()
        {
            int sector = (int)MathF.Floor((((_seeker.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _seeker.TeamBoard != null
                ? _seeker.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _seeker.GetKnownWallsFlat(_world.Size);

            bool visible = _seeker.CanSee(_hider, _world);

            // Признаки памяти искателя о противнике
            bool hasOppMem = false;
            int relX = 0, relZ = 0;
            float conf = 0f;
            var memCfg = GameConfig.Instance.Memory;
            if (_seeker.Memory.TryGetLastOpponent(out var opp) && opp.Confidence >= memCfg.MinConfidenceForNav)
            {
                var last = opp.LastPosition;
                int lastGX = Agent3D.ToGridX(last.X, _world.Size);
                int lastGZ = Agent3D.ToGridZ(last.Z, _world.Size);
                relX = lastGX - _seeker.GridX;
                relZ = lastGZ - _seeker.GridZ;
                hasOppMem = true;
                conf = opp.Confidence;
            }

            // Отдельный признак: меня видят (любой Hider видит этого Seeker'а)
            bool isSeenByOpponent;
            if (_seekers != null && _seekers.Count > 0)
            {
                // В мульти-режиме у этого адаптера один hider; проверяем видимость hider -> каждый seeker
                // Для состояния конкретного seeker достаточно проверить, видит ли его текущий _hider
                isSeenByOpponent = _hider.CanSee(_seeker, _world);
            }
            else
            {
                isSeenByOpponent = _hider.CanSee(_seeker, _world);
            }

            return new State(
                _seeker.GridX,
                _seeker.GridZ,
                _hider.GridX,
                _hider.GridZ,
                sector,
                visible,
                knownWalls,
                // Не смешиваем «вижу» и «меня видят»
                isSeenByOpponent,
                // Расширение: признаки памяти
                hasOppMem,
                relX,
                relZ,
                conf
            );
        }

        public State GetHiderState()
        {
            int sector = (int)MathF.Floor((((_hider.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _hider.TeamBoard != null
                ? _hider.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _hider.GetKnownWallsFlat(_world.Size);

            bool isSeenBySeeker;
            bool seesSeeker;

            if (_seekers != null && _seekers.Count > 0)
            {
                isSeenBySeeker = System.Linq.Enumerable.Any(_seekers, s => _hider.IsSeenBy(s, _world));
                seesSeeker = System.Linq.Enumerable.Any(_seekers, s => _hider.CanSee(s, _world));
            }
            else
            {
                isSeenBySeeker = _hider.IsSeenBy(_seeker, _world);
                seesSeeker = _hider.CanSee(_seeker, _world);
            }

            // Признаки памяти прячущегося о противнике (искателе)
            bool hasOppMem = false;
            int relX = 0, relZ = 0;
            float conf = 0f;
            var memCfg = GameConfig.Instance.Memory;
            if (_hider.Memory.TryGetLastOpponent(out var opp) && opp.Confidence >= memCfg.MinConfidenceForNav)
            {
                var last = opp.LastPosition;
                int lastGX = Agent3D.ToGridX(last.X, _world.Size);
                int lastGZ = Agent3D.ToGridZ(last.Z, _world.Size);
                relX = lastGX - _hider.GridX;
                relZ = lastGZ - _hider.GridZ;
                hasOppMem = true;
                conf = opp.Confidence;
            }

            return new State(
                _hider.GridX,
                _hider.GridZ,
                _seeker.GridX,
                _seeker.GridZ,
                sector,
                seesSeeker,
                knownWalls,
                isSeenBySeeker,
                // Расширение: признаки памяти
                hasOppMem,
                relX,
                relZ,
                conf
            );
        }

        /// <summary>
        /// Применяет поворот согласно действию и сообщает, требуется ли поступательное движение в этом кадре.
        /// Индексы действий берутся из GameConfig.Instance.Actions.
        /// Возвращает true, если нужно выполнить движение вперед (Forward/ForwardLeft/ForwardRight); сам сдвиг выполняется вызывающим кодом.
        /// </summary>
        public bool ApplyAction(Agent3D agent, long action, float rotationStepDegrees)
        {
            var a = GameConfig.Instance.Actions;
            if (action == a.TurnLeft)
            {
                agent.Rotate(-rotationStepDegrees);
                return false;
            }
            if (action == a.TurnRight)
            {
                agent.Rotate(+rotationStepDegrees);
                return false;
            }
            if (action == a.ForwardLeft)
            {
                agent.Rotate(-rotationStepDegrees);
                return true;
            }
            if (action == a.ForwardRight)
            {
                agent.Rotate(+rotationStepDegrees);
                return true;
            }
            if (action == a.Forward)
            {
                return true;
            }
            // Неизвестное действие: ничего не делать
            return false;
        }
    }
}
