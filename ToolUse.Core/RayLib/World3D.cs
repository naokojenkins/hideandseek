
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
            
            // Если размер комнаты слишком большой для поля, уменьшим его
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
            // Инициализируем все клетки как пустые
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    Grid[x, z] = TileType.Empty;
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

            // Только если поле достаточно большое, создаем внутренние стены
            if (Size > RoomSize * 2)
            {
                int roomsInRow = Size / RoomSize;

                // Горизонтальные стены
                for (int roomY = 1; roomY < roomsInRow; roomY++)
                {
                    int z = roomY * RoomSize;
                    if (z >= Size - 1) break;
                    
                    for (int x = 1; x < Size - 1; x++)
                    {
                        Grid[x, z] = TileType.Wall;
                    }

                    // Проходы
                    for (int i = 0; i < roomsInRow && i * RoomSize < Size; i++)
                    {
                        int gapStart = i * RoomSize + 1;
                        int gapEnd = Math.Min(gapStart + RoomSize - 2, Size - 2);
                        
                        if (gapStart < gapEnd)
                        {
                            int gapX = _rng.Next(gapStart, gapEnd);
                            Grid[gapX, z] = TileType.Empty;
                            
                            // Расширяем проход
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
                    {
                        Grid[x, z] = TileType.Wall;
                    }

                    // Проходы
                    for (int i = 0; i < roomsInRow && i * RoomSize < Size; i++)
                    {
                        int gapStart = i * RoomSize + 1;
                        int gapEnd = Math.Min(gapStart + RoomSize - 2, Size - 2);
                        
                        if (gapStart < gapEnd)
                        {
                            int gapZ = _rng.Next(gapStart, gapEnd);
                            Grid[x, gapZ] = TileType.Empty;
                            
                            // Расширяем проход
                            if (gapZ + 1 < Size - 1) Grid[x, gapZ + 1] = TileType.Empty;
                            if (gapZ - 1 > 0) Grid[x, gapZ - 1] = TileType.Empty;
                        }
                    }
                }
            }
            
            // Подсчитываем свободные клетки для отладки
            int emptyCount = 0;
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                        emptyCount++;
                }
            }
            Console.WriteLine($"[DEBUG] Мир {Size}x{Size} сгенерирован: {emptyCount} свободных клеток, RoomSize={RoomSize}");
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
            // Сначала собираем все свободные позиции
            var emptyPositions = new List<Vector2>();
            
            for (int x = 1; x < Size - 1; x++)
            {
                for (int z = 1; z < Size - 1; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                    {
                        emptyPositions.Add(new Vector2(x, z));
                    }
                }
            }

            if (emptyPositions.Count == 0)
            {
                Console.WriteLine("[ERROR] No empty positions found in world!");
                // Возвращаем центр поля как последнюю попытку
                return new Vector3(Size / 2f, heightOffset, Size / 2f);
            }

            // Выбираем случайную позицию из списка
            var randomPos = emptyPositions[_rng.Next(emptyPositions.Count)];
            var result = new Vector3(randomPos.X + 0.5f, heightOffset, randomPos.Y + 0.5f);
            
            Console.WriteLine($"[DEBUG] Найдена свободная позиция: ({randomPos.X}, {randomPos.Y}) из {emptyPositions.Count} доступных");
            return result;
        }

        public Vector3 GetRandomEmptyPositionFarFrom(Vector3 otherPosition, float minDistance, float heightOffset = 0f)
        {
            // Сначала собираем все свободные позиции
            var emptyPositions = new List<Vector2>();
            
            for (int x = 1; x < Size - 1; x++)
            {
                for (int z = 1; z < Size - 1; z++)
                {
                    if (Grid[x, z] == TileType.Empty)
                    {
                        var pos = new Vector3(x + 0.5f, heightOffset, z + 0.5f);
                        if (Vector3.Distance(pos, otherPosition) >= minDistance)
                        {
                            emptyPositions.Add(new Vector2(x, z));
                        }
                    }
                }
            }

            if (emptyPositions.Count == 0)
            {
                Console.WriteLine($"[WARNING] No empty positions found far enough ({minDistance}) from other position, using any empty position");
                return GetRandomEmptyPosition(heightOffset);
            }

            // Выбираем случайную позицию из списка
            var randomPos = emptyPositions[_rng.Next(emptyPositions.Count)];
            var result = new Vector3(randomPos.X + 0.5f, heightOffset, randomPos.Y + 0.5f);
            
            Console.WriteLine($"[DEBUG] Найдена свободная позиция далеко от другой: ({randomPos.X}, {randomPos.Y}) из {emptyPositions.Count} доступных");
            return result;
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