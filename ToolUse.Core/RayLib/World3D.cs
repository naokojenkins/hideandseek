
// ToolUse.Core/3D_Raylib/World3D.cs
using System;
using System.Numerics;
using Raylib_cs;
using ToolUse.Core.Config;

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
        public int RoomSize { get; set; } = 8;

        private readonly Random _rng = new();

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            
            // Загружаем параметры из конфигурации
            var config = GameConfig.Load();
            CellSize = config.World.CellSize;
            WallHeight = config.World.WallHeight;
            RoomSize = config.World.RoomSize;
            
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

            // Используем параметр RoomSize из конфигурации
            int roomsInRow = Size / RoomSize;

            // Горизонтальные стены
            for (int roomY = 1; roomY < roomsInRow; roomY++)
            {
                int z = roomY * RoomSize;
                for (int x = 0; x < Size; x++)
                {
                    if (x > 0 && x < Size - 1)
                        Grid[x, z] = TileType.Wall;
                }

                // Проходы
                for (int i = 0; i < roomsInRow; i++)
                {
                    int gapX = i * RoomSize + _rng.Next(1, RoomSize - 1);
                    if (gapX >= Size - 1) gapX = Size - 2;

                    Grid[gapX, z] = TileType.Empty;
                    if (gapX + 1 < Size - 1) Grid[gapX + 1, z] = TileType.Empty;
                    if (gapX - 1 > 0) Grid[gapX - 1, z] = TileType.Empty;
                }
            }

            // Вертикальные стены
            for (int roomX = 1; roomX < roomsInRow; roomX++)
            {
                int x = roomX * RoomSize;
                for (int z = 0; z < Size; z++)
                {
                    if (z > 0 && z < Size - 1)
                        Grid[x, z] = TileType.Wall;
                }

                // Проходы
                for (int i = 0; i < roomsInRow; i++)
                {
                    int gapZ = i * RoomSize + _rng.Next(1, RoomSize - 1);
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
                        Vector3 wallPosition = new Vector3(x + 0.5f, WallHeight / 2, z + 0.5f);
                        Vector3 wallSize = new Vector3(CellSize, WallHeight, CellSize);

                        // Основная стена
                        Raylib.DrawCube(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, WallColor);

                        // Добавляем черные контуры стены
                        Raylib.DrawCubeWires(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, Color.Black);

                        if (showShadows)
                        {
                            // Тень под стеной
                            Raylib.DrawCube(
                                new Vector3(x + 0.5f, -0.05f, z + 0.5f),
                                CellSize * 1.1f, 0.1f, CellSize * 1.1f,
                                Raylib.ColorBrightness(WallColor, 0.5f)
                            );
                        }
                    }
                }
            }
        }

        public void DrawGrid()
        {
            // Рисуем сетку на уровне пола
            for (int x = 0; x <= Size; x++)
            {
                Raylib.DrawLine3D(
                    new Vector3(x, 0.01f, 0),
                    new Vector3(x, 0.01f, Size),
                    Color.DarkGray
                );
            }

            for (int z = 0; z <= Size; z++)
            {
                Raylib.DrawLine3D(
                    new Vector3(0, 0.01f, z),
                    new Vector3(Size, 0.01f, z),
                    Color.DarkGray
                );
            }
        }

        public Vector3 GetRandomEmptyPosition(float heightOffset = 0f)
        {
            int maxAttempts = 1000; // Увеличиваем количество попыток
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                int x = _rng.Next(1, Size - 1);
                int z = _rng.Next(1, Size - 1);

                if (Grid[x, z] == TileType.Empty)
                {
                    return new Vector3(x + 0.5f, heightOffset, z + 0.5f);
                }

                attempts++;
            }

            // Если не нашли свободное место, ищем первое доступное
            for (int x = 1; x < Size - 1; x++)
            {
                for (int z = 1; z < Size - 1; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                    {
                        Console.WriteLine($"[WARNING] Fallback position used: ({x}, {z})");
                        return new Vector3(x + 0.5f, heightOffset, z + 0.5f);
                    }
                }
            }

            // Критическая ошибка - нет свободных мест
            Console.WriteLine("[ERROR] No empty positions found in world!");
            return new Vector3(Size / 2f, heightOffset, Size / 2f);
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 direction = Vector3.Normalize(to - from);
            float distance = Vector3.Distance(from, to);
            float step = 0.1f;

            for (float t = 0; t < distance; t += step)
            {
                Vector3 point = from + direction * t;
                int x = (int)Math.Floor(point.X);
                int z = (int)Math.Floor(point.Z);

                if (IsBlocked(x, z))
                {
                    return false;
                }
            }

            return true;
        }
    }
}