using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HideAndSeek.Core.IO
{
    /// <summary>
    /// Very lightweight runtime metrics recorder.
    /// - Writes CSV and JSONL files under logs directory
    /// - Optionally exports latest metrics in Prometheus exposition format
    /// Thread-safe for simple append scenarios.
    /// </summary>
    public sealed class MetricsRecorder
    {
        private static readonly object _lock = new object();
        private static MetricsRecorder? _instance;
        public static MetricsRecorder Instance => _instance ??= new MetricsRecorder();

        private readonly string _logsDir;
        private readonly string _trainCsv;
        private readonly string _trainJsonl;
        private readonly string _promFile;
        private readonly string _episodeCsv;
        private readonly string _episodeJsonl;
        private readonly string _episodeProm;

        private MetricsRecorder()
        {
            _logsDir = PathService.GetLogsDirectory();
            _trainCsv = Path.Combine(_logsDir, "training_metrics.csv");
            _trainJsonl = Path.Combine(_logsDir, "training_metrics.jsonl");
            _promFile = Path.Combine(_logsDir, "training_metrics.prom");
            _episodeCsv = Path.Combine(_logsDir, "episode_metrics.csv");
            _episodeJsonl = Path.Combine(_logsDir, "episode_metrics.jsonl");
            _episodeProm = Path.Combine(_logsDir, "episode_metrics.prom");
            EnsureCsvHeader();
        }

        private void EnsureCsvHeader()
        {
            if (!File.Exists(_trainCsv))
            {
                File.WriteAllText(_trainCsv,
                    "timestamp,step,epsilon,beta,buffer,ema_loss,q_mean,q_max\n", Encoding.UTF8);
            }
            if (!File.Exists(_episodeCsv))
            {
                File.WriteAllText(_episodeCsv,
                    "timestamp,total_session,session_time,caught,visibility_ratio,avg_distance,seeker_physical,seeker_visual,seeker_total,acc_seeker_reward,acc_hider_reward\n", Encoding.UTF8);
            }
        }

        public void RecordTraining(long step, float epsilon, float beta, int bufferCount, float emaLoss, float qMean, float qMax)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var csvLine = string.Join(',', new[]
            {
                ts.ToString(CultureInfo.InvariantCulture),
                step.ToString(CultureInfo.InvariantCulture),
                epsilon.ToString(CultureInfo.InvariantCulture),
                beta.ToString(CultureInfo.InvariantCulture),
                bufferCount.ToString(CultureInfo.InvariantCulture),
                emaLoss.ToString(CultureInfo.InvariantCulture),
                qMean.ToString(CultureInfo.InvariantCulture),
                qMax.ToString(CultureInfo.InvariantCulture)
            });

            var json = $"{{\"ts\":{ts},\"step\":{step},\"epsilon\":{epsilon.ToString(CultureInfo.InvariantCulture)},\"beta\":{beta.ToString(CultureInfo.InvariantCulture)},\"buffer\":{bufferCount},\"ema_loss\":{emaLoss.ToString(CultureInfo.InvariantCulture)},\"q_mean\":{qMean.ToString(CultureInfo.InvariantCulture)},\"q_max\":{qMax.ToString(CultureInfo.InvariantCulture)} }}";

            lock (_lock)
            {
                File.AppendAllText(_trainCsv, csvLine + "\n", Encoding.UTF8);
                File.AppendAllText(_trainJsonl, json + "\n", Encoding.UTF8);
                // Prometheus snapshot (overwrite latest)
                var sb = new StringBuilder();
                sb.AppendLine($"# HELP dqn_training_step Current training step");
                sb.AppendLine($"# TYPE dqn_training_step gauge");
                sb.AppendLine($"dqn_training_step {step.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_epsilon {epsilon.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_beta {beta.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_buffer_size {bufferCount.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_ema_loss {emaLoss.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_q_mean {qMean.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_training_q_max {qMax.ToString(CultureInfo.InvariantCulture)}");
                File.WriteAllText(_promFile, sb.ToString(), Encoding.UTF8);
            }
        }

        public void RecordEpisode(
            int totalSession,
            float sessionTime,
            bool caught,
            float visibilityRatio,
            float avgDistance,
            int seekerPhysical,
            int seekerVisual,
            int seekerTotal,
            float accSeekerReward,
            float accHiderReward)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var csvLine = string.Join(',', new[]
            {
                ts.ToString(CultureInfo.InvariantCulture),
                totalSession.ToString(CultureInfo.InvariantCulture),
                sessionTime.ToString(CultureInfo.InvariantCulture),
                (caught ? 1 : 0).ToString(CultureInfo.InvariantCulture),
                visibilityRatio.ToString(CultureInfo.InvariantCulture),
                avgDistance.ToString(CultureInfo.InvariantCulture),
                seekerPhysical.ToString(CultureInfo.InvariantCulture),
                seekerVisual.ToString(CultureInfo.InvariantCulture),
                seekerTotal.ToString(CultureInfo.InvariantCulture),
                accSeekerReward.ToString(CultureInfo.InvariantCulture),
                accHiderReward.ToString(CultureInfo.InvariantCulture)
            });

            var json = $"{{\"ts\":{ts},\"total_session\":{totalSession},\"session_time\":{sessionTime.ToString(CultureInfo.InvariantCulture)},\"caught\":{(caught ? "true" : "false")},\"visibility_ratio\":{visibilityRatio.ToString(CultureInfo.InvariantCulture)},\"avg_distance\":{avgDistance.ToString(CultureInfo.InvariantCulture)},\"seeker_physical\":{seekerPhysical},\"seeker_visual\":{seekerVisual},\"seeker_total\":{seekerTotal},\"acc_seeker_reward\":{accSeekerReward.ToString(CultureInfo.InvariantCulture)},\"acc_hider_reward\":{accHiderReward.ToString(CultureInfo.InvariantCulture)} }}";

            lock (_lock)
            {
                File.AppendAllText(_episodeCsv, csvLine + "\n", Encoding.UTF8);
                File.AppendAllText(_episodeJsonl, json + "\n", Encoding.UTF8);
                var sb = new StringBuilder();
                sb.AppendLine($"# HELP dqn_episode_total_sessions Total sessions completed");
                sb.AppendLine($"# TYPE dqn_episode_total_sessions counter");
                sb.AppendLine($"dqn_episode_total_sessions {totalSession.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_time_seconds {sessionTime.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_caught {(caught ? 1 : 0).ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_visibility_ratio {visibilityRatio.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_avg_distance {avgDistance.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_acc_seeker_reward {accSeekerReward.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"dqn_episode_last_acc_hider_reward {accHiderReward.ToString(CultureInfo.InvariantCulture)}");
                File.WriteAllText(_episodeProm, sb.ToString(), Encoding.UTF8);
            }
        }
    }
}
