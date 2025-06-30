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
    static QTable seekerQ = new(), hiderQ = new();
    static QAgent seekerRL = new(seekerQ, 0.1f);
    static QAgent hiderRL  = new(hiderQ , 0.1f);

    const int cellSize  = 16;
    const int gridSize  = 40;
    const int fieldSize = cellSize * gridSize;

    const int graphW = 280, graphH = 100;
    const int padX   = 0 , padY   = 50;

    const int screenW = fieldSize;
    const int screenH = fieldSize + padY + graphH + 20;

    const float sessSec = 60f;
    const int   FPS     = 5;
    const int   maxFrames = (int)(sessSec * FPS);

    static JsonSerializerSettings jsonSettings = new()
    {
        Converters = new List<JsonConverter> { new StateKeyConverter() }
    };

    static World world = new(gridSize);
    static Agent seeker, hider;
    static int session = 0, frame = 0, caughtFrames = 0;
    static float timer = 0f;
    static bool caught = false;

    static List<float> rss = new(); // seeker rewards
    static List<float> rsh = new(); // hider rewards
    static float sumS = 0f, sumH = 0f;

    enum Act { RotL, RotR, Fwd }

    static void Main()
    {
        LoadTable("qtable_seeker.json", seekerQ);
        LoadTable("qtable_hider.json", hiderQ);

        Raylib.InitWindow(screenW, screenH, "Tool Use – angular movement");
        Raylib.SetTargetFPS(FPS);

        world.GenerateStaticGrid();
        Reset();

        while (!Raylib.WindowShouldClose())
        {
            frame++; timer += 1f / FPS;

            State sSeek = new(seeker.X, seeker.Y, hider.X, hider.Y,
                              seeker.CanSee(hider, world));
            State sHide = new(hider.X , hider.Y , seeker.X, seeker.Y,
                              seeker.CanSee(hider, world));

            Act aH = (Act)hiderRL .ChooseAction(sHide);
            Act aS = (Act)seekerRL.ChooseAction(sSeek);

            ApplyAction(hider , aH);
            ApplyAction(seeker, aS);

            bool visible = seeker.CanSee(hider, world);

            float rS = 0, rH = 0;
            if (visible)
            {
                rS += 0.1f;  rH -= 0.1f;
                if (++caughtFrames >= FPS * 10) { rS += 1; rH -= 1; caught = true; }
            }
            else { rH += 0.1f; caughtFrames = 0; }

            if (frame >= maxFrames) rH += 1;

            sumS += rS; sumH += rH;

            State sS2 = new(seeker.X, seeker.Y, hider.X, hider.Y, visible);
            State sH2 = new(hider.X , hider.Y , seeker.X, seeker.Y, visible);

            seekerRL.Learn(sSeek, (int)aS, rS, sS2);
            hiderRL .Learn(sHide, (int)aH, rH, sH2);

            if (caught || frame >= maxFrames) Reset();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(RColor.RAYWHITE);

            for (int gx = 0; gx < gridSize; gx++)
            for (int gy = 0; gy < gridSize; gy++)
            {
                var t = world.Grid[gx, gy];
                RColor f = t switch
                {
                    TileType.Empty  => RColor.LIGHTGRAY,
                    TileType.Wall   => RColor.DARKGRAY,
                    _               => RColor.BROWN
                };
                Raylib.DrawRectangle(gx * cellSize, gy * cellSize, cellSize, cellSize, f);
                Raylib.DrawRectangleLines(gx * cellSize, gy * cellSize, cellSize, cellSize, RColor.BLACK);
            }

            DrawFilledVisionCone(seeker, cellSize, new RColor(173, 216, 230, 80), world);
            DrawFilledVisionCone(hider , cellSize, new RColor(  0, 255,   0, 40), world);

            int pad = 4, sz = cellSize - 2 * pad;
            RColor hiderClr = visible ? RColor.YELLOW : RColor.GREEN;
            Raylib.DrawRectangle(seeker.X * cellSize + pad, seeker.Y * cellSize + pad, sz, sz, RColor.BLUE);
            Raylib.DrawRectangle(hider .X * cellSize + pad, hider .Y * cellSize + pad, sz, sz, hiderClr);

            if (visible)
                Raylib.DrawLine(seeker.X * cellSize + cellSize / 2, seeker.Y * cellSize + cellSize / 2,
                                hider .X * cellSize + cellSize / 2, hider .Y * cellSize + cellSize / 2,
                                RColor.RED);

            Raylib.DrawRectangle(0, 0, fieldSize, 12, RColor.WHITE);
            Raylib.DrawText($"Session: {session}  Time: {timer:F0}s", 10, 2, 8, RColor.BLACK);

            DrawRewardChart((screenW - graphW) / 2, fieldSize + 25);
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
            rss.Add(sumS);
            rsh.Add(sumH);
        }

        session++;  frame = 0; timer = 0;
        caught = false; caughtFrames = 0;
        sumS = sumH = 0;

        world.GenerateStaticGrid();

        do seeker = new Agent(Raylib.GetRandomValue(0, gridSize-1),
                              Raylib.GetRandomValue(0, gridSize-1), true, 0);
        while (world.IsBlocked(seeker.X, seeker.Y));

        do hider = new Agent(Raylib.GetRandomValue(0, gridSize-1),
                             Raylib.GetRandomValue(0, gridSize-1), false, 180);
        while (world.IsBlocked(hider.X, hider.Y));

        SaveTable("qtable_seeker.json", seekerQ);
        SaveTable("qtable_hider.json", hiderQ);
    }

    static void ApplyAction(Agent ag, Act act)
    {
        switch (act)
        {
            case Act.RotL: ag.Rotate(-15f); break;
            case Act.RotR: ag.Rotate(+15f); break;
            case Act.Fwd : ag.MoveForward(world); break;
        }
    }

    static void DrawRewardChart(int x0, int y0)
    {
        Raylib.DrawRectangleLines(x0, y0, graphW, graphH, RColor.BLACK);
        for (int i = 1; i < 5; i++)
            Raylib.DrawLine(x0, y0 + i * graphH / 5, x0 + graphW, y0 + i * graphH / 5, RColor.LIGHTGRAY);
        for (int i = 1; i < 10; i++)
            Raylib.DrawLine(x0 + i * graphW / 10, y0, x0 + i * graphW / 10, y0 + graphH, RColor.LIGHTGRAY);

        if (rss.Count > 0)
        {
            int bar = 2;
            int maxPts = Math.Min(100, graphW / bar);
            int n = Math.Min(rss.Count, maxPts);
            float mx = Math.Max(0.001f, rss.Concat(rsh).Max());

            for (int i = 0; i < n; i++)
            {
                int idx = rss.Count - n + i;
                int xs  = x0 + i * bar;

                int ys = y0 + graphH - (int)(rss[idx] / mx * graphH);
                int yh = y0 + graphH - (int)(rsh[idx] / mx * graphH);

                Raylib.DrawLine(xs, y0 + graphH, xs, ys, new RColor(100, 150, 255, 255));
                Raylib.DrawLine(xs, y0 + graphH, xs, yh, new RColor( 80, 220,  80, 255));
            }
        }

        int fs = 10;
        string xLab = "Sessions";
        Raylib.DrawText(xLab, x0 + graphW / 2 - Raylib.MeasureText(xLab, fs) / 2, y0 + graphH + 5, fs, RColor.BLACK);

        string yLab = "Reward";
        Vector2 pos = new(x0 - 32, y0 + graphH / 2);
        Vector2 origin = new(0, Raylib.MeasureText(yLab, fs) / 2f);
        Raylib.DrawTextPro(Raylib.GetFontDefault(), yLab, pos, origin, -90f, fs, 1, RColor.BLACK);
    }

    static void DrawFilledVisionCone(Agent agent, int cell, RColor col, World w,
                                     float stepDeg = 1f, float stepPix = 2f, float thick = 3f)
    {
        Vector2 c = new(agent.X * cell + cell / 2, agent.Y * cell + cell / 2);
        float maxR = cell * agent.VisionRadius;
        float a0   = agent.Angle - agent.VisionAngle / 2;
        float a1   = agent.Angle + agent.VisionAngle / 2;

        Raylib.BeginBlendMode(BlendMode.BLEND_ALPHA);
        for (float ang = a0; ang <= a1; ang += stepDeg)
        {
            float r = ang * MathF.PI / 180f;
            float dx = MathF.Cos(r), dy = MathF.Sin(r), dist = 0;

            while (dist < maxR)
            {
                int gxi = (int)((c.X + dx * dist) / cell);
                int gyi = (int)((c.Y + dy * dist) / cell);

                if (!w.IsInside(gxi, gyi) || w.Grid[gxi, gyi] == TileType.Wall)
                {
                    dist -= stepPix; break;
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
            var map = JsonConvert.DeserializeObject<Dictionary<State, float[]>>(
                File.ReadAllText(path), jsonSettings);
            if (map != null) q.LoadFrom(map);
        }
        catch { }
    }

    static void SaveTable(string path, QTable q) =>
        File.WriteAllText(path,
            JsonConvert.SerializeObject(q.Export(), Formatting.None, jsonSettings));
}
