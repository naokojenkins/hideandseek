using System;
using System.Collections.Generic;

namespace ToolUse.Core
{
    public class World
    {
        public TileType[,] Grid { get; private set; }
        public int Size { get; }

        public Agent Seeker { get; private set; }
        public Agent Hider { get; private set; }

        private Random rand = new();

        public World(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
        }

        public void GenerateStaticGrid()
        {
            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                Grid[x, y] = TileType.Empty;

            for (int i = 0; i < Size; i++)
            {
                Grid[i, 0] = TileType.Wall;
                Grid[i, Size - 1] = TileType.Wall;
                Grid[0, i] = TileType.Wall;
                Grid[Size - 1, i] = TileType.Wall;
            }

            for (int i = 0; i < Size * 2; i++)
            {
                int x = rand.Next(1, Size - 1);
                int y = rand.Next(1, Size - 1);
                Grid[x, y] = rand.NextDouble() < 0.8 ? TileType.Wall : TileType.Object;
            }
        }

        public void PlaceAgents()
        {
            Seeker = new Agent(1, 1, true);
            Hider = new Agent(Size - 2, Size - 2, false);
        }

        public bool IsInside(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

        public bool IsBlocked(int x, int y)
        {
            if (!IsInside(x, y)) return true;
            return Grid[x, y] == TileType.Wall;
        }

        public bool HasLineOfSight(Agent from, Agent to)
        {
            int x0 = from.X, y0 = from.Y;
            int x1 = to.X, y1 = to.Y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 == x1 && y0 == y1)
                    return true;
                if (Grid[x0, y0] == TileType.Wall)
                    return false;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }


} 
