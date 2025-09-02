using System;
using System.IO;
using HideAndSeek.Core.IO;
using Newtonsoft.Json;

namespace HideAndSeek.Core.Config
{
    /// <summary>
    /// Helper to dump effective configuration and seed for traceability.
    /// </summary>
    public static class ConfigDumper
    {
        public static void Dump(GameConfig cfg, int effectiveSeed, string? outputFile = null)
        {
            var payload = new
            {
                Timestamp = DateTimeOffset.Now,
                EffectiveSeed = effectiveSeed,
                Config = cfg
            };

            string json = JsonConvert.SerializeObject(payload, Formatting.Indented);

            // Always write to console
            Console.WriteLine("[CONFIG DUMP]\n" + json);

            try
            {
                string targetFile = outputFile ?? GetDefaultDumpFilePath();
                var dir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(dir)) PathService.EnsureDirectoryExists(dir);
                File.WriteAllText(targetFile, json, System.Text.Encoding.UTF8);
                Console.WriteLine($"[CONFIG DUMP] Saved to: {targetFile}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CONFIG DUMP ERROR] Failed to save file: {ex.Message}");
            }
        }

        private static string GetDefaultDumpFilePath()
        {
            // Reuse logs directory by deriving from existing logs file path
            string logsPath = PathService.GetLogsFilePath("app-.log");
            string? dir = Path.GetDirectoryName(logsPath);
            if (string.IsNullOrEmpty(dir)) dir = PathService.BaseDirectory;
            string fileName = $"config_dump_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            return Path.Combine(dir!, fileName);
        }
    }
}
