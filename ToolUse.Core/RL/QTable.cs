using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ToolUse.Core.RL
{
    public class QTable
    {
        private static int _idCounter = 0;
        private readonly int _id;
        private readonly Dictionary<string, float[]> _table = new();

        public QTable()
        {
            _id = ++_idCounter;
        }

        public float[] Get(State state)
        {
            string key = StateToString(state);
            if (!_table.TryGetValue(key, out var value))
            {
                value = new float[Actions.AllMoves.Length];
                _table[key] = value;
            }
            return value;
        }

        public void LoadFrom(Dictionary<string, float[]> data)
        {
            _table.Clear();
            foreach (var kvp in data)
            {
                _table[kvp.Key] = kvp.Value;
            }
        }

        public Dictionary<string, float[]> Export()
        {
            return new Dictionary<string, float[]>(_table);
        }

        public void Save(string file)
        {
            var json = JsonConvert.SerializeObject(_table, Formatting.None);
            File.WriteAllText(file, json);
        }

        public void Load(string file)
        {
            if (!File.Exists(file))
            {
                return;
            }

            var json = File.ReadAllText(file);
            var data = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json);
            if (data != null)
                LoadFrom(data);
        }

        public void Clear()
        {
            _table.Clear();
        }

        public void Set(State state, float[] values)
        {
            string key = StateToString(state);
            _table[key] = values;
        }

        public static string StateToString(State s) => s.ToString()!;
        public static State StringToState(string s) => State.FromString(s);

        public float[] this[string key]
        {
            get => _table.TryGetValue(key, out var value) ? value : null!;
            set => _table[key] = value;
        }
    }
}
