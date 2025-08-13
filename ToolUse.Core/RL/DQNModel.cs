using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace ToolUse.Core.RL
{
    public class DQNModel : Module
    {
        private readonly Linear fc1;
        private readonly Linear fc2;
        private readonly Linear valueStream;
        private readonly Linear advantageStream;

        public DQNModel(int inputSize, int outputSize, int hidden1 = 256, int hidden2 = 256)
            : base("DQNModel")
        {
            fc1 = Linear(inputSize, hidden1);
            fc2 = Linear(hidden1, hidden2);
            valueStream = Linear(hidden2, 1);
            advantageStream = Linear(hidden2, outputSize);
            RegisterComponents();
        }

        public torch.Tensor forward(torch.Tensor x)
        {
            x = functional.relu(fc1.forward(x));
            x = functional.relu(fc2.forward(x));
            var value = valueStream.forward(x);
            var advantage = advantageStream.forward(x);
            return value + (advantage - advantage.mean(new long[] { 1 }, keepdim: true));
        }
    }
}
