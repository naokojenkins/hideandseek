using TorchSharp;

namespace ToolUse.Core.RL
{
    public interface ILossCalculator
    {
        torch.Tensor Calculate(torch.Tensor qValues, torch.Tensor targets);
    }
}
