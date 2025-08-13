using TorchSharp;

namespace ToolUse.Core.RL
{
    public interface IOptimizerFactory
    {
        torch.optim.Optimizer Create(object model);
    }
}
