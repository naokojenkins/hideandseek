using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;

namespace ToolUse.Core.RayLib
{
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
            int sector = (int)(MathF.Round(_seeker.Direction / 45f) % 8);
            return new State(
                _seeker.GridX,
                _seeker.GridZ,
                _hider.GridX,
                _hider.GridZ,
                sector,
                IsVisible()
            );
        }

        public State GetHiderState()
        {
            int sector = (int)(MathF.Round(_hider.Direction / 45f) % 8);
            return new State(
                _hider.GridX,
                _hider.GridZ,
                _seeker.GridX,
                _seeker.GridZ,
                sector,
                IsVisible()
            );
        }

        public bool IsVisible()
        {
            return _seeker.CanSee(_hider, _world);
        }

        public void ApplyAction(Agent3D agent, int action)
        {
            switch (action)
            {
                case 0:
                    agent.Rotate(-30f);
                    break;
                case 1:
                    agent.Rotate(+30f);
                    break;
                case 2:
                    // Вперёд (движение происходит отдельно)
                    break;
            }
        }
    }
}