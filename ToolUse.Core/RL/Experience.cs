// using System;
// using System.Collections.Generic;
// using System.Linq;
//
// namespace ToolUse.Core.RL
// {
//     public class Experience
//     {
//         public float[] State { get; }
//         public int Action { get; }
//         public float Reward { get; }
//         public float[] NextState { get; }
//         public bool Done { get; }
//
//         public Experience(float[] state, int action, float reward, float[] nextState, bool done)
//         {
//             State = state ?? throw new ArgumentNullException(nameof(state));
//             Action = action;
//             Reward = reward;
//             NextState = nextState ?? throw new ArgumentNullException(nameof(nextState));
//             Done = done;
//         }
//     }
//
//     public class ReplayBuffer
//     {
//         private readonly int maxSize;
//         private readonly List<Experience> buffer;
//         private readonly Random rnd = new();
//
//         public ReplayBuffer(int maxSize)
//         {
//             this.maxSize = maxSize;
//             buffer = new List<Experience>(maxSize);
//         }
//
//         public int Count => buffer.Count;
//
//         public void Add(Experience exp)
//         {
//             if (buffer.Count >= maxSize)
//                 buffer.RemoveAt(0);
//             buffer.Add(exp);
//         }
//
//         /// <summary>
//         /// Возвращает батч из буфера, подготовленный для TorchSharp DQN.
//         /// </summary>
//         public (float[][] States, int[] Actions, float[] Rewards, float[][] NextStates, bool[] Dones) Sample(int batchSize)
//         {
//             // Защита: если буфер еще не заполнен, возвращаем сколько есть
//             int count = Math.Min(batchSize, buffer.Count);
//             // Быстрая случайная выборка
//             var indices = Enumerable.Range(0, buffer.Count).OrderBy(_ => rnd.Next()).Take(count).ToArray();
//             var sample = indices.Select(i => buffer[i]).ToArray();
//
//             float[][] states = sample.Select(e => e.State).ToArray();
//             int[] actions = sample.Select(e => e.Action).ToArray();
//             float[] rewards = sample.Select(e => e.Reward).ToArray();
//             float[][] nextStates = sample.Select(e => e.NextState).ToArray();
//             bool[] dones = sample.Select(e => e.Done).ToArray();
//
//             return (states, actions, rewards, nextStates, dones);
//         }
//     }
// }
