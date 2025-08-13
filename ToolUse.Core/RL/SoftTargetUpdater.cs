using System.Linq;
using TorchSharp;

namespace ToolUse.Core.RL
{
    public class SoftTargetUpdater : ITargetUpdater
    {
        private readonly float tau;

        public SoftTargetUpdater(float tau)
        {
            this.tau = tau;
        }

        public void Update(object model, object target, int step)
        {
            using (torch.no_grad())
            {
                dynamic m = model;
                dynamic t = target;
                var current = ((System.Collections.IEnumerable)m.parameters()).Cast<dynamic>().ToArray();
                var tgt = ((System.Collections.IEnumerable)t.parameters()).Cast<dynamic>().ToArray();
                for (int i = 0; i < current.Length; i++)
                {
                    tgt[i].mul_(1 - tau);
                    tgt[i].add_(current[i] * tau);
                }
            }
        }
    }
}
