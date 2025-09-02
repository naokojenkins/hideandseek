using System;
using System.IO;
using HideAndSeek.Core.RaylibThreeD;

namespace HideAndSeek.Core.IO
{
    /// <summary>
    /// Utilities to back up learning data and reset learning progress.
    /// </summary>
    public static class LearningDataReset
    {
        /// <summary>
        /// Creates/overwrites a backup folder and copies learning data there (models, qtables, logs),
        /// then resets the global session counter to 0.
        /// </summary>
        public static void BackupLearningDataAndResetCounter()
        {
            string modelsDir = string.Empty;
            string qtablesDir = string.Empty;
            string logsDir = string.Empty;
            try { modelsDir = PathService.GetModelsDirectory(); } catch { }
            try { qtablesDir = PathService.GetQtablesDirectory(); } catch { }
            try { logsDir = PathService.GetLogsDirectory(); } catch { }

            // Choose a single backup root under models to keep it simple and portable
            string backupRoot = Path.Combine(!string.IsNullOrWhiteSpace(modelsDir) ? modelsDir : PathService.GetDataRoot(), "backup");

            // Ensure backup root exists and is empty (overwrite semantics)
            SafeRecreateDirectory(backupRoot);

            // Copy known learning folders into backup, if they exist
            if (Directory.Exists(modelsDir))
                SafeCopyDirectory(modelsDir, Path.Combine(backupRoot, "models"));
            if (Directory.Exists(qtablesDir))
                SafeCopyDirectory(qtablesDir, Path.Combine(backupRoot, "qtables"));
            if (Directory.Exists(logsDir))
                SafeCopyDirectory(logsDir, Path.Combine(backupRoot, "logs"));

            // Reset global counter and persist
            try { Simulation3D.ResetTotalSessions(); } catch { }
        }

        private static void SafeRecreateDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    // Delete contents only to avoid issues if 'dir' equals a source folder
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        try { Directory.Delete(sub, true); } catch { }
                    }
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                else
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        private static void SafeCopyDirectory(string sourceDir, string destDir)
        {
            try
            {
                // Avoid copying a directory into itself
                var srcFull = Path.GetFullPath(sourceDir);
                var dstFull = Path.GetFullPath(destDir);
                if (dstFull.StartsWith(srcFull, StringComparison.OrdinalIgnoreCase))
                {
                    // If destination is inside source, skip to prevent recursion
                    return;
                }

                if (!Directory.Exists(sourceDir)) return;
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                // Copy files
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    string destFile = Path.Combine(destDir, Path.GetFileName(file));
                    try { File.Copy(file, destFile, overwrite: true); } catch { }
                }
                // Copy subdirectories
                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    string subName = Path.GetFileName(dir);
                    string destSub = Path.Combine(destDir, subName);
                    SafeCopyDirectory(dir, destSub);
                }
            }
            catch { }
        }
    }
}
