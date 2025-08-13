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
        public bool IsSeenBySeeker { get; } // ✅ Новое поле

        // Карта известных стен, сериализованная в плоский массив
        public bool[] KnownWallsFlat { get; }

        // Основной конструктор
        public State(int ax, int ay, int ox, int oy, int direction, bool see, bool[] knownWalls, bool isSeenBySeeker = false)
        {
            AgentX = Math.Max(0, ax);
            AgentY = Math.Max(0, ay);
            OtherX = Math.Max(0, ox);
            OtherY = Math.Max(0, oy);
            Direction = direction;
            CanSee = see;
            IsSeenBySeeker = isSeenBySeeker;
            // Защитное копирование, чтобы состояние не зависело от внешних мутаций knownWalls
            KnownWallsFlat = knownWalls != null ? knownWalls.ToArray() : Array.Empty<bool>();
        }

        // Старый конструктор для обратной совместимости
        public State(int ax, int ay, int ox, int oy, int direction, bool see, bool[] knownWalls)
            : this(ax, ay, ox, oy, direction, see, knownWalls, false)
        { }

        // Ещё один конструктор без KnownWalls для старых версий
        public State(int ax, int ay, int ox, int oy, int direction, bool see)
            : this(ax, ay, ox, oy, direction, see, Array.Empty<bool>(), false)
        { }

        public override string ToString()
        {
            var basic = $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},dir={Direction},see={CanSee},seen={IsSeenBySeeker}";
            if (KnownWallsFlat != null && KnownWallsFlat.Length > 0)
                return basic + $",walls={string.Join("", KnownWallsFlat.Select(x => x ? "1" : "0"))}";
            return basic;
        }

        public static State FromString(string s)
        {
            try
            {
                var p = s.Split(',');
                if (p.Length < 6) throw new FormatException("State string has insufficient parts");

                int ax = int.Parse(p[0][3..]);
                int ay = int.Parse(p[1][3..]);
                int ox = int.Parse(p[2][3..]);
                int oy = int.Parse(p[3][3..]);
                int dir = int.Parse(p[4][4..]);
                bool see = bool.Parse(p[5][4..]);

                bool seen = false;
                int wallStart = 6;

                // Проверяем, есть ли seen=...
                if (p.Length > 6 && p[6].StartsWith("seen="))
                {
                    seen = bool.Parse(p[6][5..]);
                    wallStart = 7;
                }

                bool[]? walls = null;
                if (p.Length > wallStart && p[wallStart].StartsWith("walls="))
                {
                    string wallStr = p[wallStart]["walls=".Length..];
                    walls = wallStr.Select(c => c == '1').ToArray();
                }

                return new State(ax, ay, ox, oy, dir, see, walls ?? Array.Empty<bool>(), seen);
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Для нейросети: преобразование состояния в массив признаков float[]
        /// </summary>
        public float[] ToArray(int worldSize)
        {
            float[] basic = new[]
            {
                AgentX / (float)worldSize,
                AgentY / (float)worldSize,
                OtherX / (float)worldSize,
                OtherY / (float)worldSize,
                Direction / 8.0f, // Нормализованный сектор (0–7 → 0–1)
                CanSee ? 1f : 0f,
                IsSeenBySeeker ? 1f : 0f // ✅ Новое поле
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
            IsSeenBySeeker == other.IsSeenBySeeker &&
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
                hash = hash * 23 + IsSeenBySeeker.GetHashCode();
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