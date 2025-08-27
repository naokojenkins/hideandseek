using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolUse.Core.Config
{
    /// <summary>
    /// Единый источник правды для пространства действий.
    /// Позволяет явно назначить индексы и общее количество действий.
    /// По умолчанию: 0=L, 1=R, 2=FWD, 3=FWD+L, 4=FWD+R, 5=IDLE, 6=BWD, Count=7.
    /// </summary>
    public class ActionSpaceConfig
    {
        public int TurnLeft { get; set; } = 0;
        public int TurnRight { get; set; } = 1;
        public int Forward { get; set; } = 2;
        public int ForwardLeft { get; set; } = 3;
        public int ForwardRight { get; set; } = 4;

        /// <summary>
        /// Агент остаётся на месте (без поворота и перемещения).
        /// </summary>
        public int Idle { get; set; } = 5;

        /// <summary> Move backward. </summary>
        public int Backward { get; set; } = 6;

        public int Count { get; set; } = 7;

        /// <summary>
        /// Validate the action space configuration and return list of errors (empty if valid).
        /// Invariants:
        /// - All action indices must be unique (no duplicates).
        /// - Count must be >= (max index + 1) and > 0.
        /// - All indices must be >= 0.
        /// </summary>
        public string[] Validate()
        {
            var errors = new List<string>();
            var indices = new[] { TurnLeft, TurnRight, Forward, ForwardLeft, ForwardRight, Idle, Backward };

            // Non-negative check
            if (indices.Any(i => i < 0))
                errors.Add("Action indices must be non-negative.");

            // Duplicates
            var dupes = indices
                .GroupBy(i => i)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (dupes.Length > 0)
            {
                errors.Add($"Duplicate action indices detected: {string.Join(", ", dupes)}.");
            }

            // Count constraint
            int maxIdx = indices.Length == 0 ? -1 : indices.Max();
            if (Count <= 0)
                errors.Add("ActionSpaceConfig.Count must be > 0.");
            if (maxIdx >= 0 && Count < maxIdx + 1)
                errors.Add($"ActionSpaceConfig.Count must be >= maxIndex+1 (max={maxIdx}, required={maxIdx + 1}, got={Count}).");

            return errors.ToArray();
        }
    }
}
