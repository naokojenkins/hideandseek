// ToolUse.Core/3D_Raylib/World3D.cs
using System;
using System.Numerics;
using Raylib_cs;

namespace ToolUse.Core.RaylibThreeD
{
    public class World3D
    {
        public TileType[,] Grid { get; }
        public int Size { get; }
        public float CellSize { get; set; } = 1.0f;
        public float WallHeight { get; set; } = 2.0f;
        public Raylib_cs.Color FloorColor { get; set; } = Raylib_cs.Color.LightGray;
        public Raylib_cs.Color WallColor { get; set; } = Raylib_cs.Color.DarkGray;

        private readonly Random _rng = new();

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            GenerateStaticGrid();
        }

        public bool IsInside(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size;

        public bool IsBlocked(int x, int z) =>
            !IsInside(x, z) || Grid[x, z] == TileType.Wall;

        public void GenerateStaticGrid()
        {
            // Используем ту же логику генерации, что и в 2D версии
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    Grid[x, z] = TileType.Empty;
                }
            }

            // Копируем логику из World.cs
            int roomSize = 8;
            int roomsInRow = Size / roomSize;

            // Горизонтальные стены
            for (int roomY = 1; roomY < roomsInRow; roomY++)
            {
                int z = roomY * roomSize;
                for (int x = 0; x < Size; x++)
                {
                    if (x > 0 && x < Size - 1)
                        Grid[x, z] = TileType.Wall;
                }

                // Проходы
                for (int i = 0; i < roomsInRow; i++)
                {
                    int gapX = i * roomSize + _rng.Next(1, roomSize - 1);
                    if (gapX >= Size - 1) gapX = Size - 2;

                    Grid[gapX, z] = TileType.Empty;
                    if (gapX + 1 < Size - 1) Grid[gapX + 1, z] = TileType.Empty;
                    if (gapX - 1 > 0) Grid[gapX - 1, z] = TileType.Empty;
                }
            }

            // Вертикальные стены
            for (int roomX = 1; roomX < roomsInRow; roomX++)
            {
                int x = roomX * roomSize;
                for (int z = 0; z < Size; z++)
                {
                    if (z > 0 && z < Size - 1)
                        Grid[x, z] = TileType.Wall;
                }

                // Проходы
                for (int i = 0; i < roomsInRow; i++)
                {
                    int gapZ = i * roomSize + _rng.Next(1, roomSize - 1);
                    if (gapZ >= Size - 1) gapZ = Size - 2;

                    Grid[x, gapZ] = TileType.Empty;
                    if (gapZ + 1 < Size - 1) Grid[x, gapZ + 1] = TileType.Empty;
                    if (gapZ - 1 > 0) Grid[x, gapZ - 1] = TileType.Empty;
                }
            }

            // Рамка по краям
            for (int x = 0; x < Size; x++)
            {
                Grid[x, 0] = TileType.Wall;
                Grid[x, Size - 1] = TileType.Wall;
            }

            for (int z = 0; z < Size; z++)
            {
                Grid[0, z] = TileType.Wall;
                Grid[Size - 1, z] = TileType.Wall;
            }

            // Проходы в рамке
            int sideGap = _rng.Next(1, Size - 2);
            Grid[sideGap, 0] = TileType.Empty;
            Grid[sideGap, Size - 1] = TileType.Empty;
            Grid[0, sideGap] = TileType.Empty;
            Grid[Size - 1, sideGap] = TileType.Empty;
        }

        public void Draw(bool showShadows = true)
        {
            // Рисуем пол - используем отдельные плитки вместо одной большой плоскости
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                    {
                        // Используем шахматный узор для пола
                        Color tileColor = ((x + z) % 2 == 0) ? 
                            Raylib.ColorBrightness(FloorColor, 0.9f) : 
                            Raylib.ColorBrightness(FloorColor, 1.1f);

                        Raylib.DrawCube(
                            new Vector3(x + 0.5f, -0.1f, z + 0.5f),
                            CellSize, 0.2f, CellSize,
                            tileColor
                        );

                        // Добавляем тонкую обводку для плиток пола
                        Raylib.DrawCubeWires(
                            new Vector3(x + 0.5f, -0.1f, z + 0.5f),
                            CellSize, 0.2f, CellSize,
                            Raylib.ColorAlpha(Raylib_cs.Color.Black, 0.3f)
                        );
                    }
                }
            }

            // Рисуем стены
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Grid[x, z] == TileType.Wall)
                    {
                        // Основная стена
                        Raylib.DrawCube(
                            new Vector3(x + 0.5f, WallHeight / 2f, z + 0.5f),
                            CellSize, WallHeight, CellSize,
                            WallColor
                        );

                        // Добавляем контур стены
                        Raylib.DrawCubeWires(
                            new Vector3(x + 0.5f, WallHeight / 2f, z + 0.5f),
                            CellSize, WallHeight, CellSize,
                            Raylib_cs.Color.Black
                        );

                        // Добавляем тень от стены на пол если включено
                        if (showShadows)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int nx = x + dx;
                                    int nz = z + dz;

                                    if (IsInside(nx, nz) && Grid[nx, nz] == TileType.Empty)
                                    {
                                        Raylib.DrawCube(
                                            new Vector3(nx + 0.5f, -0.09f, nz + 0.5f),
                                            CellSize, 0.01f, CellSize,
                                            Raylib.ColorAlpha(Raylib_cs.Color.Black, 0.2f)
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void DrawGrid()
        {
            // Рисуем сетку на полу для лучшей ориентации
            for (int x = 0; x <= Size; x++)
            {
                Raylib.DrawLine3D(
                    new Vector3(x, 0, 0),
                    new Vector3(x, 0, Size),
                    new Raylib_cs.Color(100, 100, 100, 100)
                );
            }
            
            for (int z = 0; z <= Size; z++)
            {
                Raylib.DrawLine3D(
                    new Vector3(0, 0, z),
                    new Vector3(Size, 0, z),
                    new Raylib_cs.Color(100, 100, 100, 100)
                );
            }
        }

        public bool HasLineOfSight(Agent3D from, Agent3D to)
        {
            int x0 = (int)from.Position.X, z0 = (int)from.Position.Z;
            int x1 = (int)to.Position.X, z1 = (int)to.Position.Z;

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dz = Math.Abs(z1 - z0), sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            while (true)
            {
                if (IsBlocked(x0, z0)) return false;
                if (x0 == x1 && z0 == z1) return true;

                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; x0 += sx; }
                if (e2 < dx) { err += dx; z0 += sz; }
            }
        }

        public Vector3 GetRandomEmptyPosition(float minDistanceFromWalls = 1.0f)
        {
            int attempts = 0;
            int maxAttempts = 100;

            while (attempts < maxAttempts)
            {
                int x = _rng.Next(1, Size - 1);
                int z = _rng.Next(1, Size - 1);

                bool validPosition = true;

                // Check if the position is empty
                if (Grid[x, z] != TileType.Empty)
                {
                    validPosition = false;
                }

                // Check minimum distance from walls if needed
                if (validPosition && minDistanceFromWalls > 0.5f)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int nx = x + dx;
                            int nz = z + dz;

                            if (IsInside(nx, nz) && Grid[nx, nz] == TileType.Wall)
                            {
                                validPosition = false;
                                break;
                            }
                        }

                        if (!validPosition) break;
                    }
                }

                if (validPosition)
                {
                    return new Vector3(x + 0.5f, 0, z + 0.5f);
                }

                attempts++;
            }

            // Fallback to a safe position if we couldn't find a random one
            return new Vector3(Size / 2f, 0, Size / 2f);
        }
    }
}