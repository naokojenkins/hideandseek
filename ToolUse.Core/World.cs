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
        public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

        public bool IsBlocked(int x, int y) =>
            !IsInside(x, y) || Grid[x, y] == TileType.Wall;

        /* ─────────────────────────────────────────────────────────────
         *  recursive-backtracking: идём по нечётным координатам,
         *  «вырезая» коридоры шириной 1 клетка.
         *  Размер поля должен быть нечётным (для простоты).
         * ──────────────────────────────────────────────────────────── */
        public void GenerateStaticGrid()
        {
            // 1. всё заполняем стенами
            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                Grid[x, y] = TileType.Wall;

            // 2. стартовая точка (1,1)
            Carve(1, 1);

            // 3. пробиваем дополнительные проходы
            AddExtraConnections(probability: 0.2); // 20% шанс на пробитие стены

            // 4. внешняя рамка
            for (int i = 0; i < Size; i++)
            {
                Grid[i, 0] = Grid[i, Size - 1] = TileType.Wall;
                Grid[0, i] = Grid[Size - 1, i] = TileType.Wall;
            }
        }
        
        private void AddExtraConnections(double probability)
        {
            for (int x = 1; x < Size - 1; x++)
            for (int y = 1; y < Size - 1; y++)
            {
                if (Grid[x, y] != TileType.Wall) continue;

                bool verticalWall =
                    Grid[x, y - 1] == TileType.Empty &&
                    Grid[x, y + 1] == TileType.Empty;

                bool horizontalWall =
                    Grid[x - 1, y] == TileType.Empty &&
                    Grid[x + 1, y] == TileType.Empty;

                if ((verticalWall || horizontalWall) && _rng.NextDouble() < probability)
                {
                    Grid[x, y] = TileType.Empty; // пробиваем
                }
            }
        }



        /* рекурсивное «вырезание» */
        private void Carve(int cx, int cy)
        {
            Grid[cx, cy] = TileType.Empty;

            // случайный порядок четырёх направлений
            var dirs = new List<(int dx, int dy)>
            {
                ( 0, -2), // вверх
                ( 2,  0), // вправо
                ( 0,  2), // вниз
                (-2,  0)  // влево
            };
            Shuffle(dirs);

            foreach (var (dx, dy) in dirs)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (IsInside(nx, ny) && Grid[nx, ny] == TileType.Wall)
                {
                    // «прорубаем» стену между клетками
                    Grid[cx + dx / 2, cy + dy / 2] = TileType.Empty;
                    Carve(nx, ny);
                }
            }
        }

        /* Fisher-Yates */
        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
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
                if (e2 <  dx) { err += dx; y0 += sy; }
            }
        }
    }
}
