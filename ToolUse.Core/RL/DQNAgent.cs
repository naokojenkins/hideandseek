using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using TorchSharp.Modules;
using ToolUse.Core.Config;

namespace ToolUse.Core.RL
{
    public class DQNAgent
    {
        private readonly int stateSize;
        private readonly int actionSize;
        private readonly float gamma;
        private readonly float epsilonStart;
        private readonly float epsilonMin;
        private readonly float epsilonDecay;
        private float epsilon;
        private readonly int batchSize;
        private readonly int replayBufferSize;
        private readonly IReplayBuffer buffer;
        private readonly torch.Device device;
        private readonly Random rng;

        private readonly DQNModel model;
        private readonly DQNModel targetModel;
        private readonly torch.optim.Optimizer optimizer;

        private int updateTargetEvery;
        private int steps = 0;
        private readonly bool useDoubleDQN = true;

        // Abstractions
        private readonly ILossCalculator lossCalculator;
        private readonly ITargetUpdater targetUpdater;
        private readonly IExplorationPolicy explorationPolicy;
        private readonly IOptimizerFactory optimizerFactory;

        // New training controls
        private readonly int warmupSize;
        private readonly int stepsPerUpdate;
        private readonly bool useHuberLoss;
        private readonly float maxGradNorm;
        private readonly bool useSoftTarget;
        private readonly float tau;
        private readonly float rewardClipAbs;
        private readonly float rewardScale;
        private readonly bool useAdamW;
        private readonly float weightDecay;

        // PER annealing
        private readonly float betaStart;
        private readonly float betaEnd;
        private readonly int betaFrames;
        private readonly bool useStratifiedSampling;
        private readonly IBetaScheduler betaScheduler;
        private int learnSteps = 0;

        // Logging
        private float emaLoss = 0f;

        public DQNAgent(int stateSize, int actionSize, DQNConfig dqnCfg, torch.Device? deviceOverride = null)
        {
            this.stateSize = stateSize;
            this.actionSize = actionSize;

            this.gamma = dqnCfg.Gamma;
            this.epsilonStart = dqnCfg.EpsilonStart;
            this.epsilonMin = dqnCfg.EpsilonMin;
            this.epsilonDecay = dqnCfg.EpsilonDecay;
            this.epsilon = dqnCfg.EpsilonStart;
            this.batchSize = dqnCfg.BatchSize;
            this.replayBufferSize = dqnCfg.ReplayBufferSize;
            this.updateTargetEvery = dqnCfg.UpdateTargetEvery;

            this.warmupSize = Math.Max(dqnCfg.WarmupSize, this.batchSize);
            this.stepsPerUpdate = Math.Max(1, dqnCfg.StepsPerUpdate);
            this.useHuberLoss = dqnCfg.UseHuberLoss;
            this.maxGradNorm = dqnCfg.MaxGradNorm;
            this.useSoftTarget = dqnCfg.UseSoftTarget;
            this.tau = dqnCfg.TargetUpdateTau;
            this.rewardClipAbs = dqnCfg.RewardClipAbs;
            this.rewardScale = dqnCfg.RewardScale;
            this.useAdamW = dqnCfg.UseAdamW;
            this.weightDecay = dqnCfg.WeightDecay;

            this.betaStart = dqnCfg.BetaStart;
            this.betaEnd = dqnCfg.BetaEnd;
            this.betaFrames = Math.Max(1, dqnCfg.BetaFrames);
            this.useStratifiedSampling = dqnCfg.UseStratifiedSampling;
            this.betaScheduler = new LinearBetaScheduler(this.betaStart, this.betaEnd, this.betaFrames);

            device = deviceOverride ?? (torch.cuda.is_available() ? torch.CUDA : torch.CPU);

            // Deterministic RNG from config seed (if provided)
            var cfgSeed = GameConfig.Instance.Seed;
            rng = cfgSeed != 0 ? new Random(cfgSeed) : new Random();

            model = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);
            targetModel = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);

            if (useAdamW)
                optimizer = torch.optim.AdamW(model.parameters(), dqnCfg.LearningRate, weight_decay: weightDecay);
            else
                optimizer = torch.optim.Adam(model.parameters(), dqnCfg.LearningRate);

            buffer = new PrioritizedReplayBuffer(replayBufferSize, rng: rng);

            // Strategy components
            lossCalculator = useHuberLoss ? new HuberLossCalculator() : new MSELossCalculator();
            targetUpdater = useSoftTarget ? new SoftTargetUpdater(tau) : new HardTargetUpdater(updateTargetEvery);
            explorationPolicy = new EpsilonGreedyPolicy(epsilonStart, epsilonMin, epsilonDecay) { Epsilon = epsilonStart };
            epsilon = explorationPolicy.Epsilon;

            // Optimizer factory
            optimizerFactory = new AdamOptimizerFactory();
            optimizer = optimizerFactory.Create(model);

            UpdateTargetModel();
        }

        public DQNAgent(
            int stateSize,
            int actionSize,
            torch.Device? deviceOverride = null,
            float gamma = 0.99f,
            float epsilonStart = 1.0f,
            float epsilonMin = 0.05f,
            float epsilonDecay = 0.995f,
            int batchSize = 64,
            int replayBufferSize = 10000,
            float lr = 0.0005f)
            : this(stateSize, actionSize, new DQNConfig
            {
                Gamma = gamma,
                EpsilonStart = epsilonStart,
                EpsilonMin = epsilonMin,
                EpsilonDecay = epsilonDecay,
                BatchSize = batchSize,
                ReplayBufferSize = replayBufferSize,
                LearningRate = lr
            }, deviceOverride)
        { }

        private void CheckNaN(float[] arr, string tag)
        {
            for (int i = 0; i < arr.Length; i++)
                if (float.IsNaN(arr[i]) || float.IsInfinity(arr[i]))
                    throw new Exception($"[NaN/Inf] {tag}: Index {i} value {arr[i]}");
        }

        private void CheckNaN(torch.Tensor t, string tag)
        {
            if (t.isnan().any().item<bool>())
                throw new Exception($"[NaN/Inf] {tag}: has NaN");
            if (t.isinf().any().item<bool>())
                throw new Exception($"[NaN/Inf] {tag}: has Inf");
        }

        public long ChooseAction(float[] state)
        {
            CheckNaN(state, "ChooseAction:input");
            var input = torch.tensor(state, device: device).reshape(1, stateSize);
            CheckNaN(input, "ChooseAction:tensor_input");

                            if (explorationPolicy.ShouldExplore(rng))
                return rng.Next(actionSize);

            using (torch.no_grad())
            {
                var qVals = model.forward(input);
                CheckNaN(qVals, "ChooseAction:Q-values");
                var t = qVals.argmax(Convert.ToInt64(1));
                long action = t.item<long>();
                if (action < 0 || action >= actionSize)
                    throw new Exception($"[ChooseAction] Invalid action index: {action}");
                return action;
            }
        }

        public void Store(float[] state, long action, float reward, float[] nextState, bool done)
        {
            CheckNaN(state, "Store:state");
            CheckNaN(nextState, "Store:nextState");
            if (float.IsNaN(reward) || float.IsInfinity(reward))
                throw new Exception($"[NaN/Inf] Store:reward={reward}");

            // Reward clipping and scaling
            float r = reward;
            if (rewardClipAbs > 0f)
                r = Math.Clamp(r, -rewardClipAbs, rewardClipAbs);
            r *= rewardScale;

            buffer.Add(new Experience(state, action, r, nextState, done));
        }

        public void Learn()
        {
            if (buffer.Count < warmupSize) return;

            for (int it = 0; it < stepsPerUpdate; it++)
            {
                if (buffer.Count < batchSize) break;

                float beta = CalcBeta();
                var (statesArr, actionsArr, rewardsArr, nextStatesArr, donesArr, weightsArr, indicesArr) =
                    buffer.Sample(batchSize, beta, useStratifiedSampling);

                foreach (var s in statesArr) CheckNaN(s, "Learn:States");
                foreach (var ns in nextStatesArr) CheckNaN(ns, "Learn:NextStates");

                var states = torch.tensor(JaggedTo2D(statesArr), dtype: ScalarType.Float32, device: device);
                var nextStates = torch.tensor(JaggedTo2D(nextStatesArr), dtype: ScalarType.Float32, device: device);

                CheckNaN(states, "Learn:states tensor");
                CheckNaN(nextStates, "Learn:nextStates tensor");

                var actions = torch.tensor(actionsArr, dtype: ScalarType.Int64, device: device).unsqueeze(1);
                var rewards = torch.tensor(rewardsArr, dtype: ScalarType.Float32, device: device).unsqueeze(1);
                var dones = torch.tensor(donesArr.Select(x => x ? 1.0f : 0.0f).ToArray(), dtype: ScalarType.Float32, device: device).unsqueeze(1);

                var qModelOutput = model.forward(states);
                CheckNaN(qModelOutput, "Learn:model.forward(states)");

                var qValues = qModelOutput.gather(1, actions);

                torch.Tensor nextQTarget;
                torch.Tensor targets;

                using (torch.no_grad())
                {
                    if (useDoubleDQN)
                    {
                        var nextQIdx = model.forward(nextStates).argmax(1).to_type(ScalarType.Int64).unsqueeze(1);
                        var targetOut = targetModel.forward(nextStates);
                        CheckNaN(targetOut, "Learn:targetModel.forward(nextStates)");
                        nextQTarget = targetOut.gather(1, nextQIdx);
                    }
                    else
                    {
                        var targetOut = targetModel.forward(nextStates);
                        CheckNaN(targetOut, "Learn:targetModel.forward(nextStates)");
                        nextQTarget = targetOut.max(1).values.unsqueeze(1);
                    }

                    CheckNaN(nextQTarget, "Learn:nextQTarget");
                    targets = rewards + gamma * nextQTarget * (1 - dones);
                }

                CheckNaN(targets, "Learn:targets");

                targets = targets.to_type(ScalarType.Float32);
                qValues = qValues.to_type(ScalarType.Float32);

                var weightsTensor = torch.tensor(weightsArr, dtype: ScalarType.Float32, device: device).unsqueeze(1);

                torch.Tensor lossTensor = lossCalculator.Calculate(qValues, targets);

                var loss = (lossTensor * weightsTensor).mean();

                CheckNaN(loss, "Learn:loss");

                optimizer.zero_grad();
                loss.backward();

                if (maxGradNorm > 0f)
                    torch.nn.utils.clip_grad_norm_(model.parameters(), maxGradNorm);

                optimizer.step();

                using (var errorTensor = (qValues - targets).abs().cpu().flatten())
                {
                    var errorArray = errorTensor.ToArray_Float();
                    buffer.UpdatePriorities(indicesArr, errorArray);
                }

                learnSteps++;
                steps++;

                targetUpdater.Update(model, targetModel, steps);

                                    explorationPolicy.Step();
                                    epsilon = explorationPolicy.Epsilon;

                // simple EMA loss log
                float l = loss.ToSingle();
                if (emaLoss == 0f) emaLoss = l;
                emaLoss = 0.98f * emaLoss + 0.02f * l;
                if (steps % 500 == 0)
                {
                    Console.WriteLine($"[DQN] steps={steps} eps={epsilon:F3} beta={beta:F3} buf={buffer.Count} emaLoss={emaLoss:F4}");
                }
            }
        }

        private float CalcBeta()
        {
            return betaScheduler.GetBeta(learnSteps);
        }

        private void UpdateTargetModel()
        {
            targetModel.load_state_dict(model.state_dict());
        }

        private void SoftUpdateTargetModel(float tau)
        {
            using (torch.no_grad())
            {
                var current = model.parameters().ToArray();
                var target = targetModel.parameters().ToArray();
                for (int i = 0; i < current.Length; i++)
                {
                    // target = (1 - tau) * target + tau * current
                    target[i].mul_(1 - tau);
                    target[i].add_(current[i] * tau);
                }
            }
        }

        private static float[,] JaggedTo2D(float[][] array)
        {
            int rows = array.Length;
            int cols = array[0].Length;
            var result = new float[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = array[i][j];
            return result;
        }

        public void SaveAll(string weightsPath, string statePath)
        {
            model.save(weightsPath);

            var agentState = new DQNAgentState
            {
                Epsilon = epsilon,
                Steps = steps,
                StateSize = stateSize,
                ActionSize = actionSize,
                Buffer = buffer.ToList(),
                Seed = GameConfig.Instance.Seed
            };
            File.WriteAllText(statePath, JsonConvert.SerializeObject(agentState, Formatting.Indented));
            Console.WriteLine($"[DEBUG] Model and state saved to {weightsPath}, {statePath}");
        }

        public void LoadAll(string weightsPath, string statePath)
        {
            // Пытаемся загрузить веса, при несовпадении архитектуры — пропускаем
            if (File.Exists(weightsPath))
            {
                try
                {
                    model.load(weightsPath);
                    targetModel.load(weightsPath);
                    Console.WriteLine($"[DEBUG] Model loaded from {weightsPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to load model weights '{weightsPath}': {ex.Message}. Starting with fresh weights.");
                    try { File.Delete(weightsPath); } catch { /* ignore */ }
                }
            }

            // Пытаемся загрузить состояние агента, проверяем совместимость размеров
            if (File.Exists(statePath))
            {
                try
                {
                    var state = JsonConvert.DeserializeObject<DQNAgentState>(File.ReadAllText(statePath));
                    if (state != null)
                    {
                        bool stateSizeOk = state.StateSize == 0 || state.StateSize == stateSize;
                        bool actionSizeOk = state.ActionSize == 0 || state.ActionSize == actionSize;

                        if (stateSizeOk && actionSizeOk)
                        {
                            epsilon = state.Epsilon;
                            if (explorationPolicy != null) explorationPolicy.Epsilon = epsilon;
                            steps = state.Steps;

                            buffer.Clear();
                            // Загружаем только полностью совместимые и валидные записи
                            int added = 0;
                            foreach (var exp in state.Buffer)
                            {
                                if (exp != null &&
                                    exp.State != null && exp.State.Length == stateSize &&
                                    exp.NextState != null && exp.NextState.Length == stateSize &&
                                    exp.Action >= 0 && exp.Action < actionSize &&
                                    !float.IsNaN(exp.Reward) && !float.IsInfinity(exp.Reward))
                                {
                                    buffer.Add(exp);
                                    added++;
                                }
                            }
                            Console.WriteLine($"[DEBUG] State loaded from {statePath} (buffer entries: {added})");
                        }
                        else
                        {
                            Console.WriteLine($"[WARN] Incompatible agent state '{statePath}' (saved: {state.StateSize}/{state.ActionSize}, current: {stateSize}/{actionSize}). Ignoring saved buffer/state.");
                            epsilon = epsilonStart;
                            steps = 0;
                            buffer.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to load agent state '{statePath}': {ex.Message}. Resetting state.");
                    try { File.Delete(statePath); } catch { /* ignore */ }
                    epsilon = epsilonStart;
                    steps = 0;
                    buffer.Clear();
                }
            }
        }
    }


}
