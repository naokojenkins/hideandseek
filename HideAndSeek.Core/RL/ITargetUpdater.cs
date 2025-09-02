namespace HideAndSeek.Core.RL
{
    public interface ITargetUpdater
    {
        void Update(object model, object target, int step);
    }
}
