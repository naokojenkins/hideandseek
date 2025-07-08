using System;
using System.Linq;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;


namespace ToolUse.Core.RL
{
    public class QAgent
    {
        private readonly QTable _table;
        private readonly float _alpha;
        private readonly float _gamma;
        private readonly float _epsilon;

        public QAgent(QTable table, float alpha = 0.1f, float gamma = 0.9f, float epsilon = 0.1f)
        {
            _table = table;
            _alpha = alpha;
            _gamma = gamma;
            _epsilon = epsilon;
        }

        public void UpdateAgent(Agent3D agent)
        {
            Console.WriteLine("[DEBUG] QAgent: агент обновлён");
        }

        public int ChooseAction(State state)
        {
            var values = _table.Get(state);

            if (new Random().NextDouble() < _epsilon)
            {
                return new Random().Next(0, 4);
            }

            return Array.IndexOf(values, values.Max());
        }

        public void Learn(State oldState, int action, float reward, State newState)
        {
            var oldValues = _table.Get(oldState);
            var newValues = _table.Get(newState);

            float oldValue = oldValues[action];
            float newValue = (1 - _alpha) * oldValue + _alpha * (reward + _gamma * newValues.Max());

            oldValues[action] = newValue;
            _table.Set(oldState, oldValues);

            // Логируем
            string oldKey = QTable.StateToString(oldState);
            string newKey = QTable.StateToString(newState);

            Console.WriteLine($"[DEBUG] QAgent.Learn() => s='{oldKey}', a={action}, r={reward:F2}, s2='{newKey}'");
            Console.WriteLine($"[DEBUG] QTable[{GetTableId()}].GET('{oldKey}')");
            Console.WriteLine($"[DEBUG] QTable[{GetTableId()}].GET('{newKey}')");
            Console.WriteLine($"[DEBUG] QAgent.Learn() => best={newValues.Max():F2}, before={oldValue:F2}");
            Console.WriteLine($"[DEBUG] QTable[{GetTableId()}].SET('{oldKey}', length={oldValues.Length})");
            Console.WriteLine($"[DEBUG] QAgent.Learn() => После обновления: {newValue:F2}");
        }

        private int GetTableId()
        {
            var type = _table.GetType();
            var field = type.GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (int)(field?.GetValue(_table) ?? -1);
        }
    }
}