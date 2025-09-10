using System;
using System.IO;
using HideAndSeek.Core.Config;

namespace HideAndSeek.Core.IO
{
    /// <summary>
    /// Centralized service for building and validating file system paths in a cross-platform way.
    /// </summary>
    public static class PathService
    {
        // Helper avoids direct GameConfig.Instance access to prevent recursion during bootstrap.
        private static class GameConfigAccessor
        {
            public static bool TryGetTrainingDataRoot(out string? dataRoot)
            {
                try
                {
                    var cfg = HideAndSeek.Core.Config.GameConfig.Instance; // may throw during early bootstrap
                    dataRoot = cfg?.Training?.DataRoot;
                    return true;
                }
                catch
                {
                    dataRoot = null;
                    return false;
                }
            }
        }
        private static string? _baseDir;

        /// <summary>
        /// Base directory for app-relative paths. Defaults to AppContext.BaseDirectory.
        /// </summary>
        public static string BaseDirectory
        {
            get => _baseDir ?? AppContext.BaseDirectory;
            set => _baseDir = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is null or empty", nameof(path));
            return Path.GetFullPath(path);
        }

        public static string Combine(params string[] parts)
        {
            if (parts == null || parts.Length == 0) throw new ArgumentException("No path parts provided", nameof(parts));
            string result = parts[0] ?? string.Empty;
            for (int i = 1; i < parts.Length; i++)
                result = Path.Combine(result, parts[i] ?? string.Empty);
            return Normalize(result);
        }

        /// <summary>
        /// Returns the configured data root directory.
        /// If Training.DataRoot is absolute, it's used as is; otherwise resolved under BaseDirectory.
        /// Ensures the directory exists.
        /// </summary>
        public static string GetDataRoot()
        {
            string root = ".";
            try
            {
                // Avoid touching GameConfig during early bootstrap to prevent recursion.
                // If GameConfig.Instance throws or is not yet initialized, fall back to BaseDirectory + ".".
                if (GameConfigAccessor.TryGetTrainingDataRoot(out var cfgRoot) && !string.IsNullOrWhiteSpace(cfgRoot))
                    root = cfgRoot!;
            }
            catch { }

            string dir = System.IO.Path.IsPathFullyQualified(root) ? Normalize(root) : Combine(BaseDirectory, root);
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetLogsDirectory()
        {
            string logsPath = "logs";
            try
            {
                var cfg = GameConfig.Instance;
                if (cfg?.Training != null && !string.IsNullOrWhiteSpace(cfg.Training.LogsPath))
                    logsPath = cfg.Training.LogsPath;
            }
            catch { }

            string baseRoot = GetDataRoot();
            string dir = System.IO.Path.IsPathFullyQualified(logsPath) ? Normalize(logsPath) : Combine(baseRoot, logsPath);
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetLogsFilePath(string fileNamePattern = "app-.log")
        {
            string dir = GetLogsDirectory();
            return Combine(dir, fileNamePattern);
        }

        public static string GetModelsDirectory()
        {
            string modelsPath = "models";
            try
            {
                var cfg = GameConfig.Instance;
                if (cfg?.Training != null && !string.IsNullOrWhiteSpace(cfg.Training.ModelsPath))
                    modelsPath = cfg.Training.ModelsPath;
            }
            catch { }

            string baseRoot = GetDataRoot();
            string dir = System.IO.Path.IsPathFullyQualified(modelsPath) ? Normalize(modelsPath) : Combine(baseRoot, modelsPath);
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetQtablesDirectory()
        {
            string dir = Combine(GetDataRoot(), "qtables");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetConfigsDirectory()
        {
            // Avoid triggering GameConfig during early bootstrap; build under BaseDirectory by default.
            string baseRoot;
            try
            {
                if (GameConfigAccessor.TryGetTrainingDataRoot(out var cfgRoot) && !string.IsNullOrWhiteSpace(cfgRoot))
                    baseRoot = System.IO.Path.IsPathFullyQualified(cfgRoot!) ? Normalize(cfgRoot!) : Combine(BaseDirectory, cfgRoot!);
                else
                    baseRoot = BaseDirectory;
            }
            catch { baseRoot = BaseDirectory; }

            string dir = Combine(baseRoot, "configs");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetConfigPath(string fileName = "game_config.json")
        {
            // 0) Absolute path as-is
            if (System.IO.Path.IsPathFullyQualified(fileName))
            {
                string abs = Normalize(fileName);
                if (File.Exists(abs)) return abs;
            }

            // 1) Strongly prefer repository source: search upwards from CurrentDirectory for HideAndSeek.Sim/configs
            try
            {
                string? dir = Normalize(Directory.GetCurrentDirectory());
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    // A. .../HideAndSeek.Sim/configs/file
                    string candidateSimConfigs = Combine(dir, "HideAndSeek.Sim", "configs", fileName);
                    if (File.Exists(candidateSimConfigs)) return candidateSimConfigs;

                    // B. If current dir itself is HideAndSeek.Sim or configs under it
                    string dirName = new DirectoryInfo(dir).Name;
                    if (string.Equals(dirName, "HideAndSeek.Sim", StringComparison.OrdinalIgnoreCase))
                    {
                        string hereFile = Combine(dir, fileName);
                        if (File.Exists(hereFile)) return hereFile;
                        string hereConfigs = Combine(dir, "configs", fileName);
                        if (File.Exists(hereConfigs)) return hereConfigs;
                    }

                    dir = Directory.GetParent(dir)?.FullName;
                }
            }
            catch { }

            // 2) Also try upwards from BaseDirectory for repository source
            try
            {
                string? dir = BaseDirectory;
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    string candidateSimConfigs = Combine(dir, "HideAndSeek.Sim", "configs", fileName);
                    if (File.Exists(candidateSimConfigs)) return candidateSimConfigs;

                    string candidateSim = Combine(dir, "HideAndSeek.Sim", fileName);
                    if (File.Exists(candidateSim)) return candidateSim;

                    string dirName = new DirectoryInfo(dir).Name;
                    if (string.Equals(dirName, "HideAndSeek.Sim", StringComparison.OrdinalIgnoreCase))
                    {
                        string here = Combine(dir, fileName);
                        if (File.Exists(here)) return here;
                        string hereConfigs = Combine(dir, "configs", fileName);
                        if (File.Exists(hereConfigs)) return hereConfigs;
                    }

                    dir = Directory.GetParent(dir)?.FullName;
                }
            }
            catch { }

            // 3) Try current working directory and ./configs (bin copies)
            try
            {
                string cwd = Normalize(Directory.GetCurrentDirectory());
                string inCwdConfigs = Combine(cwd, "configs", fileName);
                if (File.Exists(inCwdConfigs)) return inCwdConfigs;
                string inCwd = Combine(cwd, fileName);
                if (File.Exists(inCwd)) return inCwd;
            }
            catch { }

            // 4) Try base directory and base/configs (bin copies)
            string candidate = Combine(BaseDirectory, fileName);
            if (File.Exists(candidate)) return candidate;
            string candidateInBaseConfigs = Combine(BaseDirectory, "configs");
            candidateInBaseConfigs = Combine(candidateInBaseConfigs, fileName);
            if (File.Exists(candidateInBaseConfigs)) return candidateInBaseConfigs;

            // 5) Fallback: return normalized provided path (may not exist)
            string fallback = Normalize(fileName);
            return fallback;
        }

        public static void EnsureDirectoryExists(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) throw new ArgumentException("Directory is null or empty", nameof(dir));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Performs a basic write permission check by attempting to open a temporary file.
        /// </summary>
        public static bool CanWriteToDirectory(string dir)
        {
            try
            {
                EnsureDirectoryExists(dir);
                string testFile = Path.Combine(dir, ".perm_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var fs = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.ASCII.GetBytes("ok");
                    fs.Write(bytes, 0, bytes.Length);
                }
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
