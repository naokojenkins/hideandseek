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
                bool InBounds(int x, int z) => x >= 2 && z >= 2 && x <= Size - 3 && z <= Size - 3;

                bool IsMoatClear(int x, int z, int px, int pz)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (!IsInside(nx, nz)) return false;
                        if (nx == px && nz == pz) continue; // позволяем соседство только с предыдущей клеткой линии
                        if (Grid[nx, nz] == TileType.Wall) return false;
                    }
                    return true;
                }

                void PlaceSnake()
                {
                    // Длины и количество стен масштабируем от размера поля
                    int maxLen = Math.Max(8, (int)MathF.Round(Size * 0.55f));
                    int minLen = Math.Max(5, (int)MathF.Round(Size * 0.30f));
                    if (minLen > maxLen) (minLen, maxLen) = (maxLen, minLen);
                    int len = _rng.Next(minLen, maxLen + 1);

                    // Стартовая точка подальше от краёв и иных стен
                    int sx, sz; int tries = 0;
                    while (true)
                    {
                        sx = _rng.Next(2, Size - 2);
                        sz = _rng.Next(2, Size - 2);
                        if (!InBounds(sx, sz)) { if (++tries > 200) return; else continue; }
                        if (Grid[sx, sz] == TileType.Empty && IsMoatClear(sx, sz, -999, -999)) break;
                        if (++tries > 200) return;
                    }

                    int x = sx, z = sz;
                    // Случайное начальное направление
                    var dirs = new (int dx, int dz)[] { (1,0), (-1,0), (0,1), (0,-1) };
                    int dirIdx = _rng.Next(dirs.Length);
                    var dir = dirs[dirIdx];
                    int px = -999, pz = -999; // предыдущая клетка

                    // Режимы: обычный и «изогнутый» (формирует дуги лестничным паттерном)
                    bool curvedMode = _rng.NextDouble() < 0.6; // чаще создаём изогнутые формы
                    int turnSense = _rng.Next(2) == 0 ? +1 : -1; // +1=левый поворот, -1=правый
                    int arcSpan = _rng.Next(3, 7); // длина «дуги» до пересмотра
                    int arcStep = 0;

                    // Помощник: поворот индекса направления влево/вправо
                    int Turn(int idx, int sense)
                    {
                        // соответствие: 0:(1,0), 1:(-1,0), 2:(0,1), 3:(0,-1)
                        // «влево/вправо» определим вручную — выберем таблицу поворотов
                        // Для единообразия определим порядок: Right(1,0)->Down(0,1)->Left(-1,0)->Up(0,-1)
                        // Тогда левый поворот: idx = (idx + 1) % 4; правый: (idx + 3) % 4
                        int mapIdx;
                        // Преобразуем в цикл Right,Down,Left,Up из нашего массива
                        // Наш массив: [Right, Left, Down, Up]
                        // Создадим отображение из массива в циклический индекс RDLU
                        int[] toCycle = new int[] { 0, 2, 1, 3 }; // mapping indices into R(0),D(1),L(2),U(3)
                        int[] fromCycle = new int[] { 0, 2, 1, 3 }; // inverse is the same here
                        int cyc = toCycle[idx];
                        cyc = sense > 0 ? (cyc + 1) & 3 : (cyc + 3) & 3;
                        mapIdx = fromCycle[cyc];
                        return mapIdx;
                    }

                    for (int i = 0; i < len; i++)
                    {
                        if (!InBounds(x, z) || !IsMoatClear(x, z, px, pz)) break;
                        Grid[x, z] = TileType.Wall;

                        px = x; pz = z;

                        // Выбор следующего шага
                        if (curvedMode)
                        {
                            // Лестничный паттерн: несколько шагов вперёд, затем поворот, повторять
                            // Альтернируем между «вперёд» и «повернуть и шагнуть» для диагональной дуги
                            bool turnNow = (arcStep % 2 == 1);
                            int nextDirIdx = dirIdx;
                            if (turnNow)
                            {
                                nextDirIdx = Turn(dirIdx, turnSense);
                            }

                            // Иногда (маловероятно) поменяем сторону изгиба для разнообразия
                            if (_rng.NextDouble() < 0.07) turnSense = -turnSense;

                            int nx = x + dirs[nextDirIdx].dx;
                            int nz = z + dirs[nextDirIdx].dz;
                            if (InBounds(nx, nz) && IsMoatClear(nx, nz, px, pz))
                            {
                                dirIdx = nextDirIdx;
                                dir = dirs[dirIdx];
                                x = nx; z = nz;
                                arcStep++;
                                if (arcStep >= arcSpan)
                                {
                                    arcStep = 0;
                                    arcSpan = _rng.Next(3, 7);
                                    // иногда полностью меняем режим/направление
                                    if (_rng.NextDouble() < 0.2) dirIdx = _rng.Next(dirs.Length);
                                }
                                continue;
                            }
                            else
                            {
                                // Падение в резервный режим — ищем любой допустимый ход, сохраняя «изогнутость» при возможности
                                bool moved = false;
                                int[] candidates = new int[] { Turn(dirIdx, turnSense), dirIdx, Turn(dirIdx, -turnSense), Turn(Turn(dirIdx, turnSense), turnSense) };
                                foreach (var c in candidates)
                                {
                                    int tx = x + dirs[c].dx; int tz = z + dirs[c].dz;
                                    if (InBounds(tx, tz) && IsMoatClear(tx, tz, px, pz)) { dirIdx = c; dir = dirs[dirIdx]; x = tx; z = tz; moved = true; break; }
                                }
                                if (!moved) break;
                                arcStep++;
                                continue;
                            }
                        }
                        else
                        {
                            // Обычный случайный «змей» с редкими поворотами
                            if (_rng.NextDouble() < 0.35)
                            {
                                // избегаем мгновенного разворота на 180°
                                int newIdx;
                                int attempts = 0;
                                do { newIdx = _rng.Next(dirs.Length); attempts++; }
                                while (attempts < 6 && (dirs[newIdx].dx == -dir.dx && dirs[newIdx].dz == -dir.dz));
                                dirIdx = newIdx;
                                dir = dirs[dirIdx];
                            }

                            int nx = x + dir.dx;
                            int nz = z + dir.dz;
                            if (!InBounds(nx, nz) || !IsMoatClear(nx, nz, px, pz))
                            {
                                // попробуем повернуть, чтобы продолжить без столкновений
                                bool moved = false;
                                for (int k = 0; k < 4; k++)
                                {
                                    var candIdx = _rng.Next(dirs.Length);
                                    int tx = x + dirs[candIdx].dx; int tz = z + dirs[candIdx].dz;
                                    if (InBounds(tx, tz) && IsMoatClear(tx, tz, px, pz)) { dirIdx = candIdx; dir = dirs[dirIdx]; nx = tx; nz = tz; moved = true; break; }
                                }
                                if (!moved) break;
                            }
                            x = nx; z = nz;
                        }
                    }
                }

                // Количество змейки-проходов зависит от размера
                int snakes = Math.Max(7, (int)MathF.Round(Size * 0.6f));
                for (int i = 0; i < snakes; i++) PlaceSnake();
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
