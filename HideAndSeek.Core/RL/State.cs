using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HideAndSeek.Core.RL
{
    /// <summary>
    /// Represents the full RL state used by feature extractors and serializers.
    /// Field order and normalization for ToArray(int worldSize):
    /// 0: AgentX / worldSize (float, [0..1])
    /// 1: AgentY / worldSize (float, [0..1])
    /// 2: OtherX / worldSize (float, [0..1])
    /// 3: OtherY / worldSize (float, [0..1])
    /// 4: Direction sector normalized: sector(0..7) / 8.0 (float, [0..1))
    ///    Direction is interpreted as:
    ///    - if in [0..7]: already a sector; else treated as degrees and mapped to sector via floor((deg % 360)/45)
    /// 5: CanSee (1f if true, 0f otherwise)
    /// 6: IsSeenBySeeker (1f if true, 0f otherwise)
    /// 7..: KnownWallsFlat as worldSize*worldSize floats (1f if true, 0f otherwise)
    /// </summary>
    public class State
    {
        /// <summary>Agent x coordinate in grid cells. Must be >= 0.</summary>
        public int AgentX { get; }
        /// <summary>Agent y coordinate in grid cells. Must be >= 0.</summary>
        public int AgentY { get; }
        /// <summary>Other agent x coordinate in grid cells. Must be >= 0.</summary>
        public int OtherX { get; }
        /// <summary>Other agent y coordinate in grid cells. Must be >= 0.</summary>
        public int OtherY { get; }
        /// <summary>
        /// Direction. If in range [0..7] treated as 8-way sector. Otherwise treated as degrees (any int) and converted to sector.
        /// </summary>
        public int Direction { get; }
        /// <summary>Whether the agent can see the other agent.</summary>
        public bool CanSee { get; }
        /// <summary>Whether the agent is seen by the seeker (back-compat name: seen).</summary>
        public bool IsSeenBySeeker { get; }

        /// <summary>
        /// Known walls map flattened into a row-major 1D array of length worldSize*worldSize. May be empty if unknown.
        /// </summary>
        public bool[] KnownWallsFlat { get; }

        /// <summary>
        /// Primary constructor.
        /// </summary>
        /// <param name="ax">Agent x (>=0).</param>
        /// <param name="ay">Agent y (>=0).</param>
        /// <param name="ox">Other x (>=0).</param>
        /// <param name="oy">Other y (>=0).</param>
        /// <param name="direction">Direction sector [0..7] or degrees.</param>
        /// <param name="see">Whether agent can see the other.</param>
        /// <param name="knownWalls">Flattened known walls (can be empty). Defensive-copied.</param>
        /// <param name="isSeenBySeeker">Whether agent is seen by the seeker.</param>
        public State(int ax, int ay, int ox, int oy, int direction, bool see, bool[] knownWalls, bool isSeenBySeeker = false)
        {
            AgentX = Math.Max(0, ax);
            AgentY = Math.Max(0, ay);
            OtherX = Math.Max(0, ox);
            OtherY = Math.Max(0, oy);
            Direction = direction;
            CanSee = see;
            IsSeenBySeeker = isSeenBySeeker;
            KnownWallsFlat = knownWalls != null ? knownWalls.ToArray() : Array.Empty<bool>();
        }

        /// <summary>Backward-compatible constructor that accepts known walls without isSeenBySeeker.</summary>
        public State(int ax, int ay, int ox, int oy, int direction, bool see, bool[] knownWalls)
            : this(ax, ay, ox, oy, direction, see, knownWalls, false)
        { }

        /// <summary>Legacy constructor without KnownWalls and seen-by-seeker fields.</summary>
        public State(int ax, int ay, int ox, int oy, int direction, bool see)
            : this(ax, ay, ox, oy, direction, see, Array.Empty<bool>(), false)
        { }

        /// <summary>
        /// Legacy string serialization for logging/backward-compatibility.
        /// Format: "ax=..,ay=..,ox=..,oy=..,dir=..,see=..,seen=..,walls=0101" where seen and walls are optional.
        /// </summary>
        public override string ToString()
        {
            var basic = $"ax={AgentX},ay={AgentY},ox={OtherX},oy={OtherY},dir={Direction},see={CanSee},seen={IsSeenBySeeker}";
            if (KnownWallsFlat.Length > 0)
                return basic + $",walls={string.Join("", KnownWallsFlat.Select(x => x ? "1" : "0"))}";
            return basic;
        }

        /// <summary>
        /// Parses legacy string format. Supports optional seen= and walls= fields.
        /// Throws descriptive exceptions pointing to the offending token/character.
        /// </summary>
        public static State FromString(string s)
        {
            if (s is null) throw new ArgumentNullException(nameof(s), "State.FromString: input string is null");

            string Truncate(string input)
            {
                const int max = 120;
                if (input.Length <= max) return input;
                return input.Substring(0, max) + "...";
            }

            try
            {
                var p = s.Split(',', StringSplitOptions.TrimEntries);
                if (p.Length < 6)
                    throw new FormatException($"Expected at least 6 comma-separated tokens, got {p.Length} in '{Truncate(s)}'");

                int ParseIntToken(string token, string prefix)
                {
                    if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException($"Expected token '{prefix}...' but got '{token}' in '{Truncate(s)}'");
                    var span = token.AsSpan(prefix.Length);
                    if (span.Length == 0)
                        throw new FormatException($"Missing integer after '{prefix}' in '{Truncate(s)}'");
                    if (!int.TryParse(span, out int val))
                        throw new FormatException($"Invalid integer for '{prefix}' value '{span.ToString()}' in '{Truncate(s)}'");
                    return val;
                }

                bool ParseBoolToken(string token, string prefix)
                {
                    if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException($"Expected token '{prefix}...' but got '{token}' in '{Truncate(s)}'");
                    var span = token.AsSpan(prefix.Length);
                    if (span.Length == 0)
                        throw new FormatException($"Missing boolean after '{prefix}' in '{Truncate(s)}'");
                    if (!bool.TryParse(span, out bool val))
                        throw new FormatException($"Invalid boolean for '{prefix}' value '{span.ToString()}' in '{Truncate(s)}'");
                    return val;
                }

                int ax = ParseIntToken(p[0], "ax=");
                int ay = ParseIntToken(p[1], "ay=");
                int ox = ParseIntToken(p[2], "ox=");
                int oy = ParseIntToken(p[3], "oy=");
                int dir = ParseIntToken(p[4], "dir=");
                bool see = ParseBoolToken(p[5], "see=");

                bool seen = false;
                int idx = 6;
                if (p.Length > idx && p[idx].StartsWith("seen=", StringComparison.OrdinalIgnoreCase))
                {
                    seen = ParseBoolToken(p[idx], "seen=");
                    idx++;
                }

                bool[] walls = Array.Empty<bool>();
                if (p.Length > idx && p[idx].StartsWith("walls=", StringComparison.OrdinalIgnoreCase))
                {
                    string wallStr = p[idx].Substring("walls=".Length);
                    walls = new bool[wallStr.Length];
                    for (int i = 0; i < wallStr.Length; i++)
                    {
                        char c = wallStr[i];
                        if (c == '0') walls[i] = false;
                        else if (c == '1') walls[i] = true;
                        else throw new FormatException($"Invalid character '{c}' at walls[{i}] in '{Truncate(s)}'. Only '0' or '1' allowed.");
                    }
                }

                return new State(ax, ay, ox, oy, dir, see, walls, seen);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is IndexOutOfRangeException)
            {
                // Re-throw with a consistent prefix while preserving the specific message from above
                throw new FormatException($"State.FromString error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts state into a float[] feature vector for NN consumption.
        /// Order and normalization are documented on the class summary.
        /// </summary>
        /// <param name="worldSize">Grid size (number of cells per side). Must be >= 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when worldSize &lt; 1.</exception>
        /// <exception cref="ArgumentException">Thrown when KnownWallsFlat length is non-zero and not equal to worldSize*worldSize.</exception>
        public float[] ToArray(int worldSize)
        {
            if (worldSize < 1)
                throw new ArgumentOutOfRangeException(nameof(worldSize), worldSize, "ToArray: worldSize must be >= 1");

            // Normalize direction to 8-sector [0..7]
            int sector;
            if (Direction >= 0 && Direction <= 7)
            {
                sector = Direction;
            }
            else
            {
                int deg = Direction % 360;
                if (deg < 0) deg += 360;
                sector = deg / 45; // 0..7
            }
            float dirNorm = sector / 8.0f;

            float[] basic = new[]
            {
                AgentX / (float)worldSize,
                AgentY / (float)worldSize,
                OtherX / (float)worldSize,
                OtherY / (float)worldSize,
                dirNorm,
                CanSee ? 1f : 0f,
                IsSeenBySeeker ? 1f : 0f
            };

            int expectedWalls = worldSize * worldSize;
            if (KnownWallsFlat.Length != 0 && KnownWallsFlat.Length != expectedWalls)
            {
                string relation = KnownWallsFlat.Length < expectedWalls ? "shorter" : "longer";
                throw new ArgumentException($"ToArray: KnownWallsFlat length {KnownWallsFlat.Length} is {relation} than expected {expectedWalls} for worldSize={worldSize}");
            }

            var arr = new float[basic.Length + expectedWalls];
            basic.CopyTo(arr, 0);

            for (int i = 0; i < expectedWalls; i++)
            {
                bool b = i < KnownWallsFlat.Length ? KnownWallsFlat[i] : false;
                arr[basic.Length + i] = b ? 1f : 0f;
            }
            return arr;
        }

        /// <summary>
        /// JSON serialization with explicit versioning.
        /// </summary>
        public string ToJson()
        {
            var dto = new StateDto
            {
                version = 1,
                ax = AgentX,
                ay = AgentY,
                ox = OtherX,
                oy = OtherY,
                dir = Direction,
                see = CanSee,
                seen = IsSeenBySeeker,
                walls = KnownWallsFlat
            };
            var opts = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            return JsonSerializer.Serialize(dto, opts);
        }

        /// <summary>
        /// JSON deserialization with version check. Currently supports version = 1.
        /// </summary>
        public static State FromJson(string json)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            var dto = JsonSerializer.Deserialize<StateDto>(json) ?? throw new FormatException("FromJson: failed to deserialize state");
            if (dto.version != 1)
                throw new NotSupportedException($"FromJson: unsupported version {dto.version}");
            return new State(dto.ax, dto.ay, dto.ox, dto.oy, dto.dir, dto.see, dto.walls ?? Array.Empty<bool>(), dto.seen);
        }

        private sealed class StateDto
        {
            public int version { get; set; }
            public int ax { get; set; }
            public int ay { get; set; }
            public int ox { get; set; }
            public int oy { get; set; }
            public int dir { get; set; }
            public bool see { get; set; }
            public bool seen { get; set; }
            public bool[]? walls { get; set; }
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
            KnownWallsFlat.SequenceEqual(other.KnownWallsFlat);

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
                foreach (bool b in KnownWallsFlat)
                    hash = hash * 23 + (b ? 1 : 0);
                return hash;
            }
        }
    }
}