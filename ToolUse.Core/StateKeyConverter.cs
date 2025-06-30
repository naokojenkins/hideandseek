using System;
using Newtonsoft.Json;

namespace ToolUse.Core.RL
{
    /// <summary>Позволяет сериализовать / десериализовать State, когда он выступает КЛЮЧОМ словаря.</summary>
    public sealed class StateKeyConverter : JsonConverter<State>
    {
        public override void WriteJson(JsonWriter writer, State value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());          // ключ → строка
        }

        public override State ReadJson(JsonReader reader, Type t, State? _, bool __, JsonSerializer ___)
        {
            return State.FromString((string)reader.Value!); // строка → State
        }
    }
}