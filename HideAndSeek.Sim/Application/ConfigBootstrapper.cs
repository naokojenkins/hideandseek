using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using HideAndSeek.Core.Config;

namespace ToolUse.Sim.Application
{
    /// <summary>
    /// Builds configuration from multiple sources and binds to GameConfig.
    /// Sources (lowest to highest precedence):
    /// - appsettings.json (optional)
    /// - game_config.json (optional)
    /// - Environment variables with prefix HNS_ (e.g., HNS_GameConfig__Seed or HNS_Seed)
    /// - Command line (supports standard 'Section:Key' and convenience switches like --seed, --device, --batchSize)
    /// </summary>
    public static class ConfigBootstrapper
    {
        public static void Initialize(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            // Resolve game config path using PathService to allow running without rebuild
            string requested = GameConfig.ConfigPath ?? "game_config.json";
            string resolved = HideAndSeek.Core.IO.PathService.GetConfigPath(requested);
            builder.AddJsonFile(resolved, optional: true, reloadOnChange: false);

            // Also try to overlay from a nearby agents_config.json if present (handled below via AgentsConfig.Load)

            builder.AddEnvironmentVariables(prefix: "HNS_");

            // Map convenience command-line switches to full keys via in-memory provider first
            var mapped = MapFriendlyArgs(args);
            if (mapped.Count > 0)
                builder.AddInMemoryCollection(mapped);

            // Also add raw command-line provider for full key syntax
            builder.AddCommandLine(args);

            var configuration = builder.Build();

            // Bind into a new GameConfig instance; because GameConfig is nested, we can bind directly
            var cfg = new GameConfig();
            configuration.Bind(cfg);

            // Overlay Agents config from separate file if present (takes precedence over game_config.json for agent sections)
            var agents = AgentsConfig.Load();
            if (agents != null)
            {
                cfg.Seeker = agents.Seeker ?? cfg.Seeker;
                cfg.Hider = agents.Hider ?? cfg.Hider;
            }

            // If Training.Seed not provided, propagate from root Seed
            if (cfg.Training.Seed == null)
                cfg.Training.Seed = cfg.Seed;

            // Validate
            var errors = cfg.Validate();
            if (errors.Length > 0)
            {
                Console.Error.WriteLine("[CONFIG VALIDATION FAILED]");
                foreach (var e in errors)
                    Console.Error.WriteLine(" - " + e);
                throw new ArgumentException("Invalid configuration. Please fix errors above.");
            }

            // Set singleton for legacy access
            GameConfig.SetInstance(cfg);
        }

        private static Dictionary<string, string?> MapFriendlyArgs(string[] args)
        {
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in args)
            {
                if (!a.StartsWith("--")) continue;
                var trimmed = a.Substring(2);
                var parts = trimmed.Split('=', 2);
                var key = parts[0];
                var value = parts.Length > 1 ? parts[1] : "true"; // allow flags

                switch (key)
                {
                    case "seed":
                        map["Training:Seed"] = value; // primary
                        map["Seed"] = value; // keep in sync with root for legacy
                        break;
                    case "device":
                        map["Training:Device"] = value;
                        break;
                    case "batchSize":
                        map["Training:BatchSize"] = value;
                        map["DQN:BatchSize"] = value; // legacy mapping
                        break;
                    case "stepsPerUpdate":
                        map["Training:StepsPerUpdate"] = value;
                        map["DQN:StepsPerUpdate"] = value;
                        break;
                    case "dataRoot":
                        map["Training:DataRoot"] = value;
                        break;
                    case "modelsPath":
                        map["Training:ModelsPath"] = value;
                        break;
                    case "logsPath":
                        map["Training:LogsPath"] = value;
                        break;
                    case "configPath":
                        GameConfig.ConfigPath = value ?? GameConfig.ConfigPath;
                        break;
                }
            }
            return map;
        }
    }
}
