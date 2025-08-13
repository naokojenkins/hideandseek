using TorchSharp;
using static TorchSharp.torch.nn;

namespace ToolUse.Core.RL
{
    public class MSELossCalculator : ILossCalculator
    {
        public torch.Tensor Calculate(torch.Tensor qValues, torch.Tensor targets)
        {
            return functional.mse_loss(qValues, targets, Reduction.None);
        }
    }
}
