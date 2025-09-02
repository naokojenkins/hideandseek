using System;
using TorchSharp;
using static TorchSharp.torch.nn;

namespace HideAndSeek.Core.RL
{
    public class MSELossCalculator : ILossCalculator
    {
        public torch.Tensor Calculate(torch.Tensor qValues, torch.Tensor targets)
        {
            const string ctx = "MSELossCalculator.Calculate";
            TorchGuards.EnsureSameShape(qValues, targets, ctx);
            TorchGuards.EnsureFinite(qValues, ctx + " qValues");
            TorchGuards.EnsureFinite(targets, ctx + " targets");

            var loss = functional.mse_loss(qValues, targets, Reduction.None);
            TorchGuards.EnsureFinite(loss, ctx + " loss");
            return loss;
        }
    }
}
