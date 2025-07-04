using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core;
using ToolUse.Core.RL;
using ToolUse.Core.RaylibThreeD;
using RColor = Raylib_cs.Color;

namespace ToolUse.Sim
{
    class Program3D
    {
        const int gridSize = 40;
        const int screenW = 1024;
        const int screenH = 768;

        const float sessSec = 60f;
        const int FPS = 5;
        const int maxFrames = (int)(sessSec * FPS);

        static JsonSerializerSettings jsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        static QTable seekerQ = new(), hiderQ = new();
        static QAgent seekerRL = new(seekerQ, 0.1f);
        static QAgent hiderRL = new(hiderQ, 0.1f);

        static World3D world = new(gridSize);
        static Agent3D seeker = null!; // Инициализация в Reset()
        static Agent3D hider = null!;  // Инициализация в Reset()
        static Renderer3D renderer = new();
        static Simulation3D simulation = null!;

        static int session = 0, frame = 0, caughtFrames = 0;
        static float timer = 0f;
        static bool caught = false;

        static float sumSeeker = 0f, sumHider = 0f;

        enum Act { RotL, RotR, Fwd }

        public static void Run()
        {
            LoadTable("qtable_seeker_3d.json", seekerQ);
            LoadTable("qtable_hider_3d.json", hiderQ);

            Raylib.InitWindow(screenW, screenH, "Tool Use – 3D multi-agent");
            Raylib.SetTargetFPS(FPS);

            // Enable 3D mode
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);

            world.GenerateStaticGrid();
            Reset();

            while (!Raylib.WindowShouldClose())
            {
                frame++;
                timer += 1f / FPS;
                float deltaTime = 1.0f / FPS;

                // Обработка ввода для симуляции
                simulation.HandleInput();

                // Обновление симуляции
                simulation.Update(deltaTime);

                // Проверка условий завершения сессии
                bool visible = seeker.CanSee(hider, world);

                if (visible)
                {
                    if (++caughtFrames >= FPS * 5) // Reduced time to catch in 3D
                    {
                        caught = true;
                    }
                }
                else
                {
                    caughtFrames = 0;
                }

                // Обновляем счет на основе видимости
                float seekerReward = visible ? 0.1f : -0.02f;
                float hiderReward = visible ? -0.1f : 0.1f;

                // Накапливаем счет для отображения
                sumSeeker += seekerReward;
                sumHider += hiderReward;

                if (caught || frame >= maxFrames) Reset();

                // Рисуем сцену
                Raylib.BeginDrawing();
                Raylib.ClearBackground(RColor.RayWhite);

                // Рисуем 3D сцену
                simulation.Draw();

                // Рисуем UI поверх 3D
               // DrawHUD();

                Raylib.EndDrawing();
            }

            SaveTable("qtable_seeker_3d.json", seekerQ);
            SaveTable("qtable_hider_3d.json", hiderQ);
            Raylib.CloseWindow();
        }

        static void Reset()
        {
            session++;
            frame = 0;
            timer = 0f;
            caught = false;
            caughtFrames = 0;
            sumSeeker = 0f;
            sumHider = 0f;

            // 1. Сохраняем таблицы, чтобы сохранить опыт предыдущей сессии
            if (session > 1)
            {
                SaveTable("qtable_seeker_3d.json", seekerQ);
                SaveTable("qtable_hider_3d.json", hiderQ);
            }

            // 2. Загружаем обновлённые таблицы
            LoadTable("qtable_seeker_3d.json", seekerQ);
            LoadTable("qtable_hider_3d.json", hiderQ);

            // 3. Генерируем новую карту и агентов
            world.GenerateStaticGrid();

            // Получаем случайные позиции с минимальным расстоянием от стен
            Vector3 seekerPos = world.GetRandomEmptyPosition(1.0f);
            Vector3 hiderPos;

            // Убедимся, что агенты не спавнятся слишком близко друг к другу
            do
            {
                hiderPos = world.GetRandomEmptyPosition(1.0f);
            } while (Vector3.Distance(seekerPos, hiderPos) < 10f);

            // Если симуляция еще не создана, инициализируем ее вместе с агентами
            if (simulation == null)
            {
                // Создаем агентов для первоначальной инициализации
                seeker = new Agent3D(seekerPos, true, Raylib.GetRandomValue(0, 359));
                hider = new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));

                // Создаем симуляцию
                simulation = new Simulation3D(gridSize);
            }
            else
            {
                // Для последующих сбросов создаем временных агентов с новыми позициями
                Agent3D tempSeeker = new Agent3D(seekerPos, true, Raylib.GetRandomValue(0, 359));
                Agent3D tempHider = new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));

                // Обновляем симуляцию, свойства агентов будут скопированы
                simulation.Reset(tempSeeker, tempHider);
            }
        }

        static void DoAction(Agent3D ag, Act act)
        {
            switch (act)
            {
                case Act.RotL: ag.Rotate(-15f); break;
                case Act.RotR: ag.Rotate(+15f); break;
                case Act.Fwd: ag.MoveWithCollisionAvoidance(world, 1.0f / FPS); break;
            }
        }

        /*static void DrawHUD()
        {
            int x = 10, y = 10;
            Raylib.DrawRectangle(x - 5, y - 5, 300, 100, Raylib.ColorAlpha(RColor.Black, 0.7f));

            Raylib.DrawText($"Session: {session}  Time: {timer:F0}s", x, y, 20, RColor.White);
            y += 30;
            Raylib.DrawText($"Seeker score: {sumSeeker:F1}", x, y, 20, RColor.Blue);
            y += 30;
            Raylib.DrawText($"Hider score: {sumHider:F1}", x, y, 20, RColor.Green);

            // Draw instructions at the bottom
            string instructions = "F: Toggle follow | V: Toggle vision cones | G: Toggle grid | R: Restart";
            int width = Raylib.MeasureText(instructions, 20);
            Raylib.DrawRectangle(screenW/2 - width/2 - 10, screenH - 40, width + 20, 30, Raylib.ColorAlpha(RColor.Black, 0.7f));
            Raylib.DrawText(instructions, screenW/2 - width/2, screenH - 35, 20, RColor.White);
        }*/

        static void LoadTable(string path, QTable q)
        {
            if (!File.Exists(path)) return;
            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<State, float[]>>(File.ReadAllText(path), jsonSettings);
                if (map != null)
                {
                    var current = q.Export();

                    foreach (var kvp in map)
                    {
                        bool found = false;
                        foreach (var key in current.Keys)
                        {
                            if (key.Equals(kvp.Key))
                            {
                                float[] existing = current[key];
                                float[] loaded = kvp.Value;
                                for (int i = 0; i < existing.Length; i++)
                                {
                                    existing[i] = (existing[i] + loaded[i]) / 2f;
                                }
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            current[kvp.Key] = kvp.Value;
                        }
                    }

                    q.LoadFrom(current);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки файла {path}: {ex.Message}");
            }
        }

        static void SaveTable(string path, QTable q)
        {
            try
            {
                var combined = new Dictionary<State, float[]>();

                if (File.Exists(path))
                {
                    var existing = JsonConvert.DeserializeObject<Dictionary<State, float[]>>(File.ReadAllText(path), jsonSettings);
                    if (existing != null)
                    {
                        foreach (var kvp in existing)
                        {
                            combined[kvp.Key] = kvp.Value;
                        }
                    }
                }

                foreach (var kvp in q.Export())
                {
                    bool found = false;
                    foreach (var key in combined.Keys.ToList())
                    {
                        if (key.Equals(kvp.Key))
                        {
                            float[] old = combined[key];
                            float[] @new = kvp.Value;
                            for (int i = 0; i < old.Length; i++)
                            {
                                old[i] = (old[i] + @new[i]) / 2f;
                            }
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        combined[kvp.Key] = kvp.Value;
                    }
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(combined, Formatting.None, jsonSettings));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения файла {path}: {ex.Message}");
            }
        }
    }
}
