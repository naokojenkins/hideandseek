using System;

namespace HideAndSeek.Core.Rendering
{
    /// <summary>
    /// Abstraction over windowing and 2D overlay drawing used by the simulator UI loop.
    /// Keeps core logic independent from specific graphics libs (Raylib, etc.).
    /// </summary>
    public interface IWindowRenderer
    {
        void InitWindow(int width, int height, string title);
        void SetTargetFps(int fps);
        bool WindowShouldClose();
        bool IsWindowReady();
        void BeginDrawing();
        void ClearBackground(ColorRgba color);
        void EndDrawing();
        void CloseWindow();
        int MeasureText(string text, int fontSize);
        void DrawText(string text, int x, int y, int fontSize, ColorRgba color);
    }

    /// <summary>
    /// Small cross-library color struct to avoid referencing specific graphics types in Core.
    /// </summary>
    public readonly struct ColorRgba
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public ColorRgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        public static ColorRgba White => new ColorRgba(255, 255, 255, 255);
        public static ColorRgba Black => new ColorRgba(0, 0, 0, 255);
        public static ColorRgba LightGray => new ColorRgba(245, 245, 245, 255);
    }
}
