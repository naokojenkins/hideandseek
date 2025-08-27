using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using ToolUse.Core.Config;
using ToolUse.Core.RL;
using ToolUse.Sim.Application;
using ToolUse.Sim.Rendering;

namespace ToolUse.Sim
{
    class Program
    {
        static bool useVisualization = true;

        public static void Main(string[] args)
        {
            // Determine subcommand and flags early
            string mode = ParseMode(args); // train | eval | render | help | ""
            bool headlessFlag = ArgsHasFlag(args, "headless");

            // If help requested explicitly, show and exit early
            if (mode == "help")
            {
                PrintHelp();
                return;
            }

            // Configure Serilog sinks (console + rolling file)
            // Avoid accessing GameConfig before bootstrap to prevent recursion
            string defaultLogsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
            ToolUse.Core.IO.PathService.EnsureDirectoryExists(defaultLogsDir);
            var logsPath = System.IO.Path.Combine(defaultLogsDir, "app-.log");
            var logsDir = System.IO.Path.GetDirectoryName(logsPath)!;
            if (!ToolUse.Core.IO.PathService.CanWriteToDirectory(logsDir))
            {
                Console.Error.WriteLine($"[WARN] No write permission to logs directory: {logsDir}. Falling back to console-only logging.");
            }

            var level = ParseLogLevel(args);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("ToolUse", Serilog.Events.LogEventLevel.Debug)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(logsPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(3))
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (sender, a) =>
            {
                Log.Fatal(a.ExceptionObject as Exception, "Unhandled exception: {ExceptionObject}", a.ExceptionObject);
                Log.CloseAndFlush();
            };

            // Build and validate configuration: appsettings.json / game_config.json / env / args
            ConfigBootstrapper.Initialize(args);

            // Reconfigure Serilog sinks based on Training.DataRoot/LogsPath from the loaded config
            try
            {
                string dataRoot = GameConfig.Instance.Training.DataRoot ?? ".";
                string logsRel = GameConfig.Instance.Training.LogsPath ?? "logs";

                string effectiveLogsDir = System.IO.Path.IsPathRooted(logsRel)
                    ? logsRel
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, dataRoot, logsRel));

                ToolUse.Core.IO.PathService.EnsureDirectoryExists(effectiveLogsDir);

                if (ToolUse.Core.IO.PathService.CanWriteToDirectory(effectiveLogsDir))
                {
                    var configuredLogsPath = System.IO.Path.Combine(effectiveLogsDir, "app-.log");
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Is(level)
                        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                        .MinimumLevel.Override("ToolUse", Serilog.Events.LogEventLevel.Debug)
                        .Enrich.FromLogContext()
                        .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                        .WriteTo.File(configuredLogsPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(3))
                        .CreateLogger();

                    Log.Information("Logging reconfigured to: {Path}", configuredLogsPath);
                }
                else
                {
                    Log.Warning("No write permission to configured logs directory: {Dir}. Continuing with console-only logging.", effectiveLogsDir);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to reconfigure logging to Training.LogsPath/DataRoot: {ex.Message}");
            }

            // Initialize global RNGs and log effective seed
            int effectiveSeed = GameConfig.Instance.Training.Seed ?? GameConfig.Instance.Seed;
            Reproducibility.Initialize(effectiveSeed);
            Log.Information("Reproducibility seed initialized: {Seed}", Reproducibility.EffectiveSeed);

            // Provide a config dump command and exit if requested
            if (ArgsHasFlag(args, "dump-config") || ArgsHasFlag(args, "dump"))
            {
                ToolUse.Core.Config.ConfigDumper.Dump(GameConfig.Instance, Reproducibility.EffectiveSeed);
                Log.CloseAndFlush();
                return;
            }

            // Decide visualization based on subcommand or interactive menu
            if (string.IsNullOrEmpty(mode))
            {
                mode = ShowStartupMenu(out useVisualization);
            }
            else
            {
                useVisualization = mode switch
                {
                    "train" => false,
                    "eval" => !headlessFlag,
                    "render" => true,
                    "reset" => false,
                    _ => true
                };
            }

            // DI composition root
            var services = new ServiceCollection();
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddSerilog(dispose: true);
            });

            // Configure device provider using config
            var pref = GameConfig.Instance.Training.Device?.ToLowerInvariant() ?? "auto";
            DevicePreference preference = pref switch
            {
                "cpu" => DevicePreference.Cpu,
                "cuda" => DevicePreference.Cuda,
                _ => DevicePreference.Auto
            };
            var deviceSettings = new DeviceSettings { Preference = preference };
            services.AddSingleton(deviceSettings);
            services.AddSingleton<IDeviceProvider, DeviceProvider>();

            // Register factories
            services.AddSingleton<IOptimizerFactory>(sp =>
            {
                var modelCfg = GameConfig.Instance.Model;
                // Use values from new Model section
                return new AdamOptimizerFactory(modelCfg.LearningRate, modelCfg.WeightDecay);
            });
            services.AddTransient<IReplayBufferFactory, PrioritizedReplayBufferFactory>();

            var provider = services.BuildServiceProvider();

            // If reset was requested, perform backup and counter reset, then exit
            if (mode == "reset")
            {
                try
                {
                    ToolUse.Core.IO.LearningDataReset.BackupLearningDataAndResetCounter();
                    Log.Information("Learning data backed up and total session counter reset. Exiting as requested.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to reset learning progress");
                    Console.Error.WriteLine($"[ERROR] Failed to reset learning progress: {ex.Message}");
                }
                Log.CloseAndFlush();
                return;
            }

            // Compose application
            // Choose renderer based on visualization flag; use a headless no-op renderer for CI/headless runs
            ToolUse.Core.Rendering.IWindowRenderer renderer = useVisualization
                ? new RaylibWindowRenderer()
                : new HeadlessWindowRenderer();
            var app = new SimulationApp(useVisualization, renderer, fps: (useVisualization ? 40 : 60), services: provider);

            // Set eval mode based on subcommand
            if (mode == "eval" || mode == "render")
            {
                app.SetEvalMode(true);
            }

            using var cts = new System.Threading.CancellationTokenSource();

            // Graceful shutdown on Ctrl+C / process exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // prevent abrupt termination
                try { cts.Cancel(); } catch { }
                Log.Information("Cancellation requested due to Ctrl+C");
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                try { cts.Cancel(); } catch { }
                try { app.Shutdown(); } catch { }
                Log.CloseAndFlush();
            };

            // Run until cancellation requested
            app.Run(cts.Token);

            // After Run exits, finalize shutdown
            try { app.Shutdown(); } catch { }
            Log.CloseAndFlush();
        }

        static bool ArgsHasFlag(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (a.Equals("--" + name, StringComparison.OrdinalIgnoreCase)) return true;
                if (a.StartsWith("--" + name + "=", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static Serilog.Events.LogEventLevel ParseLogLevel(string[] args)
        {
            foreach (var a in args)
            {
                if (!a.StartsWith("--logLevel=", StringComparison.OrdinalIgnoreCase)) continue;
                var v = a.Split('=', 2)[1];
                if (Enum.TryParse<Serilog.Events.LogEventLevel>(v, true, out var lvl)) return lvl;
            }
            return Serilog.Events.LogEventLevel.Information;
        }

        static string ParseMode(string[] args)
        {
            if (args == null || args.Length == 0) return string.Empty;
            var first = args[0].Trim().ToLowerInvariant();
            if (first is "train" or "eval" or "render" or "help" or "reset" or "-h" or "--help")
                return first == "-h" || first == "--help" ? "help" : first;
            return string.Empty;
        }

        static string ShowStartupMenu(out bool visualization)
        {
            Console.WriteLine("ToolUse.Sim — Hide & Seek DQN Simulator");
            Console.WriteLine("=======================================");
            Console.WriteLine("Choose mode:");
            Console.WriteLine("  1) train   — headless training");
            Console.WriteLine("  2) eval    — headless evaluation (no learning)");
            Console.WriteLine("  3) render  — 3D visualization (no learning)");
            Console.WriteLine("  4) help    — show CLI help");
            Console.WriteLine("  5) reset   — backup learning data and reset session counter");
            Console.Write("Enter choice [1-5] (default 1): ");

            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) input = "1"; // safe default: train
            while (input != "1" && input != "2" && input != "3" && input != "4" && input != "5")
            {
                Console.Write("Invalid input. Enter 1, 2, 3, 4 or 5: ");
                input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) input = "1";
            }

            switch (input)
            {
                case "1": visualization = false; return "train";
                case "2": visualization = false; return "eval";
                case "3": visualization = true;  return "render";
                case "4": PrintHelp(); visualization = false; return "train"; // print help then continue with safe default
                case "5": visualization = false; return "reset";
                default: visualization = false; return "train";
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  ToolUse.Sim [subcommand] [options]\n");
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  train              Run training in headless mode");
            Console.WriteLine("  eval               Run evaluation (no learning), headless by default");
            Console.WriteLine("  render             Run with 3D visualization (no learning)");
            Console.WriteLine("  reset              Backup learning data and reset session counter, then exit\n");
            Console.WriteLine("Options:");
            Console.WriteLine($"  --configPath=PATH  Path to {GameConfig.ConfigPath} (default: {GameConfig.ConfigPath})");
            Console.WriteLine($"                     Agents overrides are read from {AgentsConfig.FileName} next to it if present.");
            Console.WriteLine("  --seed=N           Global seed (overrides GameConfig.Seed/Training.Seed)");
            Console.WriteLine("  --device=(auto|cpu|cuda)  Preferred device (default: auto)");
            Console.WriteLine("  --logLevel=(Verbose|Debug|Information|Warning|Error|Fatal)  Logging level (default: Information)");
            Console.WriteLine("  --headless         Force headless (useful with 'eval')");
            Console.WriteLine("  --dump-config      Print effective configuration and exit\n");
            Console.WriteLine("Environment variables (prefix HNS_): e.g., HNS_Seed, HNS_Training__BatchSize, etc.");
        }
    }
}