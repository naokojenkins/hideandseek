using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using TorchSharp.Modules;

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
        private readonly ReplayBuffer buffer;
        private readonly torch.Device device;

        private readonly DQNModel model;
        private readonly DQNModel targetModel;
        private readonly torch.optim.Optimizer optimizer;

        private int updateTargetEvery = 200;
        private int steps = 0;

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
        {
            this.stateSize = stateSize;
            this.actionSize = actionSize;
            this.gamma = gamma;
            this.epsilonStart = epsilonStart;
            this.epsilonMin = epsilonMin;
            this.epsilonDecay = epsilonDecay;
            this.epsilon = epsilonStart;
            this.batchSize = batchSize;
            this.replayBufferSize = replayBufferSize;

            device = deviceOverride ?? (torch.cuda.is_available() ? torch.CUDA : torch.CPU);

            model = new DQNModel(stateSize, actionSize).to(device);
            targetModel = new DQNModel(stateSize, actionSize).to(device);
            optimizer = torch.optim.Adam(model.parameters(), lr);

            buffer = new ReplayBuffer(replayBufferSize);

            UpdateTargetModel();
        }

        public long ChooseAction(float[] state)
        {
            var input = torch.tensor(state, device: device).reshape(1, stateSize);

            if (new Random().NextDouble() < epsilon)
                return new Random().Next(actionSize);

            using (torch.no_grad())
            {
                var qVals = model.forward(input);
                var t = qVals.argmax(Convert.ToInt64(1));
                return t.item<long>();
            }
        }

        public void Store(float[] state, long action, float reward, float[] nextState, bool done)
        {
            buffer.Add(new Experience(state, action, reward, nextState, done));
        }

        public void Learn()
        {
            if (buffer.Count < batchSize) return;

            var batch = buffer.Sample(batchSize);

            var states = torch.tensor(JaggedTo2D(batch.States), dtype: ScalarType.Float32, device: device);
            var nextStates = torch.tensor(JaggedTo2D(batch.NextStates), dtype: ScalarType.Float32, device: device);

            var actionsArr = batch.Actions.Select(a => (long)a).ToArray();
            var actions = torch.tensor(actionsArr, dtype: ScalarType.Int64, device: device).unsqueeze(1);

            var rewardsArr = batch.Rewards.Select(r => (float)r).ToArray();
            var rewards = torch.tensor(rewardsArr, dtype: ScalarType.Float32, device: device).unsqueeze(1);

            var donesArr = batch.Dones.Select(x => x ? 1.0f : 0.0f).ToArray();
            var dones = torch.tensor(donesArr, dtype: ScalarType.Float32, device: device).unsqueeze(1);

            var qModelOutput = model.forward(states);
            var qValues = qModelOutput.gather(1, actions);

            torch.Tensor nextQTarget;
            torch.Tensor targets;

            using (torch.no_grad())
            {
                var nextModelOutput = model.forward(nextStates);
                var nextQ = nextModelOutput.argmax(1).to_type(ScalarType.Int64).unsqueeze(1);
                var targetOut = targetModel.forward(nextStates);
                nextQTarget = targetOut.gather(1, nextQ);
                targets = rewards + gamma * nextQTarget * (1 - dones);
            }

            targets = targets.to_type(ScalarType.Float32);
            qValues = qValues.to_type(ScalarType.Float32);

            var loss = functional.mse_loss(qValues, targets);
            Console.WriteLine($"[DEBUG] Loss computed: {loss.item<float>()}");

            optimizer.zero_grad();
            loss.backward();
            optimizer.step();

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

        // ======== Новое: Сохранение и загрузка всего состояния ========
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
        // ======== Конец блока сохранения ========
    }

    public class DQNModel : Module
    {
        private readonly Linear fc1;
        private readonly Linear fc2;
        private readonly Linear fc3;

        public DQNModel(int inputSize, int outputSize) : base("DQNModel")
        {
            fc1 = Linear(inputSize, 128);
            fc2 = Linear(128, 128);
            fc3 = Linear(128, outputSize);
            RegisterComponents();
        }

        public torch.Tensor forward(torch.Tensor x)
        {
            x = functional.relu(fc1.forward(x));
            x = functional.relu(fc2.forward(x));
            x = fc3.forward(x);
            return x;
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

    public class ReplayBuffer : IEnumerable<Experience>
    {
        private readonly int capacity;
        private readonly Queue<Experience> buffer = new();

        public ReplayBuffer(int capacity) => this.capacity = capacity;
        public int Count => buffer.Count;
        public void Add(Experience exp)
        {
            if (buffer.Count >= capacity)
                buffer.Dequeue();
            buffer.Enqueue(exp);
        }

        public (float[][] States, long[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones) Sample(int batchSize)
        {
            var rnd = new Random();
            var experiences = buffer.OrderBy(_ => rnd.Next()).Take(batchSize).ToArray();

            return (
                experiences.Select(e => e.State).ToArray(),
                experiences.Select(e => e.Action).ToArray(),
                experiences.Select(e => e.Reward).ToArray(),
                experiences.Select(e => e.NextState).ToArray(),
                experiences.Select(e => e.Done).ToArray()
            );
        }

        public IEnumerator<Experience> GetEnumerator() => buffer.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => buffer.GetEnumerator();
        public void Clear() => buffer.Clear();
        public List<Experience> ToList() => buffer.ToList();
    }
}
