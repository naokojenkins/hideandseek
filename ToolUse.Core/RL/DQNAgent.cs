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
    [Serializable]
    public class DQNAgentState
    {
        public float Epsilon { get; set; }
        public int Steps { get; set; }
        public List<Experience> Buffer { get; set; } = new();
    }

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
        private readonly PrioritizedReplayBuffer buffer;
        private readonly torch.Device device;

        private readonly DQNModel model;
        private readonly DQNModel targetModel;
        private readonly torch.optim.Optimizer optimizer;

        private int updateTargetEvery;
        private int steps = 0;
        private readonly bool useDoubleDQN = true;

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

            device = deviceOverride ?? (torch.cuda.is_available() ? torch.CUDA : torch.CPU);

            model = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);
            targetModel = new DQNModel(stateSize, actionSize, dqnCfg.Hidden1, dqnCfg.Hidden2).to(device);
            optimizer = torch.optim.Adam(model.parameters(), dqnCfg.LearningRate);

            buffer = new PrioritizedReplayBuffer(replayBufferSize);

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

            if (new Random().NextDouble() < epsilon)
                return new Random().Next(actionSize);

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
            buffer.Add(new Experience(state, action, reward, nextState, done));
        }

        public void Learn()
        {
            if (buffer.Count < batchSize) return;

            var (statesArr, actionsArr, rewardsArr, nextStatesArr, donesArr, weightsArr, indicesArr) = buffer.Sample(batchSize);

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
                    // ВАЖНО: следующий блок полностью совместим с gather
                    var nextQ = model.forward(nextStates).argmax(1).to_type(ScalarType.Int64).unsqueeze(1);
                    var targetOut = targetModel.forward(nextStates);
                    CheckNaN(targetOut, "Learn:targetModel.forward(nextStates)");
                    nextQTarget = targetOut.gather(1, nextQ);
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

            var loss = functional.mse_loss(qValues, targets, Reduction.None);
            var weightsTensor = torch.tensor(weightsArr, dtype: ScalarType.Float32, device: device).unsqueeze(1);
            loss = (loss * weightsTensor).mean();

            CheckNaN(loss, "Learn:loss");

            optimizer.zero_grad();
            loss.backward();
            optimizer.step();

            using (var errorTensor = (qValues - targets).abs().cpu().flatten())
            {
                var errorArray = errorTensor.ToArray_Float();
                buffer.UpdatePriorities(indicesArr, errorArray);
            }

            steps++;
            if (steps % updateTargetEvery == 0)
                UpdateTargetModel();

            if (epsilon > epsilonMin)
                epsilon *= epsilonDecay;
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
            model.save(weightsPath);

            var agentState = new DQNAgentState
            {
                Epsilon = epsilon,
                Steps = steps,
                Buffer = buffer.ToList()
            };
            File.WriteAllText(statePath, JsonConvert.SerializeObject(agentState, Formatting.Indented));
            Console.WriteLine($"[DEBUG] Model and state saved to {weightsPath}, {statePath}");
        }

        public void LoadAll(string weightsPath, string statePath)
        {
            if (File.Exists(weightsPath))
            {
                model.load(weightsPath);
                targetModel.load(weightsPath);
                Console.WriteLine($"[DEBUG] Model loaded from {weightsPath}");
            }

            if (File.Exists(statePath))
            {
                var state = JsonConvert.DeserializeObject<DQNAgentState>(File.ReadAllText(statePath));
                if (state != null)
                {
                    epsilon = state.Epsilon;
                    steps = state.Steps;
                    buffer.Clear();
                    foreach (var exp in state.Buffer)
                        buffer.Add(exp);
                    Console.WriteLine($"[DEBUG] State loaded from {statePath}");
                }
            }
        }
    }

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

    [Serializable]
    public class Experience
    {
        public float[] State;
        public long Action;
        public float Reward;
        public float[] NextState;
        public bool Done;

        public Experience(float[] state, long action, float reward, float[] nextState, bool done)
        {
            State = state;
            Action = action;
            Reward = reward;
            NextState = nextState;
            Done = done;
        }
    }

    public class PrioritizedReplayBuffer : IEnumerable<Experience>
    {
        private class PrioritizedExperience
        {
            public Experience Experience { get; set; }
            public float Priority { get; set; }
        }

        private readonly int capacity;
        private readonly List<PrioritizedExperience> buffer = new();
        private readonly float alpha = 0.6f;
        private readonly float epsilon = 1e-6f;

        public PrioritizedReplayBuffer(int capacity, float alpha = 0.6f)
        {
            this.capacity = capacity;
            this.alpha = alpha;
        }

        public int Count => buffer.Count;

        public void Add(Experience exp, float error = 1.0f)
        {
            buffer.Add(new PrioritizedExperience
            {
                Experience = exp,
                Priority = (float)Math.Pow(Math.Abs(error) + epsilon, alpha)
            });

            if (buffer.Count > capacity)
                buffer.RemoveAt(0);
        }

        public (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones, float[] Weights, int[] Indices)
            Sample(int batchSize)
        {
            float totalPriority = buffer.Sum(x => x.Priority);
            float[] probabilities = buffer.Select(x => x.Priority / totalPriority).ToArray();

            var indices = new List<int>();
            var rnd = new Random();
            for (int i = 0; i < batchSize; i++)
            {
                float p = (float)rnd.NextDouble();
                float cumulative = 0f;
                int idx = 0;
                foreach (var prob in probabilities)
                {
                    cumulative += prob;
                    if (cumulative >= p)
                    {
                        indices.Add(idx);
                        break;
                    }
                    idx++;
                }
            }

            float[] weights = indices.Select(x => (float)Math.Pow(buffer.Count * probabilities[x], -0.4)).ToArray();
            float maxWeight = weights.Max();
            weights = weights.Select(w => w / maxWeight).ToArray();

            return (
                indices.Select(i => buffer[i].Experience.State).ToArray(),
                indices.Select(i => buffer[i].Experience.Action).ToArray(),
                indices.Select(i => buffer[i].Experience.Reward).ToArray(),
                indices.Select(i => buffer[i].Experience.NextState).ToArray(),
                indices.Select(i => buffer[i].Experience.Done).ToArray(),
                weights,
                indices.ToArray()
            );
        }

        public IEnumerator<Experience> GetEnumerator() => buffer.Select(x => x.Experience).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => buffer.Select(x => x.Experience).GetEnumerator();
        public void Clear() => buffer.Clear();
        public List<Experience> ToList() => buffer.Select(x => x.Experience).ToList();

        public void UpdatePriorities(int[] indices, float[] errors)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                int idx = indices[i];
                float error = errors[i];
                buffer[idx].Priority = (float)Math.Pow(Math.Abs(error) + epsilon, alpha);
            }
        }
    }

    public static class TensorExtensions
    {
        public static float[] ToArray_Float(this torch.Tensor tensor)
        {
            return tensor.cpu().data<float>().ToArray();
        }
    }
}
