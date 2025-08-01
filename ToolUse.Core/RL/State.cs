using System;
using System.Linq;

namespace ToolUse.Core.RL
{
    public class State
    {
        public int AgentX { get; }
        public int AgentY { get; }
        public int OtherX { get; }
        public int OtherY { get; }
        public int Direction { get; }
        public bool CanSee { get; }

        // Карта известных стен, сериализованная в плоский массив
        public bool[] KnownWallsFlat { get; }

        // Новый конструктор с KnownWalls
        public State(int ax, int ay, int ox, int oy, int direction, bool see, bool[] knownWalls)
        {
            AgentX = Math.Max(0, ax);
            AgentY = Math.Max(0, ay);
            OtherX = Math.Max(0, ox);
            OtherY = Math.Max(0, oy);
            Direction = direction;
            CanSee = see;
            KnownWallsFlat = knownWalls ?? Array.Empty<bool>();
        }

        // Старый конструктор для обратной совместимости
        public State(int ax, int ay, int ox, int oy, int direction, bool see)
            : this(ax, ay, ox, oy, direction, see, Array.Empty<bool>())
        { }

        public override string ToString()
        {
            var basic = $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},dir={Direction},see={CanSee}";
            if (KnownWallsFlat != null && KnownWallsFlat.Length > 0)
                return basic + $",walls={string.Join("", KnownWallsFlat.Select(x => x ? "1" : "0"))}";
            return basic;
        }

        public static State FromString(string s)
        {
            try
            {
                var p = s.Split(',');
                int ax = int.Parse(p[0][3..]);
                int ay = int.Parse(p[1][3..]);
                int ox = int.Parse(p[2][3..]);
                int oy = int.Parse(p[3][3..]);
                int dir = int.Parse(p[4][4..]);
                bool v = bool.Parse(p[5][4..]);
                // KnownWalls не десериализуется для простоты
                return new State(ax, ay, ox, oy, dir, v);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Для нейросети: преобразование состояния в массив признаков float[]
        /// </summary>
        public float[] ToArray()
        {
            var basic = new float[]
            {
                AgentX,
                AgentY,
                OtherX,
                OtherY,
                Direction / 360f,
                CanSee ? 1f : 0f
            };
            if (KnownWallsFlat != null && KnownWallsFlat.Length > 0)
            {
                var arr = new float[basic.Length + KnownWallsFlat.Length];
                basic.CopyTo(arr, 0);
                for (int i = 0; i < KnownWallsFlat.Length; i++)
                    arr[basic.Length + i] = KnownWallsFlat[i] ? 1f : 0f;
                return arr;
            }
            return basic;
        }

        public override bool Equals(object? obj) => obj is State other && Equals(other);
        private bool Equals(State other) =>
            AgentX == other.AgentX &&
            AgentY == other.AgentY &&
            OtherX == other.OtherX &&
            OtherY == other.OtherY &&
            Direction == other.Direction &&
            CanSee == other.CanSee &&
            ((KnownWallsFlat == null && other.KnownWallsFlat == null) ||
             (KnownWallsFlat != null && other.KnownWallsFlat != null && KnownWallsFlat.SequenceEqual(other.KnownWallsFlat)));

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + AgentX.GetHashCode();
                hash = hash * 23 + AgentY.GetHashCode();
                hash = hash * 23 + OtherX.GetHashCode();
                hash = hash * 23 + OtherY.GetHashCode();
                hash = hash * 23 + Direction.GetHashCode();
                hash = hash * 23 + CanSee.GetHashCode();
                if (KnownWallsFlat != null)
                {
                    foreach (bool b in KnownWallsFlat)
                        hash = hash * 23 + (b ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
