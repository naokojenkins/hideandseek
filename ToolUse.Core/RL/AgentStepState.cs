using System;

namespace ToolUse.Core.RL
{
    /// <summary>
    /// Консолидированное состояние одного агента на шаге.
    /// Универсальная структура вместо множества разнотипных коллекций.
    /// </summary>
    [Serializable]
    public sealed class AgentStepState
    {
        /// <summary>Идентификатор агента в рамках роли (необязателен).</summary>
        public string? AgentKey { get; set; }

        /// <summary>Последнее наблюдение (состояние) агента.</summary>
        public float[]? Observation { get; set; }

        /// <summary>Последнее выбранное действие (если есть).</summary>
        public int? LastAction { get; set; }

        /// <summary>Награда, полученная за текущий шаг.</summary>
        public float Reward { get; set; }

        /// <summary>Флаг завершения эпизода для агента.</summary>
        public bool Done { get; set; }

        /// <summary>Метка времени обновления состояния.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
