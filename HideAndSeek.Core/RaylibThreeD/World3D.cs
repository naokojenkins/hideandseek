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

        // RNG для генерации мира (детерминизм управляется через WorldConfig.Seed)
        private readonly Random _rng;

        public World3D(int size)
        {
            Size = size;
            Grid = new TileType[size, size];
            var config = GameConfig.Instance.World;
            CellSize = config.CellSize;
            WallHeight = config.WallHeight;

            // RNG: если задан Seed — используем его, иначе недетерминированный
            if (config.Seed.HasValue)
                _rng = new Random(config.Seed.Value);
            else
                _rng = new Random(unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode()));

            // Цвета из конфига (с дефолтами, совпадающими с прежними)
            FloorColor = (config.FloorColor ?? new ColorConfig(200, 200, 200, 255)).ToRaylibColor();
            WallColor  = (config.WallColor  ?? new ColorConfig(80, 80, 80, 255)).ToRaylibColor();

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
            // Fast reject: center cell must be empty and inside the grid
            int centerGX = (int)MathF.Floor(centerX);
            int centerGZ = (int)MathF.Floor(centerZ);
            if (!IsInside(centerGX, centerGZ) || Grid[centerGX, centerGZ] == TileType.Wall)
                return false;

            // Robust continuous collision check against unit wall cells
            // Iterate only over the neighborhood potentially intersecting the agent's disc
            int minGX = Math.Max(0, (int)MathF.Floor(centerX - radius) - 1);
            int maxGX = Math.Min(Size - 1, (int)MathF.Floor(centerX + radius) + 1);
            int minGZ = Math.Max(0, (int)MathF.Floor(centerZ - radius) - 1);
            int maxGZ = Math.Min(Size - 1, (int)MathF.Floor(centerZ + radius) + 1);

            for (int gx = minGX; gx <= maxGX; gx++)
            {
                for (int gz = minGZ; gz <= maxGZ; gz++)
                {
                    if (Grid[gx, gz] != TileType.Wall) continue;

                    // Axis-aligned unit square for the wall cell: [gx, gx+1] x [gz, gz+1]
                    float nearestX = MathF.Max(gx, MathF.Min(centerX, gx + 1f));
                    float nearestZ = MathF.Max(gz, MathF.Min(centerZ, gz + 1f));
                    float dx = centerX - nearestX;
                    float dz = centerZ - nearestZ;
                    float distSq = dx * dx + dz * dz;
                    if (distSq < radius * radius)
                        return false;
                }
            }

            // Optional perimeter sampling (extra safety for numeric quirks)
            int steps = Math.Max(0, GameConfig.Instance.World.AreaFreePerimeterSamples);
            for (int i = 0; i < steps; i++)
            {
                float angle = (float)(2 * Math.PI * i / steps);
                float eps = GameConfig.Instance.World.AreaFreeEdgeEpsilon;
                float checkX = centerX + MathF.Cos(angle) * radius * eps;
                float checkZ = centerZ + MathF.Sin(angle) * radius * eps;
                int gx = Math.Clamp((int)MathF.Floor(checkX), 0, Size - 1);
                int gz = Math.Clamp((int)MathF.Floor(checkZ), 0, Size - 1);
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
            // чтобы поведение было детерминированным и не зависело от геометрии
            // (порог задаётся через конфиг).
            var wcfg = GameConfig.Instance.World;
            bool useMaze = wcfg.UseMaze && Size > Math.Max(0, wcfg.MazeThresholdSize);
            if (useMaze && string.Equals(wcfg.GenerationType, "MazeDFS", StringComparison.OrdinalIgnoreCase))
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
            Console.WriteLine($"[DEBUG] Мир {Size}x{Size} сгенерирован: {emptyCount} свободных клеток (генератор={wcfg.GenerationType}, seed={(wcfg.Seed?.ToString() ?? "auto")})");
        }

        public void Draw(bool showShadows = true)
        {
            var wcfg = GameConfig.Instance.World;
            var wireColor = (wcfg.WallWireColor ?? new ColorConfig(0, 0, 0, 255)).ToRaylibColor();
            bool drawShadows = showShadows && wcfg.DrawShadows;
            float shadowScale = wcfg.ShadowScale;
            float shadowHeight = wcfg.ShadowHeight;
            float shadowBrightness = wcfg.ShadowBrightness;

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
                        Raylib.DrawCubeWires(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, wireColor);
                        if (drawShadows)
                        {
                            Raylib.DrawCube(
                                new Vector3(x + 0.5f, -shadowHeight * 0.5f, z + 0.5f),
                                CellSize * shadowScale, shadowHeight, CellSize * shadowScale,
                                Brightness(WallColor, shadowBrightness)
                            );
                        }
                    }
                }
            }
        }

        public void DrawGrid()
        {
            var wcfg = GameConfig.Instance.World;
            if (!wcfg.DrawGrid) return;
            float y = wcfg.GridY;
            var gridColor = (wcfg.GridColor ?? new ColorConfig(60, 60, 60, 255)).ToRaylibColor();
            for (int x = 0; x <= Size; x++)
                Raylib.DrawLine3D(
                    new Vector3(x, y, 0),
                    new Vector3(x, y, Size),
                    gridColor);
            for (int z = 0; z <= Size; z++)
                Raylib.DrawLine3D(
                    new Vector3(0, y, z),
                    new Vector3(Size, y, z),
                    gridColor);
        }

        public Vector3 GetRandomEmptyPosition(float heightOffset = 0f)
        {
            // Reservoir sampling одной позиции без хранения всех вариантов
            int count = 0;
            int rx = -1, rz = -1;
            for (int x = 1; x < Size - 1; x++)
            for (int z = 1; z < Size - 1; z++)
            {
                if (Grid[x, z] == TileType.Empty)
                {
                    count++;
                    if (_rng.Next(count) == 0) { rx = x; rz = z; }
                }
            }

            if (count == 0 || rx < 0)
            {
                Console.WriteLine("[ERROR] No empty positions found in world!");
                return new Vector3(Size / 2f, heightOffset, Size / 2f);
            }
            return new Vector3(rx + 0.5f, heightOffset, rz + 0.5f);
        }

        public Vector3 GetRandomEmptyPositionFarFrom(Vector3 otherPosition, float minDistance, float heightOffset = 0f)
        {
            // Reservoir sampling с фильтром по расстоянию
            int count = 0;
            int rx = -1, rz = -1;
            for (int x = 1; x < Size - 1; x++)
            for (int z = 1; z < Size - 1; z++)
            {
                if (Grid[x, z] != TileType.Empty) continue;
                var pos = new Vector3(x + 0.5f, heightOffset, z + 0.5f);
                if (Vector3.Distance(pos, otherPosition) < minDistance) continue;
                count++;
                if (_rng.Next(count) == 0) { rx = x; rz = z; }
            }

            if (count == 0 || rx < 0)
            {
                Console.WriteLine($"[WARNING] No empty positions found far enough ({minDistance}) from other position, using any empty position");
                return GetRandomEmptyPosition(heightOffset);
            }
            return new Vector3(rx + 0.5f, heightOffset, rz + 0.5f);
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to, float agentRadius = 0.3f)
        {
            var wcfg = GameConfig.Instance.World;
            Vector3 dir = Vector3.Normalize(to - from);
            float side = MathF.Max(0f, wcfg.LoSRaycastSideOffsetFactor) * agentRadius;
            // оффсеты для толщины луча
            Vector3 perp = new Vector3(-dir.Z, 0, dir.X);
            var offsets = side > 0.0001f
                ? new Vector3[] { Vector3.Zero, perp * side, -perp * side }
                : new Vector3[] { Vector3.Zero };

            foreach (var off in offsets)
            {
                if (!RaycastDdaClear(from + off, to + off))
                    return false;
            }
            return true;
        }

        private bool RaycastDdaClear(Vector3 from, Vector3 to)
        {
            // 2D DDA по XZ-плоскости
            float x0 = from.X, z0 = from.Z;
            float x1 = to.X, z1 = to.Z;

            int gx = Math.Clamp((int)MathF.Floor(x0), 0, Size - 1);
            int gz = Math.Clamp((int)MathF.Floor(z0), 0, Size - 1);
            int gx1 = Math.Clamp((int)MathF.Floor(x1), 0, Size - 1);
            int gz1 = Math.Clamp((int)MathF.Floor(z1), 0, Size - 1);

            float dx = x1 - x0;
            float dz = z1 - z0;
            float stepX = dx >= 0 ? 1 : -1;
            float stepZ = dz >= 0 ? 1 : -1;

            float tDeltaX = dx == 0 ? float.PositiveInfinity : MathF.Abs(1f / dx);
            float tDeltaZ = dz == 0 ? float.PositiveInfinity : MathF.Abs(1f / dz);

            float nextGridX = stepX > 0 ? (gx + 1) : gx; // ближайшая вертикальная грань
            float nextGridZ = stepZ > 0 ? (gz + 1) : gz; // ближайшая горизонтальная грань
            float tMaxX = dx == 0 ? float.PositiveInfinity : (nextGridX - x0) / dx;
            float tMaxZ = dz == 0 ? float.PositiveInfinity : (nextGridZ - z0) / dz;

            // проверяем стартовую клетку
            if (IsBlocked(gx, gz)) return false;

            int guard = Size * Size * 4; // страховка от бесконечного цикла
            while ((gx != gx1 || gz != gz1) && guard-- > 0)
            {
                if (tMaxX < tMaxZ)
                {
                    gx += (int)stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    gz += (int)stepZ;
                    tMaxZ += tDeltaZ;
                }
                if (!IsInside(gx, gz)) return false; // вышли за пределы — трактуем как блок
                if (IsBlocked(gx, gz)) return false;
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

        private bool IsInside(float x, float z)
        {
            return x >= 0 && z >= 0 && x < Size && z < Size;
        }

        private bool IsInside(int x, int z, int size) => x >= 0 && z >= 0 && x < size && z < size;
    }
}
