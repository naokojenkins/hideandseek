using System;
using System.Collections.Generic;

namespace HideAndSeek.Core.RL
{
    [Serializable]
    public class DQNAgentState
    {
        public float Epsilon { get; set; }
        public int Steps { get; set; }
        public int StateSize { get; set; }  // для проверки совместимости состояния
        public int ActionSize { get; set; } // для проверки совместимости действий
        public List<Experience> Buffer { get; set; } = new();
        // Не влияет на поведение при загрузке, только для информации
        public int Seed { get; set; }

        // Консолидация per-agent состояния: вместо множества коллекций
        // используем словарь по роли -> состояние шага агента в этой роли.
        // Ключ роли произвольный (например, "self", "ally", "enemy" и т.д.).
        public Dictionary<string, AgentStepState> StepByRole { get; set; } = new();
    }
}
