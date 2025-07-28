using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ToolUse.Core.RL; // Даем доступ к Actions и State

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
            //Console.WriteLine($"[DEBUG] QTable.ctor => создан новый экземпляр #{_id}");
        }

        public float[] Get(State state)
        {
            string key = StateToString(state);
            if (!_table.TryGetValue(key, out var value))
            {
                value = new float[ActionCount.NumActions];
                _table[key] = value;
            }
            return value;
        }

        public void LoadFrom(Dictionary<string, float[]> data)
        {
            //Console.WriteLine($"[DEBUG] QTable[{_id}].LoadFrom() => загружено {data.Count} записей");
            _table.Clear();
            foreach (var kvp in data)
            {
                _table[kvp.Key] = kvp.Value;
            }
        }

        public Dictionary<string, float[]> Export()
        {
            //Console.WriteLine(_table.Count == 0
            //    ? $"[DEBUG] QTable[{_id}].Export => ПУСТАЯ ТАБЛИЦА"
            //    : $"[DEBUG] QTable[{_id}].Export => Записей: {_table.Count}");

            return new Dictionary<string, float[]>(_table);
        }

        public void Save(string file)
        {
            //Console.WriteLine($"[DEBUG] QTable[{_id}].Save() => Сохраняется в {file}");
            var json = JsonConvert.SerializeObject(_table, Formatting.None);
            File.WriteAllText(file, json);
        }

        public void Load(string file)
        {
            //Console.WriteLine($"[DEBUG] QTable[{_id}].Load() => Загружается из {file}");
            if (!File.Exists(file))
            {
                //Console.WriteLine($"[DEBUG] QTable[{_id}].Load() => Файл не найден: {file}");
                return;
            }

            var json = File.ReadAllText(file);
            var data = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json);
            if (data != null)
                LoadFrom(data);
        }

        public void Clear()
        {
            //Console.WriteLine($"[DEBUG] QTable[{_id}].Clear() => Таблица очищена");
            _table.Clear();
        }

        public void Set(State state, float[] values)
        {
            string key = StateToString(state);
            //Console.WriteLine($"[DEBUG] QTable[{_id}].SET('{key}', length={values.Length})");
            _table[key] = values;
        }

        public static string StateToString(State s) => s.ToString()!;
        public static State StringToState(string s) => State.FromString(s);

        // ✅ Убрали логи из индексатора
        public float[] this[string key]
        {
            get => _table.TryGetValue(key, out var value) ? value : null!;
            set => _table[key] = value;
        }
    }
}
