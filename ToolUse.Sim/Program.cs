using System;
using System.IO;
using System.Numerics;
using System.Reflection;
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

        static int session = 0;
        static bool isExiting = false;

        static Action? sessionCompletedHandler = null;

        static readonly string QTableDir = "qtables";
        static readonly string SeekerWeights = Path.Combine(QTableDir, "seeker.pt");
        static readonly string HiderWeights  = Path.Combine(QTableDir, "hider.pt");
        static readonly string SeekerState   = Path.Combine(QTableDir, "seeker_state.json");
        static readonly string HiderState    = Path.Combine(QTableDir, "hider_state.json");

        public static void Main(string[] args)
        {
            Run();
        }

        public static void Run()
        {
            config = GameConfig.Load();
            Console.WriteLine($"[DEBUG] Loaded config: GridSize={config.World.GridSize}, CellSize={config.World.CellSize}");
            Console.WriteLine($"[DEBUG] SessionDurationSeconds = {config.SessionDurationSeconds}");

            gridSize = config.World.GridSize;

            int stateSize = 6;
            int actionSize = 3;
            seekerDQN = new DQNAgent(stateSize, actionSize);
            hiderDQN  = new DQNAgent(stateSize, actionSize);

            Directory.CreateDirectory(QTableDir);
            seekerDQN.LoadAll(SeekerWeights, SeekerState);
            hiderDQN.LoadAll(HiderWeights, HiderState);

            Reset();

            Raylib.InitWindow(screenW, screenH, "ToolUse – 3D Hide & Seek (DQN)");
            Raylib.SetTargetFPS(FPS);
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

            try
            {
                while (!Raylib.WindowShouldClose() && !isExiting)
                {
                    simulation?.HandleInput();
                    simulation?.Update(1f / FPS);

                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(new Color(245, 245, 245, 255));
                    simulation?.Draw();
                    Raylib.EndDrawing();
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

        static void Shutdown()
        {
            if (isExiting) return;
            isExiting = true;

            Console.WriteLine("Завершение программы...");

            try
            {
                Directory.CreateDirectory(QTableDir);
                seekerDQN.SaveAll(SeekerWeights, SeekerState);
                hiderDQN.SaveAll(HiderWeights, HiderState);

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
                if (Raylib.IsWindowReady())
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

            session++;
            Console.WriteLine($"[DEBUG] Сессия #{session} (общий #{Simulation3D.TotalSessions + 1}) начата");

            try
            {
                var world = new World3D(gridSize);
                world.GenerateStaticGrid();

                Vector3 seekerPos = world.GetRandomEmptyPosition(0f);
                Vector3 hiderPos = world.GetRandomEmptyPositionFarFrom(seekerPos, 5f, 0f);

                float actualDistance = Vector3.Distance(seekerPos, hiderPos);

                var newSeeker = new Agent3D(seekerPos, true, Raylib.GetRandomValue(0, 359));
                var newHider = new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));

                if (simulation == null)
                {
                    simulation = new Simulation3D(gridSize, newSeeker, newHider, seekerDQN, hiderDQN);
                }
                else
                {
                    simulation.Reset(newSeeker, newHider);
                }

                if (sessionCompletedHandler != null)
                    simulation.OnSessionCompleted -= sessionCompletedHandler;

                sessionCompletedHandler = () =>
                {
                    if (isExiting) return;

                    Console.WriteLine($"[DEBUG] Сессия #{session} (общий #{Simulation3D.TotalSessions}) завершена");

                    // --- Сохраняем после каждой сессии ---
                    Directory.CreateDirectory(QTableDir);
                    seekerDQN.SaveAll(SeekerWeights, SeekerState);
                    hiderDQN.SaveAll(HiderWeights, HiderState);

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
