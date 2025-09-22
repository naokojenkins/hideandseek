# References

Core RL algorithms
- Mnih, V. et al. (2015). Human-level control through deep reinforcement learning. Nature.
- Schaul, T., Quan, J., Antonoglou, I., & Silver, D. (2016). Prioritized Experience Replay. ICLR.
- Van Hasselt, H., Guez, A., & Silver, D. (2016). Deep Reinforcement Learning with Double Q-learning. AAAI.
- Hessel, M. et al. (2018). Rainbow: Combining Improvements in Deep RL. AAAI.
- Lillicrap, T. P. et al. (2016). Continuous control with deep reinforcement learning. ICLR (for Polyak/target update commonality).

Stability and practical tips
- Fujimoto, S., van Hoof, H., & Meger, D. (2018). Addressing Function Approximation Error in Actor-Critic Methods. ICML (target networks and clipped critics context).
- Zhang, S., & Sutton, R. S. (2017). A Deeper Look at Experience Replay. arXiv.

Libraries and ecosystem
- TorchSharp (PyTorch for .NET): https://github.com/dotnet/TorchSharp

Hide & Seek environment shaping
- No canonical paper; the environment here is bespoke. Reward shaping and visibility mechanics are documented in GameConfig.AgentConfig comments.
