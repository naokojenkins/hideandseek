using System;

namespace HideAndSeek.Core.RL
{
    [Serializable]
    public class Experience
    {
        // Fields with private setters for better encapsulation
        public float[] State { get; private set; }
        public long Action { get; private set; }
        public float Reward { get; private set; }
        public float[] NextState { get; private set; }
        public bool Done { get; private set; }

        // Constructor with parameter validation
        public Experience(float[] state, long action, float reward, float[] nextState, bool done)
        {
            State = state ?? throw new ArgumentNullException(nameof(state), "State cannot be null");
            Action = action;
            Reward = reward;
            NextState = nextState ?? throw new ArgumentNullException(nameof(nextState), "NextState cannot be null");
            Done = done;
        }

        // Override ToString for easier debugging and logging
        public override string ToString()
        {
            return $"Experience(State: [{string.Join(", ", State)}], Action: {Action}, Reward: {Reward}, NextState: [{string.Join(", ", NextState)}], Done: {Done})";
        }
    }
}