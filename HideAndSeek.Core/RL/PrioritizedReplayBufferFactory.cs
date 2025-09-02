using System;

namespace HideAndSeek.Core.RL
{
    public class PrioritizedReplayBufferFactory : IReplayBufferFactory
    {
        public IReplayBuffer Create(int capacity, float alpha = PrioritizedReplayBuffer.DefaultAlpha, Random? rng = null)
        {
            return new PrioritizedReplayBuffer(capacity, alpha, rng);
        }
    }
}
