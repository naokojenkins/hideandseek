using System;
using System.Numerics;
using System.Collections.Generic;
using Raylib_cs;
using ToolUse.Core.Config;

namespace ToolUse.Core.RaylibThreeD
{
    public class World3D
    {
        public TileType[,] Grid { get; }
        public int Size { get; }
        public float CellSize { get; set; }
        public float WallHeight { get; set; }
        public Color FloorColor { get; set; } = new Color(200, 200, 200, 255); // светло-серый
        public Color WallColor { get; set; } = new Color(80, 80, 80, 255);     // тёмно-серый
        public int RoomSize { get; set; } = 8;

        private readonly Random _rng = new();

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            var config = GameConfig.Load();
            CellSize = config.World.CellSize;
            WallHeight = config.World.WallHeight;
            RoomSize = config.World.RoomSize;
            if (RoomSize >= Size - 2)
            {
                RoomSize = Math.Max(4, Size / 4);
                Console.WriteLine($"[WARNING] RoomSize слишком большой для поля {Size}x{Size}, установлен в {RoomSize}");
            }
            GenerateStaticGrid();
        }

        public bool IsInside(int x, int z) =>
            x >= 0 && z >= 0 && x < Size && z < Size;

        public bool IsBlocked(int x, int z) =>
            !IsInside(x, z) || Grid[x, z] == TileType.Wall;

        public void GenerateStaticGrid()
        {
            // Все клетки — пустые
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    Grid[x, z] = TileType.Empty;

            // Рамка стен по краям
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

            // Внутренние стены — если поле достаточно большое
            if (Size > RoomSize * 2)
            {
                int roomsInRow = Size / RoomSize;

                // Горизонтальные стены
                for (int roomY = 1; roomY < roomsInRow; roomY++)
                {
                    int z = roomY * RoomSize;
                    if (z >= Size - 1) break;

                    for (int x = 1; x < Size - 1; x++)
                        Grid[x, z] = TileType.Wall;

                    for (int i = 0; i < roomsInRow && i * RoomSize < Size; i++)
                    {
                        int gapStart = i * RoomSize + 1;
                        int gapEnd = Math.Min(gapStart + RoomSize - 2, Size - 2);
                        if (gapStart < gapEnd)
                        {
                            int gapX = _rng.Next(gapStart, gapEnd);
                            Grid[gapX, z] = TileType.Empty;
                            if (gapX + 1 < Size - 1) Grid[gapX + 1, z] = TileType.Empty;
                            if (gapX - 1 > 0) Grid[gapX - 1, z] = TileType.Empty;
                        }
                    }
                }

                // Вертикальные стены
                for (int roomX = 1; roomX < roomsInRow; roomX++)
                {
                    int x = roomX * RoomSize;
                    if (x >= Size - 1) break;

                    for (int z = 1; z < Size - 1; z++)
                        Grid[x, z] = TileType.Wall;

                    for (int i = 0; i < roomsInRow && i * RoomSize < Size; i++)
                    {
                        int gapStart = i * RoomSize + 1;
                        int gapEnd = Math.Min(gapStart + RoomSize - 2, Size - 2);
                        if (gapStart < gapEnd)
                        {
                            int gapZ = _rng.Next(gapStart, gapEnd);
                            Grid[x, gapZ] = TileType.Empty;
                            if (gapZ + 1 < Size - 1) Grid[x, gapZ + 1] = TileType.Empty;
                            if (gapZ - 1 > 0) Grid[x, gapZ - 1] = TileType.Empty;
                        }
                    }
                }
            }

            // Для отладки: считаем число свободных клеток
            int emptyCount = 0;
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    if (Grid[x, z] == TileType.Empty)
                        emptyCount++;
            Console.WriteLine($"[DEBUG] Мир {Size}x{Size} сгенерирован: {emptyCount} свободных клеток, RoomSize={RoomSize}");
        }

        public void Draw(bool showShadows = true)
        {
            // Рисуем пол
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                    {
                        Color tileColor = ((x + z) % 2 == 0)
                            ? Brightness(FloorColor, 0.93f)
                            : Brightness(FloorColor, 1.10f);

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
                        Raylib.DrawCube(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, WallColor);
                        Raylib.DrawCubeWires(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, new Color(0, 0, 0, 255));
                        if (showShadows)
                        {
                            Raylib.DrawCube(
                                new Vector3(x + 0.5f, -0.05f, z + 0.5f),
                                CellSize * 1.1f, 0.1f, CellSize * 1.1f,
                                Brightness(WallColor, 0.5f)
                            );
                        }
                    }
                }
            }
        }

        public void DrawGrid()
        {
            // Сетка на уровне пола
            for (int x = 0; x <= Size; x++)
                Raylib.DrawLine3D(
                    new Vector3(x, 0.01f, 0),
                    new Vector3(x, 0.01f, Size),
                    new Color(60, 60, 60, 255));
            for (int z = 0; z <= Size; z++)
                Raylib.DrawLine3D(
                    new Vector3(0, 0.01f, z),
                    new Vector3(Size, 0.01f, z),
                    new Color(60, 60, 60, 255));
        }

        public Vector3 GetRandomEmptyPosition(float heightOffset = 0f)
        {
            var emptyPositions = new List<Vector2>();
            for (int x = 1; x < Size - 1; x++)
                for (int z = 1; z < Size - 1; z++)
                    if (Grid[x, z] == TileType.Empty)
                        emptyPositions.Add(new Vector2(x, z));

            if (emptyPositions.Count == 0)
            {
                Console.WriteLine("[ERROR] No empty positions found in world!");
                return new Vector3(Size / 2f, heightOffset, Size / 2f);
            }

            var randomPos = emptyPositions[_rng.Next(emptyPositions.Count)];
            return new Vector3(randomPos.X + 0.5f, heightOffset, randomPos.Y + 0.5f);
        }

        public Vector3 GetRandomEmptyPositionFarFrom(Vector3 otherPosition, float minDistance, float heightOffset = 0f)
        {
            var emptyPositions = new List<Vector2>();
            for (int x = 1; x < Size - 1; x++)
                for (int z = 1; z < Size - 1; z++)
                    if (Grid[x, z] == TileType.Empty)
                    {
                        var pos = new Vector3(x + 0.5f, heightOffset, z + 0.5f);
                        if (Vector3.Distance(pos, otherPosition) >= minDistance)
                            emptyPositions.Add(new Vector2(x, z));
                    }

            if (emptyPositions.Count == 0)
            {
                Console.WriteLine($"[WARNING] No empty positions found far enough ({minDistance}) from other position, using any empty position");
                return GetRandomEmptyPosition(heightOffset);
            }

            var randomPos = emptyPositions[_rng.Next(emptyPositions.Count)];
            return new Vector3(randomPos.X + 0.5f, heightOffset, randomPos.Y + 0.5f);
        }

        /// <summary>
        /// Линия прямой видимости: возвращает true если нет стены между точками.
        /// </summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 direction = Vector3.Normalize(to - from);
            float distance = Vector3.Distance(from, to);
            float step = 0.1f;

            for (float t = 0; t < distance; t += step)
            {
                Vector3 point = from + direction * t;
                int x = Math.Clamp((int)MathF.Floor(point.X), 0, Size - 1);
                int z = Math.Clamp((int)MathF.Floor(point.Z), 0, Size - 1);
                if (IsBlocked(x, z))
                    return false;
            }
            return true;
        }

        // Яркость цвета (новый API: R,G,B,A)
        private static Color Brightness(Color color, float factor)
        {
            byte r = (byte)Math.Clamp((int)(color.R * factor), 0, 255);
            byte g = (byte)Math.Clamp((int)(color.G * factor), 0, 255);
            byte b = (byte)Math.Clamp((int)(color.B * factor), 0, 255);
            byte a = color.A;
            return new Color(r, g, b, a);
        }
    }
}
