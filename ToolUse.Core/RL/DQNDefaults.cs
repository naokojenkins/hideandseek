using ToolUse.Core.Config;

namespace ToolUse.Core.RL
{
    /// <summary>
    /// Глобальные значения по умолчанию для гиперпараметров DQN,
    /// экспортируемые как статические члены для удобного доступа.
    /// </summary>
    public static class DQNDefaults
    {
        // Используем тип double, чтобы напрямую передавать в оптимизаторы TorchSharp.
        public static double learningRate => GameConfig.Instance.DQN.LearningRate;
        public static double weightDecay  => GameConfig.Instance.DQN.WeightDecay;
    }
}
