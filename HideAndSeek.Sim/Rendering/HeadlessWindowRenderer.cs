using HideAndSeek.Core.Rendering;

namespace ToolUse.Sim.Rendering
{
    /// <summary>
    /// No-op renderer used for headless runs (e.g., CI). Implements IWindowRenderer
    /// so the application can uniformly depend on the interface without referencing Raylib.
    /// </summary>
    public sealed class HeadlessWindowRenderer : IWindowRenderer
    {
        public void InitWindow(int width, int height, string title) { /* no-op */ }
        public void SetTargetFps(int fps) { /* no-op */ }
        public bool WindowShouldClose() => false;
        public bool IsWindowReady() => false;
        public void BeginDrawing() { /* no-op */ }
        public void ClearBackground(ColorRgba color) { /* no-op */ }
        public void EndDrawing() { /* no-op */ }
        public void CloseWindow() { /* no-op */ }
        public int MeasureText(string text, int fontSize) => text?.Length ?? 0;
        public void DrawText(string text, int x, int y, int fontSize, ColorRgba color) { /* no-op */ }
    }
}
