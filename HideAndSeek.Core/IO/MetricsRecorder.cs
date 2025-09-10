using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HideAndSeek.Core.Config;

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

        // Last snapshots kept in-memory for quick web serving
        private volatile TrainingSnapshot _lastTraining = new TrainingSnapshot();
        private volatile EpisodeSnapshot _lastEpisode = new EpisodeSnapshot();

        private void ResetSnapshotsToDefaults()
        {
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var cfg = GameConfig.Instance;
                float eps = 0f, beta = 0f;
                try { eps = cfg.DQN.EpsilonStart; } catch { eps = 0f; }
                try { beta = cfg.ReplayBuffer.BetaStart; } catch { beta = 0f; }
                _lastTraining = new TrainingSnapshot
                {
                    Ts = ts,
                    Step = 0,
                    Epsilon = eps,
                    Beta = beta,
                    Buffer = 0,
                    EmaLoss = 0f,
                    QMean = 0f,
                    QMax = 0f,
                };
                _lastEpisode = new EpisodeSnapshot
                {
                    Ts = ts,
                    TotalSession = 0,
                    SessionTime = 0f,
                    Caught = false,
                    VisibilityRatio = 0f,
                    AvgDistance = 0f,
                    SeekerPhysical = 0,
                    SeekerVisual = 0,
                    SeekerTotal = 0,
                    AccSeekerReward = 0f,
                    AccHiderReward = 0f,
                };
            }
            catch { /* safe defaults */ }
        }

        public void ClearAllData()
        {
            // Delete all known dashboard log files and reset in-memory snapshots.
            lock (_lock)
            {
                try { if (System.IO.File.Exists(_trainCsv)) System.IO.File.Delete(_trainCsv); } catch { }
                try { if (System.IO.File.Exists(_trainJsonl)) System.IO.File.Delete(_trainJsonl); } catch { }
                try { if (System.IO.File.Exists(_promFile)) System.IO.File.Delete(_promFile); } catch { }
                try { if (System.IO.File.Exists(_episodeCsv)) System.IO.File.Delete(_episodeCsv); } catch { }
                try { if (System.IO.File.Exists(_episodeJsonl)) System.IO.File.Delete(_episodeJsonl); } catch { }
                try { if (System.IO.File.Exists(_episodeProm)) System.IO.File.Delete(_episodeProm); } catch { }

                // Recreate CSV headers so subsequent appends work seamlessly
                EnsureCsvHeader();

                // Reset snapshots so web immediately reflects cleared state
                ResetSnapshotsToDefaults();

                // Seed JSONL files with a single point so charts always have at least one data point after reset
                try
                {
                    File.WriteAllText(_trainJsonl, string.Empty, Encoding.UTF8);
                    File.WriteAllText(_episodeJsonl, string.Empty, Encoding.UTF8);

                    var t = _lastTraining ?? new TrainingSnapshot { Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Step = 0, Epsilon = 0f, Beta = 0f, Buffer = 0, EmaLoss = 0f, QMean = 0f, QMax = 0f };
                    var e = _lastEpisode ?? new EpisodeSnapshot { Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TotalSession = 0, SessionTime = 0f, Caught = false, VisibilityRatio = 0f, AvgDistance = 0f, SeekerPhysical = 0, SeekerVisual = 0, SeekerTotal = 0, AccSeekerReward = 0f, AccHiderReward = 0f };

                    var tJson = "{\"ts\":" + t.Ts + ",\"step\":" + t.Step + ",\"epsilon\":" + t.Epsilon.ToString(CultureInfo.InvariantCulture) + ",\"beta\":" + t.Beta.ToString(CultureInfo.InvariantCulture) + ",\"buffer\":" + t.Buffer + ",\"ema_loss\":" + t.EmaLoss.ToString(CultureInfo.InvariantCulture) + ",\"q_mean\":" + t.QMean.ToString(CultureInfo.InvariantCulture) + ",\"q_max\":" + t.QMax.ToString(CultureInfo.InvariantCulture) + " }";
                    var eJson = "{\"ts\":" + e.Ts + ",\"total_session\":" + e.TotalSession + ",\"session_time\":" + e.SessionTime.ToString(CultureInfo.InvariantCulture) + ",\"caught\":" + (e.Caught ? "true" : "false") + ",\"visibility_ratio\":" + e.VisibilityRatio.ToString(CultureInfo.InvariantCulture) + ",\"avg_distance\":" + e.AvgDistance.ToString(CultureInfo.InvariantCulture) + ",\"seeker_physical\":" + e.SeekerPhysical + ",\"seeker_visual\":" + e.SeekerVisual + ",\"seeker_total\":" + e.SeekerTotal + ",\"acc_seeker_reward\":" + e.AccSeekerReward.ToString(CultureInfo.InvariantCulture) + ",\"acc_hider_reward\":" + e.AccHiderReward.ToString(CultureInfo.InvariantCulture) + " }";

                    File.AppendAllText(_trainJsonl, tJson + "\n", Encoding.UTF8);
                    File.AppendAllText(_episodeJsonl, eJson + "\n", Encoding.UTF8);
                }
                catch { }
            }
        }

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

            // Initialize in-memory snapshots so /metrics.json has data immediately
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var cfg = GameConfig.Instance;
                float eps = 0f, beta = 0f;
                try { eps = cfg.DQN.EpsilonStart; } catch { eps = 0f; }
                try { beta = cfg.ReplayBuffer.BetaStart; } catch { beta = 0f; }
                _lastTraining = new TrainingSnapshot
                {
                    Ts = ts,
                    Step = 0,
                    Epsilon = eps,
                    Beta = beta,
                    Buffer = 0,
                    EmaLoss = 0f,
                    QMean = 0f,
                    QMax = 0f,
                };
                _lastEpisode = new EpisodeSnapshot
                {
                    Ts = ts,
                    TotalSession = 0,
                    SessionTime = 0f,
                    Caught = false,
                    VisibilityRatio = 0f,
                    AvgDistance = 0f,
                    SeekerPhysical = 0,
                    SeekerVisual = 0,
                    SeekerTotal = 0,
                    AccSeekerReward = 0f,
                    AccHiderReward = 0f,
                };
            }
            catch { /* safe defaults */ }
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
            _lastTraining = new TrainingSnapshot
            {
                Ts = ts,
                Step = step,
                Epsilon = epsilon,
                Beta = beta,
                Buffer = bufferCount,
                EmaLoss = emaLoss,
                QMean = qMean,
                QMax = qMax
            };
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
            _lastEpisode = new EpisodeSnapshot
            {
                Ts = ts,
                TotalSession = totalSession,
                SessionTime = sessionTime,
                Caught = caught,
                VisibilityRatio = visibilityRatio,
                AvgDistance = avgDistance,
                SeekerPhysical = seekerPhysical,
                SeekerVisual = seekerVisual,
                SeekerTotal = seekerTotal,
                AccSeekerReward = accSeekerReward,
                AccHiderReward = accHiderReward
            };
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
        public MetricsWebSnapshot GetWebSnapshot(int lastTrainingPoints = 200, int lastEpisodePoints = 200)
        {
            // Build snapshot with last points by reading tails of jsonl files (best-effort)
            var snap = new MetricsWebSnapshot
            {
                LatestTraining = _lastTraining,
                LatestEpisode = _lastEpisode,
                Device = TryGetDevice(),
                Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LogsDir = _logsDir
            };

            try { snap.TrainingSeries = TailJsonl(_trainJsonl, lastTrainingPoints); } catch { snap.TrainingSeries = new List<Dictionary<string, object?>>(); }
            try { snap.EpisodeSeries = TailJsonl(_episodeJsonl, lastEpisodePoints); } catch { snap.EpisodeSeries = new List<Dictionary<string, object?>>(); }

            // If files are empty or not yet created, synthesize a single-point series from latest snapshots
            if (snap.TrainingSeries == null || snap.TrainingSeries.Count == 0)
            {
                var t = _lastTraining;
                snap.TrainingSeries = new List<Dictionary<string, object?>>()
                {
                    new Dictionary<string, object?>
                    {
                        ["ts"] = t?.Ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["step"] = t?.Step ?? 0,
                        ["epsilon"] = t?.Epsilon ?? 0f,
                        ["beta"] = t?.Beta ?? 0f,
                        ["buffer"] = t?.Buffer ?? 0,
                        ["ema_loss"] = t?.EmaLoss ?? 0f,
                        ["q_mean"] = t?.QMean ?? 0f,
                        ["q_max"] = t?.QMax ?? 0f,
                    }
                };
            }
            if (snap.EpisodeSeries == null || snap.EpisodeSeries.Count == 0)
            {
                var e = _lastEpisode;
                snap.EpisodeSeries = new List<Dictionary<string, object?>>()
                {
                    new Dictionary<string, object?>
                    {
                        ["ts"] = e?.Ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["total_session"] = e?.TotalSession ?? 0,
                        ["session_time"] = e?.SessionTime ?? 0f,
                        ["caught"] = e?.Caught ?? false,
                        ["visibility_ratio"] = e?.VisibilityRatio ?? 0f,
                        ["avg_distance"] = e?.AvgDistance ?? 0f,
                        ["seeker_physical"] = e?.SeekerPhysical ?? 0,
                        ["seeker_visual"] = e?.SeekerVisual ?? 0,
                        ["seeker_total"] = e?.SeekerTotal ?? 0,
                        ["acc_seeker_reward"] = e?.AccSeekerReward ?? 0f,
                        ["acc_hider_reward"] = e?.AccHiderReward ?? 0f,
                    }
                };
            }

            return snap;
        }

        private static string TryGetDevice()
        {
            try { return TorchSharp.torch.cuda.is_available() ? "CUDA" : "CPU"; } catch { return "Unknown"; }
        }

        private static List<Dictionary<string, object?>> TailJsonl(string path, int maxLines)
        {
            var list = new List<Dictionary<string, object?>>();
            if (!File.Exists(path) || maxLines <= 0) return list;

            // Read all lines if small, else simple tail by reverse scanning
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int start = Math.Max(0, lines.Length - maxLines);
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(line);
                    if (dict != null) list.Add(dict);
                }
                catch { }
            }
            return list;
        }
    }

    public record TrainingSnapshot
    {
        public long Ts { get; set; }
        public long Step { get; set; }
        public float Epsilon { get; set; }
        public float Beta { get; set; }
        public int Buffer { get; set; }
        public float EmaLoss { get; set; }
        public float QMean { get; set; }
        public float QMax { get; set; }
    }

    public record EpisodeSnapshot
    {
        public long Ts { get; set; }
        public int TotalSession { get; set; }
        public float SessionTime { get; set; }
        public bool Caught { get; set; }
        public float VisibilityRatio { get; set; }
        public float AvgDistance { get; set; }
        public int SeekerPhysical { get; set; }
        public int SeekerVisual { get; set; }
        public int SeekerTotal { get; set; }
        public float AccSeekerReward { get; set; }
        public float AccHiderReward { get; set; }
    }

    public class MetricsWebSnapshot
    {
        public TrainingSnapshot LatestTraining { get; set; } = new TrainingSnapshot();
        public EpisodeSnapshot LatestEpisode { get; set; } = new EpisodeSnapshot();
        public List<Dictionary<string, object?>> TrainingSeries { get; set; } = new();
        public List<Dictionary<string, object?>> EpisodeSeries { get; set; } = new();
        public long Now { get; set; }
        public string Device { get; set; } = "Unknown";
        public string LogsDir { get; set; } = string.Empty;
    }
}
