using System;

namespace ToolUse.Core.RL
{
    public class EpsilonGreedyPolicy : IExplorationPolicy
    {
        private readonly float epsilonMin;
        private readonly float epsilonDecay;

        public float Epsilon { get; set; }

        public EpsilonGreedyPolicy(float epsilonStart, float epsilonMin, float epsilonDecay)
        {
            this.Epsilon = epsilonStart;
            this.epsilonMin = epsilonMin;
            this.epsilonDecay = epsilonDecay;
        }

        public bool ShouldExplore(Random rng)
        {
            return rng.NextDouble() < Epsilon;
        }

        public void Step()
        {
            if (Epsilon > epsilonMin)
                Epsilon *= epsilonDecay;
            if (Epsilon < epsilonMin) Epsilon = epsilonMin;
        }
    }
}
