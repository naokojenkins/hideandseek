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

        public SimAdapter3D(World3D world, Agent3D seeker, Agent3D hider)
        {
            _world = world;
            _seeker = seeker;
            _hider = hider;
        }

        public State GetSeekerState()
        {
            int sector = (int)MathF.Floor((((_seeker.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _seeker.TeamBoard != null
                ? _seeker.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _seeker.GetKnownWallsFlat(_world.Size);

            bool visible = _seeker.CanSee(_hider, _world);

            return new State(
                _seeker.GridX,
                _seeker.GridZ,
                _hider.GridX,
                _hider.GridZ,
                sector,
                visible,
                knownWalls,
                _seeker.IsSeenBy(_hider, _world)
            );
        }

        public State GetHiderState()
        {
            int sector = (int)MathF.Floor((((_hider.Direction % 360f) + 360f) % 360f + 22.5f) / 45f) % 8;
            bool[] knownWalls = _hider.TeamBoard != null
                ? _hider.TeamBoard.GetKnownWallsFlat(_world.Size)
                : _hider.GetKnownWallsFlat(_world.Size);

            bool isSeenBySeeker = _hider.IsSeenBy(_seeker, _world);
            bool seesSeeker = _hider.CanSee(_seeker, _world);

            return new State(
                _hider.GridX,
                _hider.GridZ,
                _seeker.GridX,
                _seeker.GridZ,
                sector,
                seesSeeker,
                knownWalls,
                isSeenBySeeker
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
