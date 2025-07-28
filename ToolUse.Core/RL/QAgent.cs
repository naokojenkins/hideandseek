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

        private static readonly int ActionCount = 3;

        public QAgent(QTable table, float alpha = 0.1f, float gamma = 0.9f, float epsilon = 0.1f)
        {
            _table = table;
            _alpha = alpha;
            _gamma = gamma;
            _epsilon = epsilon;
        }

        public int ChooseAction(State state)
        {
            var values = _table.Get(state);

            if (new Random().NextDouble() < _epsilon)
            {
                return new Random().Next(0, ActionCount);
            }

            float max = values.Max();
            var bestActions = values
                .Select((v, idx) => (v, idx))
                .Where(pair => Math.Abs(pair.v - max) < 1e-6)
                .Select(pair => pair.idx)
                .ToArray();

            return bestActions[new Random().Next(bestActions.Length)];
        }

        public void Learn(State oldState, int action, float reward, State newState)
        {
            var oldValues = _table.Get(oldState);
            var newValues = _table.Get(newState);

            float oldValue = oldValues[action];
            float newValue = (1 - _alpha) * oldValue + _alpha * (reward + _gamma * newValues.Max());

            oldValues[action] = newValue;
            _table.Set(oldState, oldValues);
        }
    }
}