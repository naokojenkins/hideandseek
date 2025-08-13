namespace ToolUse.Core.RL
{
    public interface IBetaScheduler
    {
        float GetBeta(int learnStep);
    }
}
