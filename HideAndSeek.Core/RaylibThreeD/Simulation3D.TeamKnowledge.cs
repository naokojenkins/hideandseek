using System.Collections.Generic;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Объединение знаний команды о стенах/карте
    public partial class Simulation3D
    {
        // Объединяет известные стены по командам и распространяет union обратно всем агентам и в командный blackboard
        private void MergeTeamKnowledge()
        {
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            // Seekers
            var unionS = new HashSet<(int x, int z)>(_seekersBoard.KnownWalls);
            foreach (var s in seekers) unionS.UnionWith(s.KnownWalls);
            _seekersBoard.KnownWalls.UnionWith(unionS);
            foreach (var s in seekers) s.KnownWalls.UnionWith(_seekersBoard.KnownWalls);

            // Hiders
            var unionH = new HashSet<(int x, int z)>(_hidersBoard.KnownWalls);
            foreach (var h in hiders) unionH.UnionWith(h.KnownWalls);
            _hidersBoard.KnownWalls.UnionWith(unionH);
            foreach (var h in hiders) h.KnownWalls.UnionWith(_hidersBoard.KnownWalls);
        }
    }
}
