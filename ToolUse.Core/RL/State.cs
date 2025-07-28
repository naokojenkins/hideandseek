using System;

namespace ToolUse.Core.RL
{
    public class State
    {
        public int AgentX { get; }
        public int AgentY { get; }
        public int OtherX { get; }
        public int OtherY { get; }
        public bool CanSee { get; }

        public State(int ax, int ay, int ox, int oy, bool see)
        {
            AgentX = Math.Max(0, ax);
            AgentY = Math.Max(0, ay);
            OtherX = Math.Max(0, ox);
            OtherY = Math.Max(0, oy);
            CanSee = see;
        }

        public override string ToString()
        {
            string result = $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},see={CanSee}";
            return result;
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
                bool v = bool.Parse(p[4][4..]);

                return new State(ax, ay, ox, oy, v);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public override bool Equals(object? obj) => obj is State other && Equals(other);

        private bool Equals(State other) =>
            AgentX == other.AgentX &&
            AgentY == other.AgentY &&
            OtherX == other.OtherX &&
            OtherY == other.OtherY &&
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
                hash = hash * 23 + CanSee.GetHashCode();
                return hash;
            }
        }
    }
}
