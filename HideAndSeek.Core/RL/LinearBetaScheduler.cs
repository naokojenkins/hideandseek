using System;

namespace HideAndSeek.Core.RL
{
    public class LinearBetaScheduler : IBetaScheduler
    {
        private readonly float betaStart;
        private readonly float betaEnd;
        private readonly int betaFrames;

        public LinearBetaScheduler(float betaStart, float betaEnd, int betaFrames)
        {
            this.betaStart = betaStart;
            this.betaEnd = betaEnd;
            this.betaFrames = Math.Max(1, betaFrames);
        }

        public float GetBeta(int learnStep)
        {
            if (learnStep <= 0) return betaStart;
            if (learnStep >= betaFrames) return betaEnd;

            float t = (float)learnStep / betaFrames;
            t = Math.Clamp(t, 0f, 1f);
            return betaStart + (betaEnd - betaStart) * t;
        }
    }
}
