using System.Collections;
using TorchSharp;

namespace ToolUse.Core.RL
{
    public class SoftTargetUpdater : ITargetUpdater
    {
        private readonly float _tau;

        public SoftTargetUpdater(float tau)
        {
            if (float.IsNaN(tau) || float.IsInfinity(tau)) throw new ArgumentOutOfRangeException(nameof(tau), "tau must be finite");
            if (tau <= 0f || tau > 1f) throw new ArgumentOutOfRangeException(nameof(tau), "tau must be in (0,1]");
            _tau = tau;
        }

        public void Update(object model, object target, int step)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (target is null) throw new ArgumentNullException(nameof(target));

            using (torch.no_grad())
            {
                dynamic m = model;
                dynamic t = target;

                // Collect parameters without LINQ allocations
                var currentList = new System.Collections.Generic.List<dynamic>();
                foreach (var p in (IEnumerable)m.parameters()) currentList.Add(p);
                var targetList = new System.Collections.Generic.List<dynamic>();
                foreach (var p in (IEnumerable)t.parameters()) targetList.Add(p);

                if (currentList.Count != targetList.Count)
                    throw new InvalidOperationException($"SoftTargetUpdater.Update: parameter count mismatch: {currentList.Count} vs {targetList.Count}");
                for (int i = 0; i < currentList.Count; i++)
                {
                    targetList[i].mul_(1 - _tau);
                    targetList[i].add_(currentList[i] * _tau);
                }
            }
        }
    }
}
