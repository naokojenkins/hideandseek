using System;

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

        public State(int ax, int ay, int ox, int oy, int direction, bool see)
        {
            AgentX = Math.Max(0, ax);
            AgentY = Math.Max(0, ay);
            OtherX = Math.Max(0, ox);
            OtherY = Math.Max(0, oy);
            Direction = direction;
            CanSee = see;
        }

        public override string ToString()
            => $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},dir={Direction},see={CanSee}";

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
                return new State(ax, ay, ox, oy, dir, v);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Для нейросети: преобразование состояния в массив фичей float[]
        /// </summary>
        public float[] ToArray()
        {
            return new float[]
            {
                AgentX,
                AgentY,
                OtherX,
                OtherY,
                Direction / 360f,  // Нормируем угол до [0,1]
                CanSee ? 1f : 0f
            };
        }

        public override bool Equals(object? obj) => obj is State other && Equals(other);
        private bool Equals(State other) =>
            AgentX == other.AgentX &&
            AgentY == other.AgentY &&
            OtherX == other.OtherX &&
            OtherY == other.OtherY &&
            Direction == other.Direction &&
            CanSee == other.CanSee;

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
                return hash;
            }
        }
    }
}
