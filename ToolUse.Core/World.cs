using System;
using System.Collections.Generic;

namespace ToolUse.Core
{
    // public enum TileType
    // {
    //     Empty,   // свободная клетка
    //     Wall     // стена
    // }

    public class World
    {
        public TileType[,] Grid { get; }
        public int Size { get; }

        private readonly Random _rng = new();

        /* ────── ctor ────── */
        public World(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            GenerateStaticGrid();
        }

        /* ────── общие проверки ────── */
        public bool IsInside(int x, int y) =>
            x >= 0 && y >= 0 && x < Size && y < Size;

        public bool IsBlocked(int x, int y) =>
            !IsInside(x, y) || Grid[x, y] == TileType.Wall;

        /* ─────────────────────────────────────────────────────────────
         *  Генерация простого и полностью проходимого лабиринта
         * ──────────────────────────────────────────────────────────── */
 public void GenerateStaticGrid()
{
    // Инициализируем всё как пустые ячейки
    for (int x = 0; x < Size; x++)
    {
        for (int y = 0; y < Size; y++)
        {
            Grid[x, y] = TileType.Empty;
        }
    }

    // === Генерация внутренних стен (как раньше) ===
    int roomSize = 8;
    int roomsInRow = Size / roomSize;

    for (int roomY = 1; roomY < roomsInRow; roomY++)
    {
        int y = roomY * roomSize;

        // Горизонтальная стена
        for (int x = 0; x < Size; x++)
        {
            if (x > 0 && x < Size - 1)
                Grid[x, y] = TileType.Wall;
        }

        // Проходы в горизонтальной стене
        for (int i = 0; i < roomsInRow; i++)
        {
            int gapX = i * roomSize + _rng.Next(1, roomSize - 1);
            if (gapX >= Size - 1) gapX = Size - 2;

            Grid[gapX, y] = TileType.Empty;
            if (gapX + 1 < Size - 1) Grid[gapX + 1, y] = TileType.Empty;
            if (gapX - 1 > 0) Grid[gapX - 1, y] = TileType.Empty;
        }
    }

    for (int roomX = 1; roomX < roomsInRow; roomX++)
    {
        int x = roomX * roomSize;

        // Вертикальная стена
        for (int y = 0; y < Size; y++)
        {
            if (y > 0 && y < Size - 1)
                Grid[x, y] = TileType.Wall;
        }

        // Проходы в вертикальной стене
        for (int i = 0; i < roomsInRow; i++)
        {
            int gapY = i * roomSize + _rng.Next(1, roomSize - 1);
            if (gapY >= Size - 1) gapY = Size - 2;

            Grid[x, gapY] = TileType.Empty;
            if (gapY + 1 < Size - 1) Grid[x, gapY + 1] = TileType.Empty;
            if (gapY - 1 > 0) Grid[x, gapY - 1] = TileType.Empty;
        }
    }

    // === Добавляем рамку только по краям всего игрового поля ===
    for (int x = 0; x < Size; x++)
    {
        Grid[x, 0] = TileType.Wall;               // Верхняя граница
        Grid[x, Size - 1] = TileType.Wall;        // Нижняя граница
    }

    for (int y = 0; y < Size; y++)
    {
        Grid[0, y] = TileType.Wall;               // Левая граница
        Grid[Size - 1, y] = TileType.Wall;        // Правая граница
    }

    // === Делаем один проход в каждой стороне рамки (по желанию) ===
    int sideGap = _rng.Next(1, Size - 2);

    // Верхняя и нижняя — проходы
    Grid[sideGap, 0] = TileType.Empty;
    Grid[sideGap, Size - 1] = TileType.Empty;

    // Левая и правая — проходы
    Grid[0, sideGap] = TileType.Empty;
    Grid[Size - 1, sideGap] = TileType.Empty;
}

        /* ────── LOS (брезенхем) ────── */
        public bool HasLineOfSight(Agent from, Agent to)
        {
            int x0 = from.X, y0 = from.Y;
            int x1 = to.X,   y1 = to.Y;

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (Grid[x0, y0] == TileType.Wall) return false;
                if (x0 == x1 && y0 == y1) return true;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}