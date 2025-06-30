using System.Collections.Generic;

namespace ToolUse.Core.RL;

public class QTable
{
    private readonly Dictionary<State, float[]> _table = new();

    public float[] Get(State state)
    {
        if (!_table.ContainsKey(state))
            _table[state] = new float[Actions.AllMoves.Length];
        return _table[state];
    }

    public void LoadFrom(Dictionary<State, float[]> data)
    {
        _table.Clear();
        foreach (var kvp in data)
            _table[kvp.Key] = kvp.Value;
    }

    public Dictionary<State, float[]> Export()
    {
        return _table;
    }

    public void Save(string file)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(_table);
        System.IO.File.WriteAllText(file, json);
    }

    // 🟦 ⬇️ ДОБАВЬ ЭТО
    public float[] this[State state]
    {
        get => Get(state);
        set => _table[state] = value;
    }
}