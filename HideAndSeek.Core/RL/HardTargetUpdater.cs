namespace HideAndSeek.Core.RL
{
    public class HardTargetUpdater : ITargetUpdater
    {
        private readonly int updateEvery;

        public HardTargetUpdater(int updateEvery)
        {
            this.updateEvery = updateEvery <= 0 ? 1 : updateEvery;
        }

        public void Update(object model, object target, int step)
        {
            if (step % updateEvery == 0)
            {
                dynamic m = model;
                dynamic t = target;
                t.load_state_dict(m.state_dict());
            }
        }
    }
}
