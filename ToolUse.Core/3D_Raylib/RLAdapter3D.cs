// ToolUse.Core/3D_Raylib/RLAdapter3D.cs
using ToolUse.Core.RL;

namespace ToolUse.Core.RaylibThreeD
{
    public class RLAdapter3D
    {
        private readonly Agent3D _seeker;
        private readonly Agent3D _hider;
        private readonly World3D _world;

        public RLAdapter3D(Agent3D seeker, Agent3D hider, World3D world)
        {
            _seeker = seeker;
            _hider = hider;
            _world = world;
        }

        public State GetSeekerState() => 
            new State(_seeker.X, _seeker.Y, _hider.X, _hider.Y, _seeker.CanSee(_hider, _world));

        public State GetHiderState() => 
            new State(_hider.X, _hider.Y, _seeker.X, _seeker.Y, _seeker.CanSee(_hider, _world));

        public void ApplyAction(Agent3D agent, int action)
        {
            switch (action)
            {
                case 0: agent.Rotate(-30f); break;  // Поворот влево
                case 1: agent.Rotate(+30f); break;  // Поворот вправо
                case 2: /* Движение вперед обрабатывается отдельно */ break;
            }
        }

        public bool IsVisible() => _seeker.CanSee(_hider, _world);
    }
}