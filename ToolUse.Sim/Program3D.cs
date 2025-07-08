using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;
using ToolUse.Core.Config;

namespace ToolUse.Sim
{
    class Program3D
    {
        const int gridSize = 40;
        const int screenW = 1024;
        const int screenH = 768;
        const int FPS = 60;

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

        static int session = 0;

        public static void Run()
        {
            // AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            // {
            //     SaveTable(seekerFile, seekerQ);
            //     SaveTable(hiderFile, hiderQ);
            //     Raylib.CloseWindow();
            // };

            var cfg = GameConfig.Load();
            Console.WriteLine($"[DEBUG] SessionDurationSeconds = {cfg.SessionDurationSeconds}");

            Directory.CreateDirectory(TablesDir);

            LoadTable(seekerFile, seekerQ);
            LoadTable(hiderFile, hiderQ);

            Raylib.InitWindow(screenW, screenH, "ToolUse – 3D multi-agent");
            Raylib.SetTargetFPS(FPS);
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

            Reset();

            while (!Raylib.WindowShouldClose())
            {
                simulation.HandleInput();
                simulation.Update(1f / FPS);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.RayWhite);
                simulation.Draw();
                Raylib.EndDrawing();
            }
        }

        static void Reset()
        {
            session++;
            //Console.WriteLine($"[DEBUG] Reset: начата сессия #{session}");

            seekerQ.Clear();
            hiderQ.Clear();

            LoadTable(seekerFile, seekerQ);
            LoadTable(hiderFile, hiderQ);

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

            // ✅ Подписываемся на событие завершения сессии
            simulation.OnSessionCompleted += () =>
            {
                //Console.WriteLine("[DEBUG] Simulation: сессия завершена, вызываем Reset()");
                SaveTable(seekerFile, seekerQ);
                SaveTable(hiderFile, hiderQ);
                Reset();
            };

            seeker = newSeeker;
            hider = newHider;

            //Console.WriteLine($"[DEBUG] Reset: сессия #{session} начата");
        }

        static string PathTo(string file) => Path.Combine(TablesDir, file);

        public static void LoadTable(string file, QTable q)
        {
            string path = PathTo(file);
            if (!File.Exists(path))
            {
                //Console.WriteLine($"[DEBUG] LoadTable: файл не найден: {file}");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json, jsonSettings);
                if (data == null || data.Count == 0)
                {
                    //Console.WriteLine($"[DEBUG] LoadTable: файл пуст или не распознан: {file}");
                    return;
                }

                //Console.WriteLine($"[DEBUG] LoadTable: загружено {data.Count} записей из {file}");
                q.LoadFrom(data);

                // ✅ Логируем, что таблица обновилась
                //Console.WriteLine($"[DEBUG] LoadTable: QTable обновлена, теперь содержит {q.Export().Count} записей");
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[ERROR] LoadTable: ошибка загрузки {file}: {ex.Message}");
            }
        }

        public static void SaveTable(string file, QTable q)
        {
            string path = PathTo(file);
            try
            {
                var current = q.Export();
                //Console.WriteLine($"[DEBUG] SaveTable: экспортировано {current.Count} записей");

                File.WriteAllText(path, JsonConvert.SerializeObject(current, Formatting.None, jsonSettings));

                //Console.WriteLine($"[DEBUG] SaveTable: сохранено {current.Count} записей в {file}");
                if (current.Count > 0)
                {
                    var first = current.First();
                    //Console.WriteLine($"[DEBUG] SaveTable: пример записи: {first.Key} → [{string.Join(",", first.Value)}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения {path}: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }
    }
}