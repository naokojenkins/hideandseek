using TorchSharp;

namespace HideAndSeek.Core.RL
{
    public interface IOptimizerFactory
    {
        torch.optim.Optimizer Create(object model);
    }
}
