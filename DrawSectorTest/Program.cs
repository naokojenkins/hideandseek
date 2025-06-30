using System.Drawing;
using Raylib_cs;
using System.Numerics;

Raylib.InitWindow(800, 800, "DrawCircleSector Test");
Raylib.SetTargetFPS(60);

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Raylib_cs.Color.RayWhite);

    Vector2 center = new(400, 400);
    float radius = 100f;
    float angle = 90f;

    Raylib.DrawText("Testing DrawCircleSector...", 10, 10, 20, Raylib_cs.Color.Black);

    Raylib.DrawCircleSector(center, radius, angle - 45f, angle + 45f, 30, new Raylib_cs.Color(0, 0, 255, 120));

    Raylib.EndDrawing();
}

Raylib.CloseWindow();