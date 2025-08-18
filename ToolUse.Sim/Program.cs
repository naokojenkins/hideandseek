using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using Raylib_cs;
using ToolUse.Core.Config;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;
using TorchSharp;
using static TorchSharp.torch;

namespace ToolUse.Sim
{
    class Program
    {
        static GameConfig config;
        static int gridSize;
        const int screenW = 1024;
        const int screenH = 768;
        const int FPS = 60;

        static Simulation3D simulation = null!;
        static Agent3D seeker = null!;
        static Agent3D hider = null!;

        static DQNAgent seekerDQN = null!;
        static DQNAgent hiderDQN = null!;

        static bool isExiting = false;
        static bool useVisualization = true;
        static DateTime lastConsoleUpdate = DateTime.Now;

        // Оверлей между эпизодами в режиме визуализации
        static bool episodeOverPendingSave = false;
        static readonly string EpisodeOverlayText = "Episode over - creating new one";

        static Action? sessionCompletedHandler = null;

        // Новый флаг, чтобы не пропустить сохранения при Ctrl+C/исключениях
        static bool isShuttingDown = false;

        // Теперь используем более точное название папки
        static readonly string ModelDir = "models";
        static readonly string SeekerModelPath = Path.Combine(ModelDir, "seeker.pt");
        static readonly string HiderModelPath  = Path.Combine(ModelDir, "hider.pt");
        static readonly string SeekerStatePath = Path.Combine(ModelDir, "seeker_state.json");
        static readonly string HiderStatePath  = Path.Combine(ModelDir, "hider_state.json");

        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Console.WriteLine($"[FATAL] Необработанное исключение: {args.ExceptionObject}");
            };

            Console.CancelKeyPress += (sender, args) =>
            {
                Console.WriteLine("\n[INFO] Получен сигнал прерывания (Ctrl+C). Завершаем работу...");
                args.Cancel = true; // Предотвращаем немедленное завершение
                isExiting = true;
            };

            ShowStartupMenu();
            Run();
        }

        static void ShowStartupMenu()
        {
            Console.Clear();
            Console.WriteLine("=== Выберите режим запуска ===");
            Console.WriteLine("1 — Без визуализации (консольный режим)");
            Console.WriteLine("2 — С визуализацией (3D-окно)");
            Console.WriteLine("===============================");
            Console.Write("Введите номер режима (1 или 2): ");

            string? input = Console.ReadLine();
            while (input != "1" && input != "2")
            {
                Console.WriteLine("Неверный ввод. Пожалуйста, введите 1 или 2.");
                input = Console.ReadLine();
            }

            useVisualization = input == "2";
            Console.Clear();
        }

        public static void Run()
        {
            config = GameConfig.Instance;
            Console.WriteLine($"[DEBUG] Loaded config: GridSize={config.World.GridSize}, CellSize={config.World.CellSize}");
            Console.WriteLine($"[DEBUG] SessionDurationSeconds = {config.SessionDurationSeconds}");

            // Seeding for reproducibility
            if (config.Seed != 0)
            {
                try { Raylib.SetRandomSeed((uint)config.Seed); } catch { }
                try
                {
                    torch.random.manual_seed(config.Seed);
                    if (torch.cuda.is_available()) torch.cuda.manual_seed_all(config.Seed);
                }
                catch { }
            }

            gridSize = config.World.GridSize;

            int actionSize = Math.Max(1, config.Actions.Count); // из конфига
            var world = new World3D(gridSize);
            world.GenerateStaticGrid();
            var dummySeeker = new Agent3D(new Vector3(0, 0, 0), true);
            var dummyHider  = new Agent3D(new Vector3(0, 0, 0), false);
            var adapter = new SimAdapter3D(world, dummySeeker, dummyHider);
            var dummyState = adapter.GetSeekerState();
            int stateSize = dummyState.ToArray(gridSize).Length;

            seekerDQN = new DQNAgent(stateSize, actionSize, config.DQN);
            hiderDQN  = new DQNAgent(stateSize, actionSize, config.DQN);

            // Создаём папку для моделей
            Directory.CreateDirectory(ModelDir);

            // Загружаем веса и состояние агентов
            seekerDQN.LoadAll(SeekerModelPath, SeekerStatePath);
            hiderDQN.LoadAll(HiderModelPath, HiderStatePath);

            Reset();

            if (useVisualization)
            {
                Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
                Raylib.InitWindow(screenW, screenH, "3D Hide & Seek (DQN)");
                Raylib.SetTargetFPS(FPS);
            }

            try
            {
                while (!isExiting)
                {
                    if (useVisualization)
                    {
                        if (Raylib.WindowShouldClose())
                            break;

                        simulation?.HandleInput();
                        simulation?.Update(1f / FPS);

                        Raylib.BeginDrawing();
                        Raylib.ClearBackground(new Color(245, 245, 245, 255));
                        simulation?.Draw();

                        // Оверлей «между эпизодами»
                        if (episodeOverPendingSave)
                        {
                            int fontSize = 48;
                            int textW = Raylib.MeasureText(EpisodeOverlayText, fontSize);
                            int x = (screenW - textW) / 2;
                            int y = (screenH - fontSize) / 2;
                            // Лёгкая тень для читаемости
                            Raylib.DrawText(EpisodeOverlayText, x + 2, y + 2, fontSize, new Color(0, 0, 0, 180));
                            Raylib.DrawText(EpisodeOverlayText, x, y, fontSize, Color.White);
                        }

                        Raylib.EndDrawing();

                        // Отложенное сохранение после кадра с оверлеем
                        if (episodeOverPendingSave)
                        {
                            try
                            {
                                Directory.CreateDirectory(ModelDir);
                                seekerDQN.SaveAll(SeekerModelPath, SeekerStatePath);
                                hiderDQN.SaveAll(HiderModelPath, HiderStatePath);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR] Ошибка сохранения моделей: {ex.Message}");
                            }
                            finally
                            {
                                episodeOverPendingSave = false;
                            }
                        }
                    }
                    else
                    {
                        simulation?.Update(1f / FPS);

                        // Обновление каждую секунду
                        if ((DateTime.Now - lastConsoleUpdate).TotalSeconds >= 1)
                        {
                            PrintConsoleHUD();
                            lastConsoleUpdate = DateTime.Now;
                        }

                        // Без задержки для максимальной скорости headless-обучения
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Критическая ошибка: {ex}");
                try
                {
                    simulation?.DumpDiagnostics(ex);
                }
                catch (Exception dx)
                {
                    Console.WriteLine($"[ERROR] DumpDiagnostics failed: {dx}");
                }
            }
            finally
            {
                Shutdown();
            }
        }

        static void PrintConsoleHUD()
        {
            Console.Clear();
            Console.WriteLine("=== Статистика симуляции ===");
            Console.WriteLine($"Сессия: {simulation.Session}");
            Console.WriteLine($"Всего сессий: {Simulation3D.TotalSessions}");
            Console.WriteLine($"Текущее время сессии: {simulation.Timer:F1} с / {simulation.SessionDurationSeconds:F0} с");
            Console.WriteLine($"Seeker позиция: {simulation.Seeker.Position}");
            Console.WriteLine($"Hider позиция: {simulation.Hider.Position}");
            Console.WriteLine($"Seeker обнаружил Hider: {(simulation.Seeker.CanSee(simulation.Hider, simulation.World) ? "Да" : "Нет")}");
            Console.WriteLine($"Hider обнаружил Seeker: {(simulation.Hider.CanSee(simulation.Seeker, simulation.World) ? "Да" : "Нет")}");
            Console.WriteLine($"Обнаружено ячеек (Seeker): {simulation.Seeker.GetTotalExploredCount()}");
            Console.WriteLine($"Обнаружено ячеек (Hider): {simulation.Hider.GetTotalExploredCount()}");
            Console.WriteLine("==============================");
        }

        static void Shutdown()
        {
            if (isShuttingDown) return;
            isShuttingDown = true;
            isExiting = true;

            Console.WriteLine("Завершение программы...");

            try
            {
                Directory.CreateDirectory(ModelDir);
                seekerDQN.SaveAll(SeekerModelPath, SeekerStatePath);
                hiderDQN.SaveAll(HiderModelPath, HiderStatePath);

                if (simulation != null && sessionCompletedHandler != null)
                {
                    simulation.OnSessionCompleted -= sessionCompletedHandler;
                    sessionCompletedHandler = null;
                }

                Simulation3D.ForceSaveTotalSessions();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка завершения: {ex.Message}");
            }

            try
            {
                if (useVisualization && Raylib.IsWindowReady())
                {
                    Raylib.CloseWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка при закрытии Raylib: {ex.Message}");
            }

            Console.WriteLine($"Программа завершена. Всего сессий за историю: {Simulation3D.TotalSessions}");
        }

        static void Reset()
        {
            if (isExiting) return;

            int currentSession = simulation != null ? simulation.Session + 1 : 1;
            Console.WriteLine($"[DEBUG] Сессия #{currentSession} (общий #{Simulation3D.TotalSessions + 1}) начата");

            try
            {
                var world = new World3D(gridSize);
                world.GenerateStaticGrid();

                float seekerRadius = config.Seeker.AgentRadius;
                float hiderRadius  = config.Hider.AgentRadius;

                // Создаем списки агентов на основе Count (не меньше 1)
                int seekerCount = Math.Max(1, config.Seeker.Count);
                int hiderCount  = Math.Max(1, config.Hider.Count);

                var seekers = new List<Agent3D>(seekerCount);
                var hiders  = new List<Agent3D>(hiderCount);

                // Локальная функция сравнения по XZ (игнорируем Y)
                static bool TooCloseXZ(Vector3 a, Vector3 b, float minDist)
                {
                    float dx = a.X - b.X;
                    float dz = a.Z - b.Z;
                    return (dx * dx + dz * dz) < (minDist * minDist);
                }

                float sameTeamMinSeparationS = MathF.Max(2f * seekerRadius, 0.6f);
                float sameTeamMinSeparationH = MathF.Max(2f * hiderRadius, 0.6f);
                float crossTeamMinSeparation = MathF.Max(config.MinInitialSeparation, 1.0f);

                // Seeker'ы: избегаем совпадений внутри команды
                for (int i = 0; i < seekerCount; i++)
                {
                    Vector3 pos;
                    int attempts = 0;
                    do
                    {
                        pos = world.GetRandomValidAgentPosition(seekerRadius, 0f);
                        attempts++;
                        if (attempts > 200) break; // защита от бесконечного цикла
                    }
                    while (seekers.Any(s => TooCloseXZ(s.Position, pos, sameTeamMinSeparationS)));
                    seekers.Add(new Agent3D(pos, true, Raylib.GetRandomValue(0, 359)));
                }

                // Hider'ы: избегаем совпадений внутри команды и слишком близких к любым Seeker'ам
                for (int i = 0; i < hiderCount; i++)
                {
                    Vector3 pos;
                    int attempts = 0;
                    do
                    {
                        pos = world.GetRandomValidAgentPosition(hiderRadius, 0f);
                        attempts++;
                        if (attempts > 200) break;
                    }
                    while (hiders.Any(h => TooCloseXZ(h.Position, pos, sameTeamMinSeparationH)) ||
                           seekers.Any(s => TooCloseXZ(s.Position, pos, crossTeamMinSeparation)));
                    hiders.Add(new Agent3D(pos, false, Raylib.GetRandomValue(0, 359)));
                }

                var newSeeker = seekers[0];
                var newHider = hiders[0];

                if (simulation == null)
                {
                    simulation = new Simulation3D(gridSize, newSeeker, newHider, seekerDQN, hiderDQN);
                }
                else
                {
                    simulation.Reset(newSeeker, newHider);
                }

                // Передаем все созданные агенты в симуляцию (для отрисовки и возможной логики)
                simulation.SetAgents(seekers, hiders);

                if (sessionCompletedHandler != null)
                    simulation.OnSessionCompleted -= sessionCompletedHandler;

                sessionCompletedHandler = () =>
                {
                    if (isExiting) return;

                    Console.WriteLine($"[DEBUG] Сессия #{simulation.Session} (общий #{Simulation3D.TotalSessions}) завершена");

                    if (useVisualization)
                    {
                        // Визуальный режим: покажем оверлей и сохраним после кадра
                        episodeOverPendingSave = true;
                    }
                    else
                    {
                        // Консольный режим: сохраняем сразу
                        Directory.CreateDirectory(ModelDir);
                        seekerDQN.SaveAll(SeekerModelPath, SeekerStatePath);
                        hiderDQN.SaveAll(HiderModelPath, HiderStatePath);
                    }
                };

                simulation.OnSessionCompleted += sessionCompletedHandler;

                seeker = newSeeker;
                hider = newHider;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка в Reset(): {ex.Message}");
            }
        }
    }
}