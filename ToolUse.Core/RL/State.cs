using System;
using System.ComponentModel;
using System.Globalization;

namespace ToolUse.Core.RL
{
    [TypeConverter(typeof(StateConverter))]
    public class State
    {
        public int AgentX { get; init; }
        public int AgentY { get; init; }
        public int OtherX { get; init; }
        public int OtherY { get; init; }
        public bool CanSee { get; init; }

        public State(int ax, int ay, int ox, int oy, bool canSee)
        {
            AgentX = ax;
            AgentY = ay;
            OtherX = ox;
            OtherY = oy;
            CanSee = canSee;
        }

        public override bool Equals(object? o) =>
            o is State s &&
            AgentX == s.AgentX &&
            AgentY == s.AgentY &&
            OtherX == s.OtherX &&
            OtherY == s.OtherY &&
            CanSee == s.CanSee;

        public override int GetHashCode() => HashCode.Combine(AgentX, AgentY, OtherX, OtherY, CanSee);

        /* ── сериализация ключа ── */
        public override string ToString() =>
            $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},see={CanSee}";

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
                throw new FormatException($"Не удалось разобрать строку как State: '{s}'", ex);
            }
        }
    }
}