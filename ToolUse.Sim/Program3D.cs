using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;
using ToolUse.Core.Config;

namespace ToolUse.Sim
{
    class Program3D
    {
        static GameConfig config;
        static int gridSize;
        const int screenW = 1024;
        const int screenH = 768;
        const int FPS = 60;
        const int SAVE_INTERVAL = 10; // Сохранять каждые 10 сессий

        static readonly string TablesDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "qtables");

        const string seekerFile = "qtable_seeker_3d.json";
        const string hiderFile = "qtable_hider_3d.json";

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

        static int session = 0; // Счетчик сессий текущего запуска
        static DateTime lastSaveTime = DateTime.Now;
        static bool isExiting = false;

        // Храним ссылку на обработчик события для корректной отписки
        static System.Action? sessionCompletedHandler = null;

        public static void Run()
        {
            // Загружаем конфигурацию
            config = GameConfig.Load();
            Console.WriteLine($"[DEBUG] Loaded 3D config: GridSize={config.World.GridSize3D}, CellSize={config.World.CellSize3D}");
            Console.WriteLine($"[DEBUG] SessionDurationSeconds = {config.SessionDurationSeconds}");

            // Инициализируем размер из конфигурации
            gridSize = config.World.GridSize3D;

            Directory.CreateDirectory(TablesDir);

            // Загружаем таблицы ТОЛЬКО ОДИН РАЗ при старте
            Console.WriteLine("Загрузка Q-таблиц...");
            LoadTable(seekerFile, seekerQ);
            LoadTable(hiderFile, hiderQ);
            Console.WriteLine($"Загружено: Seeker={seekerQ.Export().Count}, Hider={hiderQ.Export().Count} записей");
            Console.WriteLine($"Общий счетчик сессий: {Simulation3D.TotalSessions}");

            // Инициализируем Raylib
            Raylib.InitWindow(screenW, screenH, "ToolUse – 3D multi-agent");
            Raylib.SetTargetFPS(FPS);
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

            try
            {
                Reset();

                // Основной игровой цикл
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
                // Корректное завершение
                Shutdown();
            }
        }

        static void Shutdown()
        {
            if (isExiting) return; // Предотвращаем двойное закрытие
            isExiting = true;

            Console.WriteLine("Завершение программы...");

            try
            {
                // Отписываемся от событий
                if (simulation != null && sessionCompletedHandler != null)
                {
                    simulation.OnSessionCompleted -= sessionCompletedHandler;
                    sessionCompletedHandler = null;
                }

                // Финальное сохранение
                Console.WriteLine("Финальное сохранение...");
                SaveBothTablesSync();
                
                // Сохраняем общий счетчик сессий из Simulation3D
                Simulation3D.ForceSaveTotalSessions();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка при сохранении: {ex.Message}");
            }

            try
            {
                // Закрываем Raylib только если оно было инициализировано
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
                // Создаем мир с размером из конфигурации
                var world = new World3D(gridSize);
                world.GenerateStaticGrid();

                Vector3 seekerPos = world.GetRandomEmptyPosition(1f);
                Vector3 hiderPos;
                do
                {
                    hiderPos = world.GetRandomEmptyPosition(1f);
                } while (Vector3.Distance(seekerPos, hiderPos) < 10f);

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

                // Отписываемся от старого обработчика, если он есть
                if (sessionCompletedHandler != null)
                {
                    simulation.OnSessionCompleted -= sessionCompletedHandler;
                }

                // Создаем новый обработчик события
                sessionCompletedHandler = () =>
                {
                    if (isExiting) return;

                    Console.WriteLine($"[DEBUG] Сессия #{session} (общий #{Simulation3D.TotalSessions}) завершена");
                    
                    // Сохраняем только каждую N-ю сессию
                    if (session % SAVE_INTERVAL == 0)
                    {
                        SaveBothTablesAsync();
                    }
                    
                    // Планируем следующую сессию
                    Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(100); // Небольшая задержка
                        if (!isExiting)
                        {
                            Reset();
                        }
                    });
                };

                // Подписываемся на событие завершения сессии
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

        // Синхронная загрузка (используется только при старте)
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

        // Асинхронное сохранение обеих таблиц одновременно
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

        // Синхронное сохранение обеих таблиц
        public static void SaveBothTablesSync()
        {
            if (isExiting) return;

            try
            {
                // Сохраняем обе таблицы последовательно
                SaveTableSync(seekerFile, seekerQ);
                SaveTableSync(hiderFile, hiderQ);
                
                Console.WriteLine($"[DEBUG] Обе таблицы сохранены успешно");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения обеих таблиц: {ex.Message}");
            }
        }

        // Синхронное сохранение одной таблицы
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

                // Объединяем с существующими данными (как в 2D версии)
                var combined = new Dictionary<string, float[]>();

                // Загружаем существующие данные
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

                // Добавляем/обновляем новые данные
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