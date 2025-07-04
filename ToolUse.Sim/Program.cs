using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core;
using ToolUse.Core.RL;
using ToolUse.Sim;
using RColor = Raylib_cs.Color;

class Program
{
    const int cellSize = 16, gridSize = 40;
    const int fieldSize = cellSize * gridSize;
    const int padY = 0;
    const int screenW = fieldSize;
    const int screenH = fieldSize + padY + 40;

    const float sessSec = 60f;
    const int FPS = 20;
    const int maxFrames = (int)(sessSec * FPS);

    static JsonSerializerSettings jsonSettings = new()
    {
        // Убираем StateKeyConverter
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore
    };

    static QTable seekerQ = new(), hiderQ = new();
    static QAgent seekerRL = new(seekerQ, 0.1f);
    static QAgent hiderRL = new(hiderQ, 0.1f);

    static World world = new(gridSize);
    static Agent seeker = null!; // Инициализация в Reset()
    static Agent hider = null!;  // Инициализация в Reset()

    static int session = 0, frame = 0, caughtFrames = 0;
    static float timer = 0f;
    static bool caught = false;

    static float sumSeeker = 0f, sumHider = 0f;

    enum Act { RotL, RotR, Fwd }

    static void Main()
    {
        Console.WriteLine("Choose simulation mode:");
        Console.WriteLine("1. 2D Simulation");
        Console.WriteLine("2. 3D Simulation");
        Console.Write("Enter your choice (1 or 2): ");

        string choice = Console.ReadLine();

        if (choice == "2")
        {
            // Run 3D simulation
            Program3D.Run();
            return;
        }

        // Default to 2D simulation
        LoadTable("qtable_seeker.json", seekerQ);
        LoadTable("qtable_hider.json", hiderQ);

        Raylib.InitWindow(screenW, screenH, "Tool Use – multi-agent (2D)");
        Raylib.SetTargetFPS(FPS);

        world.GenerateStaticGrid();
        Reset();

        while (!Raylib.WindowShouldClose())
        {
            frame++;
            timer += 1f / FPS;

            var sSeek = new State(seeker.X, seeker.Y, hider.X, hider.Y, seeker.CanSee(hider, world));
            var sHide = new State(hider.X, hider.Y, seeker.X, seeker.Y, seeker.CanSee(hider, world));

            var aS = (Act)seekerRL.ChooseAction(sSeek);
            var aH = (Act)hiderRL.ChooseAction(sHide);

            DoAction(seeker, aS);
            DoAction(hider, aH);

            bool visible = seeker.CanSee(hider, world);

            float rS = 0, rH = 0;

            if (visible)
            {
                rS += 0.1f;
                rH -= 0.1f;
                if (++caughtFrames >= FPS * 10)
                {
                    rS += 1;
                    rH -= 1;
                    caught = true;
                }
            }
            else
            {
                rS -= 0.02f;
                rH += 0.1f;
                caughtFrames = 0;
            }

            sumSeeker += rS;
            sumHider += rH;

            var sS2 = new State(seeker.X, seeker.Y, hider.X, hider.Y, visible);
            var sH2 = new State(hider.X, hider.Y, seeker.X, seeker.Y, visible);

            seekerRL.Learn(sSeek, (int)aS, rS, sS2);
            hiderRL.Learn(sHide, (int)aH, rH, sH2);

            if (caught || frame >= maxFrames) Reset();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(RColor.RayWhite);

            DrawWorld();
            DrawAgents();
            DrawHUD();

            Raylib.EndDrawing();
        }

        SaveTable("qtable_seeker.json", seekerQ);
        SaveTable("qtable_hider.json", hiderQ);
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

        // 1. Сначала сохраняем таблицы, чтобы сохранить опыт предыдущей сессии
        if (session > 1)
        {
            SaveTable("qtable_seeker.json", seekerQ);
            SaveTable("qtable_hider.json", hiderQ);
        }

        // 2. Теперь загружаем обновлённые таблицы (включая только что сохранённые)
        LoadTable("qtable_seeker.json", seekerQ);
        LoadTable("qtable_hider.json", hiderQ);

        // 3. Генерируем новую карту и агентов
        world.GenerateStaticGrid();

        // Создаем искателя
        do
        {
            seeker = new Agent(Raylib.GetRandomValue(0, gridSize - 1), Raylib.GetRandomValue(0, gridSize - 1), true, 0);
        }
        while (world.IsBlocked(seeker.X, seeker.Y));

        // Создаем прятку
        do
        {
            hider = new Agent(Raylib.GetRandomValue(0, gridSize - 1), Raylib.GetRandomValue(0, gridSize - 1), false, 180);
        }
        while (world.IsBlocked(hider.X, hider.Y));
    }

    static void DoAction(Agent ag, Act act)
    {
        switch (act)
        {
            case Act.RotL: ag.Rotate(-15f); break;
            case Act.RotR: ag.Rotate(+15f); break;
            case Act.Fwd: ag.MoveForward(world); break;
        }
    }

    static void DrawWorld()
    {
        for (int gx = 0; gx < gridSize; gx++)
        for (int gy = 0; gy < gridSize; gy++)
        {
            var t = world.Grid[gx, gy];
            RColor f = t switch
            {
                TileType.Empty => RColor.LightGray,
                TileType.Wall => RColor.DarkGray,
                _ => RColor.Brown
            };
            Raylib.DrawRectangle(gx * cellSize, gy * cellSize, cellSize, cellSize, f);
            Raylib.DrawRectangleLines(gx * cellSize, gy * cellSize, cellSize, cellSize, RColor.Black);
        }
    }

    static void DrawAgents()
    {
        DrawCone(seeker, new RColor(173, 216, 230, 80));
        DrawAgent(seeker, RColor.Blue);

        var visible = seeker.CanSee(hider, world);
        var color = visible ? RColor.Yellow : RColor.Green;
        DrawCone(hider, new RColor(0, 255, 0, 40));
        DrawAgent(hider, color);
    }

    static void DrawAgent(Agent ag, RColor color)
    {
        int pad = 4, sz = cellSize - 2 * pad;
        Raylib.DrawRectangle(ag.X * cellSize + pad, ag.Y * cellSize + pad, sz, sz, color);
    }

    static void DrawCone(Agent ag, RColor col) =>
        DrawFilledVisionCone(ag, cellSize, col, world);

    static void DrawHUD()
    {
        Raylib.DrawRectangle(0, 0, fieldSize, 12, RColor.White);
        int x = 10, y = 2, fs = 8;
        Raylib.DrawText($"Session: {session}  Time: {timer:F0}s", x, y, fs, RColor.Black);

        Raylib.DrawText($"Seeker: {sumSeeker:F1}", x + 20, y + 645, fs, RColor.Blue);
        Raylib.DrawLine(20, 659, fieldSize, 659, RColor.Black);
        Raylib.DrawText($"Hider: {sumHider:F1}", x + 20, y + 660, fs, RColor.Green);
    }

    static void DrawFilledVisionCone(Agent agent, int cell, RColor col, World w,
                                 float stepDeg = 1f, float stepPix = 2f, float thick = 3f)
    {
        Vector2 c = new(agent.X * cell + cell / 2, agent.Y * cell + cell / 2);
        float maxR = cell * agent.VisionRadius;
        float a0 = agent.Angle - agent.VisionAngle / 2;
        float a1 = agent.Angle + agent.VisionAngle / 2;

        Raylib.BeginBlendMode(BlendMode.Alpha);
        for (float ang = a0; ang <= a1; ang += stepDeg)
        {
            float r = ang * MathF.PI / 180f;
            float dx = MathF.Cos(r), dy = MathF.Sin(r), dist = 0;

            while (dist < maxR)
            {
                int gx = (int)((c.X + dx * dist) / cell);
                int gy = (int)((c.Y + dy * dist) / cell);
                if (!w.IsInside(gx, gy) || w.Grid[gx, gy] == TileType.Wall)
                {
                    dist -= stepPix;
                    break;
                }
                dist += stepPix;
            }

            Vector2 tip = new(c.X + dx * dist, c.Y + dy * dist);
            Raylib.DrawLineEx(c, tip, thick, col);
        }
        Raylib.EndBlendMode();
    }

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