using System.Collections.Generic;

namespace HideAndSeek.Core.RaylibThreeD
{
    // API для назначения коллекций агентов
    public partial class Simulation3D
    {
        // Позволяет задать списки агентов после создания симуляции
        public void SetAgents(List<Agent3D> seekers, List<Agent3D> hiders)
        {
            Seekers = seekers ?? new List<Agent3D>();
            Hiders = hiders ?? new List<Agent3D>();

            if (Seekers.Count > 0) Seeker = Seekers[0];
            if (Hiders.Count > 0) Hider = Hiders[0];

            // Assign unique ids deterministically per team
            for (int i = 0; i < Seekers.Count; i++) Seekers[i].Id = $"S{i+1}";
            for (int i = 0; i < Hiders.Count; i++) Hiders[i].Id = $"H{i+1}";

            foreach (var s in Seekers) { s.InitWorldSize(World.Size); s.SetWorld(World); }
            foreach (var h in Hiders) { h.InitWorldSize(World.Size); h.SetWorld(World); }

            foreach (var s in Seekers) s.TeamBoard = _seekersBoard;
            foreach (var h in Hiders) h.TeamBoard = _hidersBoard;

            // Валидируем позиции всех агентов относительно мира симуляции
            foreach (var s in Seekers) EnsureAgentOnValidCell(s);
            foreach (var h in Hiders) EnsureAgentOnValidCell(h);
        }
    }
}
