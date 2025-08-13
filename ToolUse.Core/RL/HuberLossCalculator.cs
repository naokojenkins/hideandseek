using TorchSharp;
using static TorchSharp.torch.nn;

namespace ToolUse.Core.RL
{
    public class HuberLossCalculator : ILossCalculator
    {
        public torch.Tensor Calculate(torch.Tensor qValues, torch.Tensor targets)
        {
            return functional.smooth_l1_loss(qValues, targets, Reduction.None);
        }
    }
}
