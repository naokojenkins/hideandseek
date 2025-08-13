using System.Collections.Generic;

namespace ToolUse.Core.RL
{
    public interface IReplayBuffer
    {
        int Count { get; }
        void Add(Experience exp, float error = 1.0f);
        (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones, float[] Weights, int[] Indices)
            Sample(int batchSize, float beta, bool stratified);
        void Clear();
        List<Experience> ToList();
        void UpdatePriorities(int[] indices, float[] errors);
    }
}
