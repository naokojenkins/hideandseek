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
                var cfg = GameConfig.Instance;
                if (cfg?.Training != null && !string.IsNullOrWhiteSpace(cfg.Training.DataRoot))
                    root = cfg.Training.DataRoot;
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
            string dir = Combine(GetDataRoot(), "configs");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetConfigPath(string fileName = "game_config.json")
        {
            if (System.IO.Path.IsPathFullyQualified(fileName))
            {
                string abs = Normalize(fileName);
                if (File.Exists(abs)) return abs;
            }

            // 1) Check under DataRoot/configs
            try
            {
                string underConfigs = Combine(GetConfigsDirectory(), fileName);
                if (File.Exists(underConfigs)) return underConfigs;
            }
            catch { }

            // 2) Try base directory
            string candidate = Combine(BaseDirectory, fileName);
            if (File.Exists(candidate)) return candidate;

            // 3) Fallback to working directory as given
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
