using System;
using System.Collections.Generic;

namespace ToolUse.Core.RL
{
    public class QAgent
    {
        private readonly QTable _q;
        private readonly float  _eps, _alpha, _gamma;
        private readonly Random _rnd = new();

        public int LastAction { get; private set; }

        public QAgent(QTable table,
            float epsilon     = 0.1f,
            float learningRate = 0.1f,
            float discount     = 0.95f)
        {
            _q = table;
            _eps   = epsilon;
            _alpha = learningRate;
            _gamma = discount;
        }

        public int ChooseAction(State s)
        {
            float[] q = _q[s];

            if (_rnd.NextDouble() < _eps)
            {
                LastAction = _rnd.Next(q.Length);
            }
            else
            {
                float max = float.NegativeInfinity;
                int   a   = 0;
                for (int i = 0; i < q.Length; i++)
                    if (q[i] > max) { max = q[i]; a = i; }
                LastAction = a;
            }
            return LastAction;
        }

        public void Learn(State s, int a, float r, State s2)
        {
            float[] q  = _q[s];
            float[] q2 = _q[s2];

            float best = float.NegativeInfinity;
            foreach (float v in q2) if (v > best) best = v;

            q[a] += _alpha * (r + _gamma * best - q[a]);
        }
    }
}