using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core;
using ToolUse.Core.RL;
using ToolUse.Core.Config;
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
        const int SAVE_INTERVAL = 10;

        static readonly string TablesDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "qtables");

        const string seekerFile = "qtable_seeker.json";
        const string hiderFile = "qtable_hider.json";

        static readonly JsonSerializerSettings jsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        static readonly QTable seekerQ = new();
        static readonly QTable hiderQ = new();

        static Simulation3D simulation = null!;
        static Agent3D seeker = null!;
        static Agent3D hider = null!;

        static int session = 0;
        static DateTime lastSaveTime = DateTime.Now;
        static bool isExiting = false;

        static System.Action? sessionCompletedHandler = null;

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

            Directory.CreateDirectory(TablesDir);

            Console.WriteLine("Загрузка Q-таблиц...");
            LoadTable(seekerFile, seekerQ);
            LoadTable(hiderFile, hiderQ);
            Console.WriteLine($"Загружено: Seeker={seekerQ.Export().Count}, Hider={hiderQ.Export().Count} записей");
            
            // Создаем временный экземпляр для инициализации статического счетчика
            var tempSimulation = new Simulation3D(gridSize, 
                new Agent3D(new Vector3(1, 0, 1), true, 0), 
                new Agent3D(new Vector3(2, 0, 2), false, 0), 
                seekerQ, hiderQ);
            
            Console.WriteLine($"Общий счетчик сессий: {Simulation3D.TotalSessions}");

            Raylib.InitWindow(screenW, screenH, "ToolUse – 3D Hide & Seek");
            Raylib.SetTargetFPS(FPS);
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

            try
            {
                Reset();

                while (!Raylib.WindowShouldClose() && !isExiting)
                {
                    try
                    {
                        simulation?.HandleInput();
                        simulation?.Update(1f / FPS);

                        Raylib.BeginDrawing();
                        Raylib.ClearBackground(Color.RayWhite);
                        simulation?.Draw();
                        
                        Raylib.EndDrawing();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Ошибка в игровом цикле: {ex.Message}");
                        break;
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

        static void Shutdown()
        {
            if (isExiting) return;
            isExiting = true;

            Console.WriteLine("Завершение программы...");

            try
            {
                if (simulation != null && sessionCompletedHandler != null)
                {
                    simulation.OnSessionCompleted -= sessionCompletedHandler;
                    sessionCompletedHandler = null;
                }

                Console.WriteLine("Финальное сохранение...");
                SaveBothTablesSync();
                
                Simulation3D.ForceSaveTotalSessions();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка при сохранении: {ex.Message}");
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
                Console.WriteLine($"[DEBUG] Seeker позиция: ({seekerPos.X:F1}, {seekerPos.Y:F1}, {seekerPos.Z:F1})");
                
                Vector3 hiderPos = world.GetRandomEmptyPositionFarFrom(seekerPos, 5f, 0f);
                Console.WriteLine($"[DEBUG] Hider позиция: ({hiderPos.X:F1}, {hiderPos.Y:F1}, {hiderPos.Z:F1})");
                
                float actualDistance = Vector3.Distance(seekerPos, hiderPos);
                Console.WriteLine($"[DEBUG] Расстояние между агентами: {actualDistance:F1}");

                var newSeeker = new Agent3D(seekerPos, true, Raylib.GetRandomValue(0, 359));
                var newHider = new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));

                
                if (simulation == null)
                {
                    simulation = new Simulation3D(gridSize, newSeeker, newHider, seekerQ, hiderQ);
                }
                else
                {
                    simulation.Reset(newSeeker, newHider);
                }

                if (sessionCompletedHandler != null)
                {
                    simulation.OnSessionCompleted -= sessionCompletedHandler;
                }

                sessionCompletedHandler = () =>
                {
                    if (isExiting) return;

                    Console.WriteLine($"[DEBUG] Сессия #{session} (общий #{Simulation3D.TotalSessions}) завершена");
                    
                    if (session % SAVE_INTERVAL == 0)
                    {
                        SaveBothTablesAsync();
                        // Принудительно сохраняем счетчик сессий периодически
                        Simulation3D.ForceSaveTotalSessions();
                    }
                    
                    Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(100);
                        if (!isExiting)
                        {
                            Reset();
                        }
                    });
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

        static string PathTo(string file) => Path.Combine(TablesDir, file);

        public static void LoadTable(string file, QTable q)
        {
            string path = PathTo(file);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[DEBUG] Файл не найден: {file}");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json, jsonSettings);
                if (data == null || data.Count == 0)
                {
                    Console.WriteLine($"[DEBUG] Файл пуст: {file}");
                    return;
                }

                q.LoadFrom(data);
                Console.WriteLine($"[DEBUG] Загружено {data.Count} записей из {file}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки {file}: {ex.Message}");
            }
        }

        public static async void SaveBothTablesAsync()
        {
            if (isExiting) return;
            
            try
            {
                await Task.Run(() =>
                {
                    SaveBothTablesSync();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка асинхронного сохранения: {ex.Message}");
            }
        }

        public static void SaveBothTablesSync()
        {
            if (isExiting) return;

            try
            {
                SaveTableSync(seekerFile, seekerQ);
                SaveTableSync(hiderFile, hiderQ);
                
                Console.WriteLine($"[DEBUG] Обе таблицы сохранены успешно");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения обеих таблиц: {ex.Message}");
            }
        }

        public static void SaveTableSync(string file, QTable q)
        {
            string path = PathTo(file);
            try
            {
                var current = q.Export();
                
                if (current.Count == 0)
                {
                    Console.WriteLine($"[WARNING] Пустая таблица для {file}");
                    return;
                }

                var combined = new Dictionary<string, float[]>();

                if (File.Exists(path))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(path);
                        var existing = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(existingJson, jsonSettings);
                        if (existing != null)
                        {
                            foreach (var kvp in existing)
                            {
                                combined[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Не удалось загрузить существующие данные из {file}: {ex.Message}");
                    }
                }

                foreach (var kvp in current)
                {
                    combined[kvp.Key] = kvp.Value;
                }

                string json = JsonConvert.SerializeObject(combined, Formatting.None, jsonSettings);
                File.WriteAllText(path, json);
                
                Console.WriteLine($"[DEBUG] Синхронно сохранено {combined.Count} записей в {file}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка синхронного сохранения {file}: {ex.Message}");
            }
        }
    }
}