using System;

namespace HideAndSeek.Core.RL
{
    public interface IExplorationPolicy
    {
        float Epsilon { get; set; }
        bool ShouldExplore(Random rng);
        void Step();
    }
}
