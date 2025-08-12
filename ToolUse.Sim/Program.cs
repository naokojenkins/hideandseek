using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using Raylib_cs;
using ToolUse.Core.Config;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;

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

            gridSize = config.World.GridSize;

            int actionSize = 5; // 0=L,1=R,2=FWD,3=FWD+L,4=FWD+R
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
                        Raylib.EndDrawing();
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
                Console.WriteLine($"[ERROR] Критическая ошибка: {ex.Message}");
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
            Console.WriteLine($"Текущее время сессии: {simulation.Timer:F1} с / {config.SessionDurationSeconds:F0} с");
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

                for (int i = 0; i < seekerCount; i++)
                {
                    Vector3 pos = world.GetRandomValidAgentPosition(seekerRadius, 0f);
                    seekers.Add(new Agent3D(pos, true, Raylib.GetRandomValue(0, 359)));
                }
                for (int i = 0; i < hiderCount; i++)
                {
                    Vector3 pos = world.GetRandomValidAgentPosition(hiderRadius, 0f);
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

                    Directory.CreateDirectory(ModelDir);
                    seekerDQN.SaveAll(SeekerModelPath, SeekerStatePath);
                    hiderDQN.SaveAll(HiderModelPath, HiderStatePath);

                    System.Threading.Thread.Sleep(100);
                    if (!isExiting)
                    {
                        Reset();
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