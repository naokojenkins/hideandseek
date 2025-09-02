using Raylib_cs;
using HideAndSeek.Core.Rendering;

namespace ToolUse.Sim.Rendering
{
    public class RaylibWindowRenderer : IWindowRenderer
    {
        public void InitWindow(int width, int height, string title)
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(width, height, title);
        }

        public void SetTargetFps(int fps)
        {
            Raylib.SetTargetFPS(fps);
        }

        public bool WindowShouldClose() => Raylib.WindowShouldClose();
        public bool IsWindowReady() => Raylib.IsWindowReady();
        public void BeginDrawing() => Raylib.BeginDrawing();
        public void EndDrawing() => Raylib.EndDrawing();
        public void CloseWindow() => Raylib.CloseWindow();

        public void ClearBackground(ColorRgba color)
        {
            Raylib.ClearBackground(new Color(color.R, color.G, color.B, color.A));
        }

        public int MeasureText(string text, int fontSize) => Raylib.MeasureText(text, fontSize);

        public void DrawText(string text, int x, int y, int fontSize, ColorRgba color)
        {
            Raylib.DrawText(text, x, y, fontSize, new Color(color.R, color.G, color.B, color.A));
        }
    }
}
