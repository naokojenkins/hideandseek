using System;

namespace HideAndSeek.Core.RL
{
    public interface IReplayBufferFactory
    {
        IReplayBuffer Create(int capacity, float alpha = PrioritizedReplayBuffer.DefaultAlpha, Random? rng = null);
    }
}
