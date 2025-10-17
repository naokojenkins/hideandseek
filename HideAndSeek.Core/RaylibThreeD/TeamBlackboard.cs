using System.Collections.Generic;
using System.Numerics;

namespace HideAndSeek.Core.RaylibThreeD
{
    public class TeamBlackboard
    {
        public HashSet<(int x, int z)> KnownWalls { get; } = new();

        // Последние известные позиции целей, замеченных командой
        private readonly Dictionary<Agent3D, (Vector3 pos, float time)> _lastKnownTargets = new();

        public void ReportSeenTarget(Agent3D target, Vector3 position, float time)
        {
            _lastKnownTargets[target] = (position, time);
        }

        public (bool has, Vector3 pos, float time) GetLastKnown(Agent3D target)
        {
            if (_lastKnownTargets.TryGetValue(target, out var v)) return (true, v.pos, v.time);
            return (false, default, 0f);
        }

        public void Clear()
        {
            KnownWalls.Clear();
            _lastKnownTargets.Clear();
        }

        public void UnionWallsWith(IEnumerable<(int x, int z)> walls)
        {
            KnownWalls.UnionWith(walls);
        }

        public bool[] GetKnownWallsFlat(int worldSize)
        {
            var arr = new bool[worldSize * worldSize];
            foreach (var (x, z) in KnownWalls)
            {
                if (x >= 0 && x < worldSize && z >= 0 && z < worldSize)
                    arr[x + z * worldSize] = true;
            }
            return arr;
        }
    }
}
