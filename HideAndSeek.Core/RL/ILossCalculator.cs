using TorchSharp;

namespace HideAndSeek.Core.RL
{
    public interface ILossCalculator
    {
        torch.Tensor Calculate(torch.Tensor qValues, torch.Tensor targets);
    }
}
