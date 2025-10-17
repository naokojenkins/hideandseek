using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HideAndSeek.Core.RL
{
    [Serializable]
    public class DQNAgentState
    {
        public float Epsilon { get; set; }
        public int Steps { get; set; }
        public int StateSize { get; set; }  // для проверки совместимости состояния
        public int ActionSize { get; set; } // для проверки совместимости действий

        // Буфер повторов может быть огромным. Никогда не сериализуем его в JSON чекпоинтов,
        // чтобы избежать OOM и гигантских файлов. Он эфемерен и восстанавливается заново при запуске.
        [JsonIgnore]
        public List<Experience> Buffer { get; set; } = new();

        // Не влияет на поведение при загрузке, только для информации
        public int Seed { get; set; }

        // Вспомогательное пошаговое состояние для отладки/визуализации — также не требуется в чекпоинтах.
        [JsonIgnore]
        public Dictionary<string, AgentStepState> StepByRole { get; set; } = new();
    }
}
