using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Raylib_cs;
using ToolUse.Core;
using ToolUse.Core.RL;
using RColor = Raylib_cs.Color;

class Program
{
    const int cellSize = 16, gridSize = 40;
    const int fieldSize = cellSize * gridSize;
    const int graphW = 280, graphH = 100;
    const int padY = 50;
    const int screenW = fieldSize;
    const int screenH = fieldSize + padY + graphH + 20;

    const float sessSec = 30f;
    const int FPS = 60;
    const int maxFrames = (int)(sessSec * FPS);

    static JsonSerializerSettings jsonSettings = new()
    {
        Converters = new List<JsonConverter> { new StateKeyConverter() }
    };

    static QTable seekerQ = new(), hiderQ = new();
    static QAgent seekerRL = new(seekerQ, 0.1f);
    static QAgent hiderRL = new(hiderQ, 0.1f);

    static World world = new(gridSize);
    static List<Agent> seekers = new();
    static List<Agent> hiders = new();

    static int session = 0, frame = 0, caughtFrames = 0;
    static float timer = 0f;
    static bool caught = false;

    static List<float> rss = new(), rsh = new();
    static float[] sumS = new float[2];
    static float[] sumH = new float[2];

    enum Act { RotL, RotR, Fwd }

    static void Main()
    {
        LoadTable("qtable_seeker.json", seekerQ);
        LoadTable("qtable_hider.json", hiderQ);

        Raylib.InitWindow(screenW, screenH, "Tool Use – multi-agent");
        Raylib.SetTargetFPS(FPS);

        world.GenerateStaticGrid();
        Reset();

        while (!Raylib.WindowShouldClose())
        {
            frame++;
            timer += 1f / FPS;

            foreach (var seeker in seekers)
            foreach (var hider in hiders)
            {
                var sSeek = new State(seeker.X, seeker.Y, hider.X, hider.Y, seeker.CanSee(hider, world));
                var sHide = new State(hider.X, hider.Y, seeker.X, seeker.Y, seeker.CanSee(hider, world));

                var aS = (Act)seekerRL.ChooseAction(sSeek);
                var aH = (Act)hiderRL.ChooseAction(sHide);

                DoAction(seeker, aS);
                DoAction(hider, aH);

                bool visible = seeker.CanSee(hider, world);

                int iS = seekers.IndexOf(seeker);
                int iH = hiders.IndexOf(hider);
                float rS = 0, rH = 0;

                if (visible)
                {
                    rS += 0.1f; rH -= 0.1f;
                    if (++caughtFrames >= FPS * 10) { rS += 1; rH -= 1; caught = true; }
                }
                else
                {
                    rS -= 0.02f; rH += 0.1f;
                    caughtFrames = 0;
                }

                sumS[iS] += rS;
                sumH[iH] += rH;

                var sS2 = new State(seeker.X, seeker.Y, hider.X, hider.Y, visible);
                var sH2 = new State(hider.X, hider.Y, seeker.X, seeker.Y, visible);

                seekerRL.Learn(sSeek, (int)aS, rS, sS2);
                hiderRL.Learn(sHide, (int)aH, rH, sH2);
            }

            if (caught || frame >= maxFrames) Reset();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(RColor.RAYWHITE);

            DrawWorld();
            DrawAgents();
            DrawHUD();
            DrawChart((screenW - graphW) / 2, fieldSize + 25);

            Raylib.EndDrawing();
        }

        SaveTable("qtable_seeker.json", seekerQ);
        SaveTable("qtable_hider.json", hiderQ);
        Raylib.CloseWindow();
    }

    static void Reset()
    {
        if (session > 0)
        {
            rss.Add(sumS.Sum());
            rsh.Add(sumH.Sum());
        }

        session++;
        frame = 0;
        timer = 0f;
        caught = false;
        caughtFrames = 0;
        sumS[0] = sumS[1] = 0;
        sumH[0] = sumH[1] = 0;

        world.GenerateStaticGrid();

        seekers.Clear();
        hiders.Clear();

        for (int i = 0; i < 2; i++)
        {
            Agent s;
            do s = new Agent(Raylib.GetRandomValue(0, gridSize - 1), Raylib.GetRandomValue(0, gridSize - 1), true, 0);
            while (world.IsBlocked(s.X, s.Y));
            seekers.Add(s);
        }

        for (int i = 0; i < 2; i++)
        {
            Agent h;
            do h = new Agent(Raylib.GetRandomValue(0, gridSize - 1), Raylib.GetRandomValue(0, gridSize - 1), false, 180);
            while (world.IsBlocked(h.X, h.Y));
            hiders.Add(h);
        }

        SaveTable("qtable_seeker.json", seekerQ);
        SaveTable("qtable_hider.json", hiderQ);
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
                TileType.Empty => RColor.LIGHTGRAY,
                TileType.Wall => RColor.DARKGRAY,
                _ => RColor.BROWN
            };
            Raylib.DrawRectangle(gx * cellSize, gy * cellSize, cellSize, cellSize, f);
            Raylib.DrawRectangleLines(gx * cellSize, gy * cellSize, cellSize, cellSize, RColor.BLACK);
        }
    }

    static void DrawAgents()
    {
        foreach (var seeker in seekers)
        {
            DrawCone(seeker, new RColor(173, 216, 230, 80));
            DrawAgent(seeker, RColor.BLUE);
        }

        foreach (var hider in hiders)
        {
            var visible = seekers.Any(s => s.CanSee(hider, world));
            var color = visible ? RColor.YELLOW : RColor.GREEN;
            DrawCone(hider, new RColor(0, 255, 0, 40));
            DrawAgent(hider, color);
        }
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
        Raylib.DrawRectangle(0, 0, fieldSize, 12, RColor.WHITE);
        int x = 10, y = 2, fs = 8;
        Raylib.DrawText($"Session: {session}  Time: {timer:F0}s", x, y, fs, RColor.BLACK);
        Raylib.DrawText($"Seeker1: {sumS[0]:F1}", x + 160, y, fs, RColor.BLUE);
        Raylib.DrawText($"Seeker2: {sumS[1]:F1}", x + 240, y, fs, RColor.BLUE);
        Raylib.DrawText($"Hider1: {sumH[0]:F1}", x + 360, y, fs, RColor.GREEN);
        Raylib.DrawText($"Hider2: {sumH[1]:F1}", x + 440, y, fs, RColor.GREEN);
    }

    static void DrawChart(int x0, int y0)
    {
        Raylib.DrawRectangleLines(x0, y0, graphW, graphH, RColor.BLACK);
        for (int i = 1; i < 5; i++)
            Raylib.DrawLine(x0, y0 + i * graphH / 5, x0 + graphW, y0 + i * graphH / 5, RColor.LIGHTGRAY);
        for (int i = 1; i < 10; i++)
            Raylib.DrawLine(x0 + i * graphW / 10, y0, x0 + i * graphW / 10, y0 + graphH, RColor.LIGHTGRAY);

        if (rss.Count > 0)
        {
            int bar = 2, maxPts = Math.Min(100, graphW / bar);
            int n = Math.Min(rss.Count, maxPts);
            float mx = Math.Max(0.001f, rss.Concat(rsh).Max());

            for (int i = 0; i < n; i++)
            {
                int idx = rss.Count - n + i;
                int xs = x0 + i * bar;
                int ys = y0 + graphH - (int)(rss[idx] / mx * graphH);
                int yh = y0 + graphH - (int)(rsh[idx] / mx * graphH);
                Raylib.DrawLine(xs, y0 + graphH, xs, ys, new RColor(100, 150, 255, 255));
                Raylib.DrawLine(xs, y0 + graphH, xs, yh, new RColor(80, 220, 80, 255));
            }
        }

        int fs = 10;
        string xLab = "Sessions";
        Raylib.DrawText(xLab, x0 + graphW / 2 - Raylib.MeasureText(xLab, fs) / 2, y0 + graphH + 5, fs, RColor.BLACK);
        Raylib.DrawText("Reward", x0 - 40, y0 + graphH / 2 - 20, fs, RColor.BLACK);
    }

    static void DrawFilledVisionCone(Agent agent, int cell, RColor col, World w,
                                     float stepDeg = 1f, float stepPix = 2f, float thick = 3f)
    {
        Vector2 c = new(agent.X * cell + cell / 2, agent.Y * cell + cell / 2);
        float maxR = cell * agent.VisionRadius;
        float a0 = agent.Angle - agent.VisionAngle / 2;
        float a1 = agent.Angle + agent.VisionAngle / 2;

        Raylib.BeginBlendMode(BlendMode.BLEND_ALPHA);
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
            if (map != null) q.LoadFrom(map);
        }
        catch { }
    }

    static void SaveTable(string path, QTable q) =>
        File.WriteAllText(path, JsonConvert.SerializeObject(q.Export(), Formatting.None, jsonSettings));
}
