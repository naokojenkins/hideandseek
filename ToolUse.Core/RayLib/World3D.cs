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
        public Color FloorColor { get; set; }
        public Color WallColor { get; set; }
        public int RoomSize { get; set; }

        private readonly Random _rng = ToolUse.Core.Config.Reproducibility.CreateRandom("World3D");

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            var config = GameConfig.Instance.World;
            CellSize = config.CellSize;
            WallHeight = config.WallHeight;
            RoomSize = config.RoomSize;

            // Цвета также берём из конфига, если добавлены:
            FloorColor = new Color(200, 200, 200, 255); // По умолчанию (можно взять из config.FloorColor, если нужно)
            WallColor  = new Color(80, 80, 80, 255);    // По умолчанию (можно взять из config.WallColor, если нужно)

            if (RoomSize >= Size - 2)
            {
                RoomSize = Math.Max(4, Size / 4);
                Console.WriteLine($"[WARNING] RoomSize слишком большой для поля {Size}x{Size}, установлен в {RoomSize}");
            }
            GenerateStaticGrid();
        }

        public Vector3 GetRandomValidAgentPosition(float agentRadius, float heightOffset = 0f)
        {
            var validPositions = new List<Vector2>();
            for (int x = 1; x < Size - 1; x++)
            for (int z = 1; z < Size - 1; z++)
                if (Grid[x, z] == TileType.Empty && IsAreaFree(x + 0.5f, z + 0.5f, agentRadius))
                    validPositions.Add(new Vector2(x, z));

            if (validPositions.Count == 0)
            {
                Console.WriteLine("[ERROR] No valid positions found for agent spawn!");
                return new Vector3(Size / 2f, heightOffset, Size / 2f);
            }

            var pos = validPositions[_rng.Next(validPositions.Count)];
            return new Vector3(pos.X + 0.5f, heightOffset, pos.Y + 0.5f);
        }

        private bool IsAreaFree(float centerX, float centerZ, float radius)
        {
            int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                float angle = (float)(2 * Math.PI * i / steps);
                float checkX = centerX + MathF.Cos(angle) * radius;
                float checkZ = centerZ + MathF.Sin(angle) * radius;
                int gx = Math.Clamp((int)Math.Floor(checkX), 0, Size - 1);
                int gz = Math.Clamp((int)Math.Floor(checkZ), 0, Size - 1);

                if (!IsInside(gx, gz) || Grid[gx, gz] == TileType.Wall)
                    return false;
            }
            return true;
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

        public bool HasLineOfSight(Vector3 from, Vector3 to, float agentRadius = 0.3f)
        {
            Vector3 direction = Vector3.Normalize(to - from);
            float distance = Vector3.Distance(from, to);
            float step = 0.2f;

            Vector3 perpendicular = new Vector3(-direction.Z, 0, direction.X);
            Vector3[] offsets = new[]
            {
                Vector3.Zero,
                perpendicular * agentRadius * 0.5f,
                -perpendicular * agentRadius * 0.5f
            };

            for (float t = 0; t < distance; t += step)
            {
                foreach (var offset in offsets)
                {
                    Vector3 point = from + direction * t + offset;
                    int x = Math.Clamp((int)MathF.Round(point.X), 0, Size - 1);
                    int z = Math.Clamp((int)MathF.Round(point.Z), 0, Size - 1);
                    if (IsBlocked(x, z))
                        return false;
                }
            }
            return true;
        }

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
