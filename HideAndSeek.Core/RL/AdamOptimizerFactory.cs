using TorchSharp;
using static TorchSharp.torch;

namespace HideAndSeek.Core.RL
{
    public class AdamOptimizerFactory : IOptimizerFactory
    {
        private readonly double lr;
        private readonly double weightDecay;

        public AdamOptimizerFactory(double learningRate, double weightDecay = 0.0)
        {
            lr = learningRate;
            this.weightDecay = weightDecay;
        }

        // Параметры по умолчанию: lr=1e-3, weightDecay=0
        public AdamOptimizerFactory() : this(0.001, 0.0) { }

        public torch.optim.Optimizer Create(object model)
        {
            dynamic m = model;
            var parameters = m.parameters();
            return torch.optim.Adam(parameters, lr: lr, weight_decay: weightDecay);
        }
    }
}
