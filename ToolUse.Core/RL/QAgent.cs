// using System;
// using System.Linq;
// using ToolUse.Core.RL;
// using ToolUse.Core.RaylibThreeD;
//
// namespace ToolUse.Core.RL
// {
//     public class QAgent
//     {
//         private readonly QTable _table;
//         private readonly float _alpha;
//         private readonly float _gamma;
//         private readonly float _epsilon;
//         private static readonly Random _rng = new(); // static!
//
//         public QAgent(QTable table, float alpha = 0.1f, float gamma = 0.9f, float epsilon = 0.1f)
//         {
//             _table = table;
//             _alpha = alpha;
//             _gamma = gamma;
//             _epsilon = epsilon;
//         }
//
//         public void UpdateAgent(Agent3D agent) { }
//
//         public int ChooseAction(State state)
//         {
//             var values = _table.Get(state);
//             if (_rng.NextDouble() < _epsilon)
//             {
//                 return _rng.Next(0, values.Length);
//             }
//             return Array.IndexOf(values, values.Max());
//         }
//
//         public void Learn(State oldState, int action, float reward, State newState)
//         {
//             var oldValues = _table.Get(oldState);
//             var newValues = _table.Get(newState);
//
//             float oldValue = oldValues[action];
//             float newValue = (1 - _alpha) * oldValue + _alpha * (reward + _gamma * newValues.Max());
//
//             oldValues[action] = newValue;
//             _table.Set(oldState, oldValues);
//         }
//     }
// }