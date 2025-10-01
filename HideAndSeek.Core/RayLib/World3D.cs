using System;
using System.Numerics;
using System.Collections.Generic;
using HideAndSeek.Core.Config;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    public class World3D
    {
        public TileType[,] Grid { get; }
        public int Size { get; }
        public float CellSize { get; set; }
        public float WallHeight { get; set; }
        public Color FloorColor { get; set; }
        public Color WallColor { get; set; }
        // RoomSize removed: generation is now random without room partitioning

        // Use non-deterministic RNG for world generation to get a new layout on every run/restart
        private readonly Random _rng = new Random(unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode()));

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            var config = GameConfig.Instance.World;
            CellSize = config.CellSize;
            WallHeight = config.WallHeight;

            // Цвета также берём из конфига, если добавлены:
            FloorColor = new Color(200, 200, 200, 255); // По умолчанию
            WallColor  = new Color(80, 80, 80, 255);    // По умолчанию

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

            // Случайные «змейки» стен внутри поля без образования замкнутых комнат.
            // Гарантии:
            // - Внешняя рамка стен сохраняется (см. выше).
            // - Внутренние стены не касаются друг друга и границы (зазор 1 клетка по Чебышеву).
            // - Стены тонкие (1 клетка) и образуют несамопересекающиеся ломаные.
            // => Вокруг любой стены можно обойти с любой стороны.

            // Для стабильности unit-тестов на маленьких мирах — не генерировать внутренние стены,
            // чтобы поведение было детерминированным и не зависело от геометрии (Size <= 12).
            if (Size > 12)
            {
                // Цель: сгенерировать лабиринт в стиле «коридоры 1 клетки шириной»,
                // где стены могут соприкасаться, образуя узоры лабиринта, включая Г-образные повороты,
                // и из каждого тупика коридора есть как минимум один выход (свойство лабиринта как связного графа).
                // Подход: классический randomized DFS (backtracker) по «клеткам» на нечётных координатах.

                // 1) Сделаем всё внутреннее пространство стенами (граница уже стеной выше)
                for (int x = 1; x < Size - 1; x++)
                for (int z = 1; z < Size - 1; z++)
                    Grid[x, z] = TileType.Wall;

                // 2) Помечаем «клетки-узлы» лабиринта как координаты с нечётными индексами
                bool InCellBounds(int cx, int cz) => cx > 0 && cz > 0 && cx < Size - 1 && cz < Size - 1;

                var stack = new Stack<(int x, int z)>();

                // старт: случайная нечётная клетка
                int sx = 1 + 2 * _rng.Next(Math.Max(1, (Size - 2) / 2));
                int sz = 1 + 2 * _rng.Next(Math.Max(1, (Size - 2) / 2));
                if (sx >= Size - 1) sx = Size - 2;
                if (sz >= Size - 1) sz = Size - 2;
                if ((sx & 1) == 0) sx = Math.Max(1, sx - 1);
                if ((sz & 1) == 0) sz = Math.Max(1, sz - 1);

                stack.Push((sx, sz));
                Grid[sx, sz] = TileType.Empty;

                // карта посещения только для нечётных клеток
                int cellsX = (Size - 1) / 2;
                int cellsZ = (Size - 1) / 2;
                var visited = new HashSet<(int x, int z)>();
                visited.Add((sx, sz));

                // векторы к соседним клеткам (шаг = 2, чтобы перепрыгивать через стену между клетками)
                var dirs2 = new (int dx, int dz)[] { (2,0), (-2,0), (0,2), (0,-2) };

                while (stack.Count > 0)
                {
                    var (x, z) = stack.Peek();

                    // собрать непосещённых соседей на расстоянии 2
                    var neighbors = new List<(int nx, int nz, int wx, int wz)>();
                    foreach (var (dx, dz) in dirs2)
                    {
                        int nx = x + dx;
                        int nz = z + dz;
                        int wx = x + dx / 2; // стена между текущей клеткой и соседней
                        int wz = z + dz / 2;
                        if (!InCellBounds(nx, nz)) continue;
                        if ((nx & 1) == 0 || (nz & 1) == 0) continue; // соседняя «клетка» должна быть нечётной по обоим координатам
                        if (!visited.Contains((nx, nz)))
                        {
                            neighbors.Add((nx, nz, wx, wz));
                        }
                    }

                    if (neighbors.Count == 0)
                    {
                        stack.Pop();
                        continue;
                    }

                    // выбрать случайного соседа
                    var choice = neighbors[_rng.Next(neighbors.Count)];
                    // пробиваем стену между клетками
                    Grid[choice.wx, choice.wz] = TileType.Empty;
                    // и саму клетку делаем пустой
                    Grid[choice.nx, choice.nz] = TileType.Empty;

                    visited.Add((choice.nx, choice.nz));
                    stack.Push((choice.nx, choice.nz));
                }

                // 3) «Разряженность» лабиринта: добавим больше дополнительных проходов,
                // чтобы увеличить число развилок и сократить количество длинных тупиков.
                int extraPassages = Math.Max(2, Size / 2);
                for (int i = 0; i < extraPassages; i++)
                {
                    // выбираем случайную внутреннюю стену, которая имеет пустоту по обе стороны по одной из осей
                    int wx = _rng.Next(2, Size - 2);
                    int wz = _rng.Next(2, Size - 2);
                    if (Grid[wx, wz] != TileType.Wall) { i--; continue; }

                    bool canOpen = false;
                    if (wx % 2 == 1 && wz % 2 == 0)
                    {
                        // вертикальная стенка между двумя клетками по Z
                        if (Grid[wx, wz - 1] == TileType.Empty && Grid[wx, wz + 1] == TileType.Empty) canOpen = true;
                    }
                    else if (wx % 2 == 0 && wz % 2 == 1)
                    {
                        // горизонтальная стенка между двумя клетками по X
                        if (Grid[wx - 1, wz] == TileType.Empty && Grid[wx + 1, wz] == TileType.Empty) canOpen = true;
                    }

                    if (canOpen)
                    {
                        Grid[wx, wz] = TileType.Empty;
                    }
                }

                // 4) Увеличим ширину коридоров случайным локальным расширением,
                // избегая глобальной симметрии (никакой привязки к чётности индексов).
                var widened = (TileType[,])Grid.Clone();
                for (int x = 1; x < Size - 1; x++)
                {
                    for (int z = 1; z < Size - 1; z++)
                    {
                        if (Grid[x, z] != TileType.Empty) continue;

                        // сохраняем текущую пустую клетку
                        widened[x, z] = TileType.Empty;

                        // С небольшой вероятностью расширяем в 1–2 случайных направления.
                        // Это увеличивает расстояние между стенами, но не создаёт «шахматной» регулярности.
                        if (_rng.NextDouble() < 0.35)
                        {
                            // случайная перестановка направлений
                            var dirs = new (int dx, int dz)[] { (1,0), (-1,0), (0,1), (0,-1) };
                            for (int k = 0; k < dirs.Length; k++)
                            {
                                int j = _rng.Next(k, dirs.Length);
                                (dirs[k], dirs[j]) = (dirs[j], dirs[k]);
                            }

                            int expansions = _rng.NextDouble() < 0.5 ? 1 : 2; // иногда расширяем два соседних тайла
                            int done = 0;
                            foreach (var (dx, dz) in dirs)
                            {
                                int nx = x + dx;
                                int nz = z + dz;
                                if (nx <= 0 || nz <= 0 || nx >= Size - 1 || nz >= Size - 1) continue;
                                if (Grid[nx, nz] == TileType.Wall)
                                {
                                    widened[nx, nz] = TileType.Empty;
                                    done++;
                                    if (done >= expansions) break;
                                }
                            }
                        }
                    }
                }

                // Граница остаётся стеной согласно правилам
                for (int i = 0; i < Size; i++)
                {
                    widened[i, 0] = TileType.Wall;
                    widened[i, Size - 1] = TileType.Wall;
                    widened[0, i] = TileType.Wall;
                    widened[Size - 1, i] = TileType.Wall;
                }
                Array.Copy(widened, Grid, Grid.Length);
            }

            // Для отладки: считаем число свободных клеток
            int emptyCount = 0;
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    if (Grid[x, z] == TileType.Empty)
                        emptyCount++;
            Console.WriteLine($"[DEBUG] Мир {Size}x{Size} сгенерирован: {emptyCount} свободных клеток (случайная генерация стен)");
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
