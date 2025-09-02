using Newtonsoft.Json;
using HideAndSeek.Core.Config;
using Xunit;

namespace HideAndSeek.Tests
{
    public class ConfigSerializationTests
    {
        [Fact]
        public void TrainingConfig_RoundTrip_Json()
        {
            var cfg = new TrainingConfig
            {
                Seed = 777,
                StepsPerUpdate = 3,
                BatchSize = 64,
                Device = "Cpu",
                ModelsPath = "modelsX",
                LogsPath = "logsX",
            };
            var json = JsonConvert.SerializeObject(cfg);
            var back = JsonConvert.DeserializeObject<TrainingConfig>(json)!;
            Assert.Equal(cfg.Seed, back.Seed);
            Assert.Equal(cfg.StepsPerUpdate, back.StepsPerUpdate);
            Assert.Equal(cfg.BatchSize, back.BatchSize);
            Assert.Equal(cfg.Device, back.Device);
            Assert.Equal(cfg.ModelsPath, back.ModelsPath);
            Assert.Equal(cfg.LogsPath, back.LogsPath);
        }

        [Fact]
        public void ModelConfig_RoundTrip_Json()
        {
            var cfg = new ModelConfig
            {
                Hidden1 = 123,
                Hidden2 = 45,
                LearningRate = 0.001f,
                UseHuberLoss = false,
                MaxGradNorm = 0.0f,
                UseAdamW = false,
                WeightDecay = 0.0f,
                Gamma = 0.95f,
                RewardClipAbs = 0.5f,
                RewardScale = 2.0f,
                UpdateTargetEvery = 50,
                UseSoftTarget = true,
                TargetUpdateTau = 0.1f,
                EpsilonStart = 0.9f,
                EpsilonMin = 0.1f,
                EpsilonDecay = 0.99f,
            };
            var json = JsonConvert.SerializeObject(cfg);
            var back = JsonConvert.DeserializeObject<ModelConfig>(json)!;
            Assert.Equal(cfg.Hidden1, back.Hidden1);
            Assert.Equal(cfg.Hidden2, back.Hidden2);
            Assert.Equal(cfg.LearningRate, back.LearningRate);
            Assert.Equal(cfg.UseHuberLoss, back.UseHuberLoss);
            Assert.Equal(cfg.MaxGradNorm, back.MaxGradNorm);
            Assert.Equal(cfg.UseAdamW, back.UseAdamW);
            Assert.Equal(cfg.WeightDecay, back.WeightDecay);
            Assert.Equal(cfg.Gamma, back.Gamma);
            Assert.Equal(cfg.RewardClipAbs, back.RewardClipAbs);
            Assert.Equal(cfg.RewardScale, back.RewardScale);
            Assert.Equal(cfg.UpdateTargetEvery, back.UpdateTargetEvery);
            Assert.Equal(cfg.UseSoftTarget, back.UseSoftTarget);
            Assert.Equal(cfg.TargetUpdateTau, back.TargetUpdateTau);
            Assert.Equal(cfg.EpsilonStart, back.EpsilonStart);
            Assert.Equal(cfg.EpsilonMin, back.EpsilonMin);
            Assert.Equal(cfg.EpsilonDecay, back.EpsilonDecay);
        }

        [Fact]
        public void ReplayBufferConfig_RoundTrip_Json()
        {
            var cfg = new ReplayBufferConfig
            {
                Size = 5000,
                WarmupSize = 100,
                UseStratifiedSampling = false,
                BetaStart = 0.1f,
                BetaEnd = 0.9f,
                BetaFrames = 1234,
            };
            var json = JsonConvert.SerializeObject(cfg);
            var back = JsonConvert.DeserializeObject<ReplayBufferConfig>(json)!;
            Assert.Equal(cfg.Size, back.Size);
            Assert.Equal(cfg.WarmupSize, back.WarmupSize);
            Assert.Equal(cfg.UseStratifiedSampling, back.UseStratifiedSampling);
            Assert.Equal(cfg.BetaStart, back.BetaStart);
            Assert.Equal(cfg.BetaEnd, back.BetaEnd);
            Assert.Equal(cfg.BetaFrames, back.BetaFrames);
        }
    }
}
