using System;
using System.Collections.Generic;
using System.Numerics;
using HideAndSeek.Core.Config;

namespace HideAndSeek.Core.RaylibThreeD
{
    public enum MemoryKind { Opponent, Ally }

    public sealed class MemoryEntry
    {
        public string TargetId { get; init; } = string.Empty;
        public MemoryKind Kind { get; init; }
        public Vector3 LastPosition { get; set; }
        public float? LastDirectionDeg { get; set; }
        public float Timestamp { get; set; }
        public float Confidence { get; set; }
    }

    public sealed class AgentMemory
    {
        private readonly Dictionary<string, MemoryEntry> _opponents = new();
        private readonly Dictionary<string, MemoryEntry> _allies = new();

        public IReadOnlyDictionary<string, MemoryEntry> Opponents => _opponents;
        public IReadOnlyDictionary<string, MemoryEntry> Allies => _allies;

        public void Clear()
        {
            _opponents.Clear();
            _allies.Clear();
        }

        public void ReportSeen(string id, MemoryKind kind, Vector3 pos, float? dirDeg, float time)
        {
            var dict = kind == MemoryKind.Opponent ? _opponents : _allies;
            if (!dict.TryGetValue(id, out var entry))
            {
                entry = new MemoryEntry { TargetId = id, Kind = kind };
                dict[id] = entry;
            }
            entry.LastPosition = pos;
            entry.LastDirectionDeg = dirDeg;
            entry.Timestamp = time;
            entry.Confidence = 1.0f; // refresh to max on observation
        }

        public bool TryGetLastOpponent(out MemoryEntry entry)
        {
            // Return the most recent non-stale opponent by timestamp with highest confidence
            MemoryEntry? best = null;
            foreach (var kv in _opponents)
            {
                var e = kv.Value;
                if (e.Confidence <= 0f) continue;
                if (best == null || e.Timestamp > best.Timestamp || (Math.Abs(e.Timestamp - best.Timestamp) < 1e-4 && e.Confidence > best.Confidence))
                    best = e;
            }
            if (best != null)
            {
                entry = best;
                return true;
            }
            entry = default!;
            return false;
        }

        public IEnumerable<MemoryEntry> GetAllies()
        {
            return _allies.Values;
        }

        public void Decay(float now)
        {
            var cfg = GameConfig.Instance.Memory;
            float maxAge = Math.Max(0f, cfg.MaxAgeSeconds);
            float decay = Math.Max(0f, cfg.DecayPerSecond);

            void Process(Dictionary<string, MemoryEntry> dict)
            {
                var toRemove = new List<string>();
                foreach (var (id, e) in dict)
                {
                    float age = MathF.Max(0f, now - e.Timestamp);
                    if (age > maxAge)
                    {
                        toRemove.Add(id);
                        continue;
                    }
                    float dt = MathF.Min(age, 10f); // safety cap
                    e.Confidence = MathF.Max(0f, e.Confidence - decay * dt);
                }
                foreach (var id in toRemove) dict.Remove(id);
            }

            Process(_opponents);
            Process(_allies);
        }

        // Utility for seeker reaching last position but not seeing target
        public void ReduceConfidenceFor(string id, float newConfidence)
        {
            if (_opponents.TryGetValue(id, out var e))
            {
                e.Confidence = MathF.Max(0f, MathF.Min(1f, newConfidence));
            }
        }
    }
}