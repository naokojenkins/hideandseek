using System;
using System.IO;
using System.Linq;
using HideAndSeek.Core.Config;
using HideAndSeek.Core.RaylibThreeD;
using HideAndSeek.Core.RL;
using Newtonsoft.Json;

namespace HideAndSeek.Core.IO
{
    /// <summary>
    /// Versioned checkpoint manager for saving/loading agent states atomically with retention.
    /// </summary>
    public static class CheckpointManager
    {
        public const int CurrentVersion = 1;

        private static string GetRootDir() => PathService.GetModelsDirectory();

        private static string CreateCheckpointDir()
        {
            string root = GetRootDir();
            string name = $"ckpt-{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void SaveAgents(DQNAgent seeker, DQNAgent hider, object? meta = null, int keepLast = 5)
        {
            string root = GetRootDir();
            if (!PathService.CanWriteToDirectory(root))
            {
                try { System.Console.Error.WriteLine($"[WARN] Checkpoint save skipped: no write permission to {root}"); } catch { }
                return;
            }

            string tempDir = Path.Combine(root, ".tmp_ckpt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string seekerW = Path.Combine(tempDir, "seeker.pt");
            string seekerS = Path.Combine(tempDir, "seeker_state.json");
            string hiderW  = Path.Combine(tempDir,  "hider.pt");
            string hiderS  = Path.Combine(tempDir,  "hider_state.json");
            string metaPath = Path.Combine(tempDir, "checkpoint.json");

            // save
            seeker.SaveAll(seekerW, seekerS);
            hider.SaveAll(hiderW, hiderS);

            var checkpointMeta = new
            {
                version = CurrentVersion,
                timestamp = DateTime.UtcNow,
                seed = GameConfig.Instance.Seed,
                trainingSeed = GameConfig.Instance.Training?.Seed,
                sessionTotal = Simulation3D.TotalSessions,
                meta
            };
            File.WriteAllText(metaPath, JsonConvert.SerializeObject(checkpointMeta, Formatting.Indented), System.Text.Encoding.UTF8);

            // move atomically: create final dir and then move files
            string finalDir = CreateCheckpointDir();
            foreach (var f in Directory.GetFiles(tempDir))
            {
                string dest = Path.Combine(finalDir, Path.GetFileName(f));
                File.Move(f, dest, overwrite: true);
            }
            Directory.Delete(tempDir, recursive: true);

            // retention
            try
            {
                var dirs = Directory.GetDirectories(root, "ckpt-*")
                    .OrderByDescending(d => d)
                    .ToList();
                for (int i = keepLast; i < dirs.Count; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
        }

        public static bool LoadLatest(DQNAgent seeker, DQNAgent hider)
        {
            try
            {
                var latest = Directory.GetDirectories(GetRootDir(), "ckpt-*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                if (latest == null) return false;

                string seekerW = Path.Combine(latest, "seeker.pt");
                string seekerS = Path.Combine(latest, "seeker_state.json");
                string hiderW  = Path.Combine(latest,  "hider.pt");
                string hiderS  = Path.Combine(latest,  "hider_state.json");

                seeker.LoadAll(seekerW, seekerS);
                hider.LoadAll(hiderW, hiderS);
                return true;
            }
            catch { return false; }
        }
    }
}
