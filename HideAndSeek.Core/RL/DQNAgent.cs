using System;
using System.Collections.Generic;
using System.IO;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.IO;
using Newtonsoft.Json;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HideAndSeek.Core.RL
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
        private readonly ILogger<DQNAgent> _log;
        private float emaLoss = 0f;
        private MetricsRecorder _metrics => MetricsRecorder.Instance;

        // Внешний контекст, выставляемый окружением на шаг (роль/видимость)
        private ExternalContext externalContext = new ExternalContext();

        // Если true, и агент — Hider, то при видимости действует жадно (без exploration)
        private bool forceExploitWhenSeen = false;

        public DQNAgent(int stateSize, int actionSize, DQNConfig dqnCfg, torch.Device? deviceOverride = null, IDeviceProvider? deviceProvider = null, IOptimizerFactory? optimizerFactory = null, IReplayBufferFactory? replayBufferFactory = null, ILogger<DQNAgent>? logger = null)
        {
            _log = logger ?? NullLogger<DQNAgent>.Instance;

            if (stateSize <= 0) throw new ArgumentOutOfRangeException(nameof(stateSize), "stateSize must be > 0");
            if (actionSize <= 0) throw new ArgumentOutOfRangeException(nameof(actionSize), "actionSize must be > 0");
            if (dqnCfg is null) throw new ArgumentNullException(nameof(dqnCfg));

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

            // Device selection via provider when available
            if (deviceOverride != null)
                device = deviceOverride;
            else if (deviceProvider != null)
                device = deviceProvider.GetDevice();
            else
                device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;

            // Deterministic RNG: use centralized reproducibility provider
            rng = Reproducibility.CreateRandom("DQNAgent");

            model = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);
            targetModel = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);

            // Optimizer via factory when provided; else fallback to legacy creation
            if (optimizerFactory != null)
            {
                optimizer = optimizerFactory.Create(model);
            }
            else
            {
                if (useAdamW)
                    optimizer = torch.optim.AdamW(model.parameters(), dqnCfg.LearningRate, weight_decay: weightDecay);
                else
                    optimizer = torch.optim.Adam(model.parameters(), dqnCfg.LearningRate);
            }

            // Replay buffer via factory when provided; else default PER buffer
            if (replayBufferFactory != null)
                buffer = replayBufferFactory.Create(replayBufferSize, rng: rng);
            else
                buffer = new PrioritizedReplayBuffer(replayBufferSize, rng: rng);

            // Strategy components
            lossCalculator = useHuberLoss ? new HuberLossCalculator() : new MSELossCalculator();
            targetUpdater = useSoftTarget ? new SoftTargetUpdater(tau) : new HardTargetUpdater(updateTargetEvery);
            explorationPolicy = new EpsilonGreedyPolicy(epsilonStart, epsilonMin, epsilonDecay) { Epsilon = epsilonStart };
            epsilon = explorationPolicy.Epsilon;

            UpdateTargetModel();
        }

        [Obsolete("Use DQNAgent(int stateSize, int actionSize, DQNConfig, ...) to ensure hyperparameters come from configuration. This overload will be removed in a future release.")]
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

        /// <summary>
        /// Устанавливает внешний контекст (роль/видимость) для текущего шага.
        /// </summary>
        public void SetExternalContext(ExternalContext ctx)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            externalContext = ctx;
        }

        /// <summary>
        /// Включает/выключает режим «жадного действия при видимости» (актуально для Hider).
        /// </summary>
        public void SetForceExploitWhenSeen(bool enabled)
        {
            forceExploitWhenSeen = enabled;
        }

        public void SetEpsilon(float value)
        {
            if (explorationPolicy != null)
            {
                explorationPolicy.Epsilon = Math.Clamp(value, 0f, 1f);
                epsilon = explorationPolicy.Epsilon;
            }
        }

        /// <summary>
        /// Returns the current epsilon value of the exploration policy.
        /// </summary>
        public float Epsilon => epsilon;

        public long ChooseAction(float[] state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            if (state.Length != stateSize) throw new ArgumentOutOfRangeException(nameof(state), $"state length must be {stateSize}, got {state.Length}");

            CheckNaN(state, "ChooseAction:input");
            var input = torch.tensor(state, device: device).reshape(1, stateSize);
            CheckNaN(input, "ChooseAction:tensor_input");

            long action;
                // Если включено и агент - Hider, а его видят, то принудительно выбираем жадное действие
                bool forceExploit = forceExploitWhenSeen && externalContext != null && externalContext.IsHiderSeen;

                bool isSeekerSearchPhase = externalContext != null && !externalContext.IsHider && !externalContext.IsHiderSeen;
                bool explore;
                if (!forceExploit && isSeekerSearchPhase)
                {
                    // Повышенная directed exploration в фазе поиска: используем повышенный epsilon
                    float epsSearch = 0.6f;
                    try { epsSearch = MathF.Max(explorationPolicy.Epsilon, GameConfig.Instance.Seeker.EpsilonWhenSearching); } catch { }
                    explore = rng.NextDouble() < epsSearch;
                }
                else
                {
                    explore = !forceExploit && explorationPolicy.ShouldExplore(rng);
                }

                if (explore)
            {
                action = rng.Next(actionSize);
            }
            else
            {
                using (torch.no_grad())
                {
                    var qVals = model.forward(input);
                    CheckNaN(qVals, "ChooseAction:Q-values");

                    // Heuristic mixing during Seeker search phase to downweight useless rotations
                    bool isSeekerSearch = externalContext != null && !externalContext.IsHider && !externalContext.IsHiderSeen;
                    float alpha = 0f;
                    try { alpha = isSeekerSearch ? MathF.Max(0f, MathF.Min(1f, GameConfig.Instance.Seeker.HeuristicAlphaSearch)) : 0f; } catch { }

                    if (alpha > 0f && isSeekerSearch)
                    {
                        try
                        {
                            var act = GameConfig.Instance.Actions;
                            // Priority vector P: prefer Forward, then ForwardLeft/Right; penalize pure turns, idle, backward
                            var pArr = new float[actionSize];
                            for (int i = 0; i < actionSize; i++) pArr[i] = 0f;
                            if (act.Forward >= 0 && act.Forward < actionSize) pArr[act.Forward] = 1.0f;
                            if (act.ForwardLeft >= 0 && act.ForwardLeft < actionSize) pArr[act.ForwardLeft] = 0.5f;
                            if (act.ForwardRight >= 0 && act.ForwardRight < actionSize) pArr[act.ForwardRight] = 0.5f;
                            if (act.TurnLeft >= 0 && act.TurnLeft < actionSize) pArr[act.TurnLeft] = -0.5f;
                            if (act.TurnRight >= 0 && act.TurnRight < actionSize) pArr[act.TurnRight] = -0.5f;
                            if (act.Idle >= 0 && act.Idle < actionSize) pArr[act.Idle] = -0.3f;
                            if (act.Backward >= 0 && act.Backward < actionSize) pArr[act.Backward] = -0.3f;

                            var pTensor = torch.tensor(pArr, device: device).reshape(1, actionSize);
                            qVals = (1 - alpha) * qVals + alpha * pTensor;
                        }
                        catch { /* heuristic optional */ }
                    }

                    var t = qVals.argmax(Convert.ToInt64(1));
                    action = t.item<long>();
                    if (action < 0 || action >= actionSize)
                        throw new Exception($"[ChooseAction] Invalid action index: {action}");
                }
            }

            // Decay epsilon every environment step
            explorationPolicy.Step();
            epsilon = explorationPolicy.Epsilon;

            return action;
        }

        public void Store(float[] state, long action, float reward, float[] nextState, bool done)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            if (nextState is null) throw new ArgumentNullException(nameof(nextState));
            if (state.Length != stateSize) throw new ArgumentOutOfRangeException(nameof(state), $"state length must be {stateSize}, got {state.Length}");
            if (nextState.Length != stateSize) throw new ArgumentOutOfRangeException(nameof(nextState), $"nextState length must be {stateSize}, got {nextState.Length}");
            if (action < 0 || action >= actionSize) throw new ArgumentOutOfRangeException(nameof(action), $"action must be in [0,{actionSize - 1}], got {action}");

            CheckNaN(state, "Store:state");
            CheckNaN(nextState, "Store:nextState");
            if (float.IsNaN(reward) || float.IsInfinity(reward))
                throw new Exception($"[NaN/Inf] Store:reward={reward}");

            float r = reward;

            // Visibility-based shaping for Hider: add reward/penalty when seen by Seeker (before clipping/scaling)
            try
            {
                var cfg = GameConfig.Instance;
                if (externalContext != null &&
                    externalContext.IsHider &&
                    cfg.Hider.ApplyVisibilityShapingInAgent &&
                    externalContext.IsHiderSeen)
                {
                    r += cfg.Hider.RewardWhenSeenBySeeker;
                }
            }
            catch
            {
                // конфиг/контекст могут быть недоступны на ранних этапах — просто пропускаем shaping
            }

            // Reward clipping and scaling
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
                // Convert bool[] to float[] without LINQ to reduce allocations on hot path
                                var donesFloat = new float[donesArr.Length];
                                for (int i = 0; i < donesArr.Length; i++) donesFloat[i] = donesArr[i] ? 1.0f : 0.0f;
                                var dones = torch.tensor(donesFloat, dtype: ScalarType.Float32, device: device).unsqueeze(1);

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

                // simple EMA loss log
                float l = loss.ToSingle();
                if (emaLoss == 0f) emaLoss = l;
                emaLoss = 0.98f * emaLoss + 0.02f * l;
                if (steps % 50 == 0 || steps <= 50)
                {
                    // Compute simple Q stats on this batch
                    float qMean = 0f, qMax = 0f;
                    try
                    {
                        using (var qAll = qModelOutput.detach().cpu())
                        {
                            var arr = qAll.ToArray_Float();
                            if (arr.Length > 0)
                            {
                                double sum = 0;
                                double max = double.NegativeInfinity;
                                for (int i = 0; i < arr.Length; i++) { sum += arr[i]; if (arr[i] > max) max = arr[i]; }
                                qMean = (float)(sum / arr.Length);
                                qMax = (float)max;
                            }
                        }
                    }
                    catch { /* ignore metric calc errors */ }

                    _log.LogInformation("Training step metrics: steps={Steps} epsilon={Eps:F3} beta={Beta:F3} buffer={BufferCount} emaLoss={EmaLoss:F4} qMean={QMean:F4} qMax={QMax:F4}", steps, epsilon, beta, buffer.Count, emaLoss, qMean, qMax);
                    try { _metrics.RecordTraining(steps, epsilon, beta, buffer.Count, emaLoss, qMean, qMax); } catch { }
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
            // Save model weights
            model.save(weightsPath);

            // Do NOT serialize the replay buffer to avoid huge JSON files and potential OOM.
            // The buffer is an ephemeral training aid and does not need to persist across sessions.
            var agentState = new DQNAgentState
            {
                Epsilon = epsilon,
                Steps = steps,
                StateSize = stateSize,
                ActionSize = actionSize,
                // Buffer intentionally omitted
                Seed = GameConfig.Instance.Seed
            };

            // Serialize compactly to reduce IO and memory footprint without building a huge string in memory
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            var serializer = JsonSerializer.Create(settings);
            using (var fs = File.Open(statePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
            using (var jw = new JsonTextWriter(sw))
            {
                serializer.Serialize(jw, agentState);
            }
            _log.LogDebug("Model and state saved to weights={WeightsPath}, state={StatePath}", weightsPath, statePath);
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
                    _log.LogDebug("Model loaded from {WeightsPath}", weightsPath);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load model weights '{WeightsPath}': {Message}. Starting with fresh weights.", weightsPath, ex.Message);
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
                            // Загружаем только полностью совместимые и валидные записи (если буфер сохранен)
                            int added = 0;
                            if (state.Buffer != null)
                            {
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
                            }
                            _log.LogDebug("State loaded from {StatePath} (buffer entries: {Added})", statePath, added);
                        }
                        else
                        {
                            _log.LogWarning("Incompatible agent state '{StatePath}' (saved: {SavedStateSize}/{SavedActionSize}, current: {StateSize}/{ActionSize}). Ignoring saved buffer/state.", statePath, state.StateSize, state.ActionSize, stateSize, actionSize);
                            epsilon = epsilonStart;
                            steps = 0;
                            buffer.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load agent state '{StatePath}': {Message}. Resetting state.", statePath, ex.Message);
                    try { File.Delete(statePath); } catch { /* ignore */ }
                    epsilon = epsilonStart;
                    steps = 0;
                    buffer.Clear();
                }
            }
        }
    }


}
