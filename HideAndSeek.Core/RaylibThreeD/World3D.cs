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
                // Начальную клетку сразу расширим до ширины 2 консистентной стороной,
                // чтобы весь путь имел толщину 2. Раньше мы брали сторону по чётности индексов,
                // что порождало «шахматные» узоры. Теперь фиксируем глобальный выбор стороны
                // для горизонтальных и вертикальных шагов один раз на генерацию.
                int sideZ = _rng.Next(2) == 0 ? -1 : 1; // для горизонтальных ходов расширяем вверх/вниз фиксировано
                int sideX = _rng.Next(2) == 0 ? -1 : 1; // для вертикальных ходов расширяем влево/вправо фиксировано
                // Ранее расширяли стартовую ячейку до ширины 2 сразу. Убираем это,
                // чтобы сохранить больше стен на старте; ширину обеспечим пост‑этапом.

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

                    // Расширяем проход до толщины 2 клеток консистентной стороной
                    // в зависимости от ориентации шага (горизонт/вертикаль).
                    int dx2 = choice.nx - x;
                    int dz2 = choice.nz - z;
                    if (Math.Abs(dx2) == 2 && dz2 == 0)
                    {
                        // Ранее здесь расширяли проход до ширины 2 сразу (вдоль соседней полосы).
                        // Отключаем это, чтобы на стадии DFS не разрежать стены — расширим позже
                        // безопасным пост‑этапом WidenLinearCorridorsToWidth.
                    }
                    else if (Math.Abs(dz2) == 2 && dx2 == 0)
                    {
                        // Аналогично для вертикального шага— расширение отключено на стадии DFS.
                    }

                    visited.Add((choice.nx, choice.nz));
                    stack.Push((choice.nx, choice.nz));
                }

                // 3) «Разряженность» лабиринта: добавим дополнительные проёмы между коридорами.
                // ВАЖНО: величина ExtraPassagesScale теперь управляет долей УСПЕШНО открытых проёмов
                // от текущего числа валидных кандидатов, а не просто числом попыток.
                // Это делает параметр предсказуемым и заметным при изменении 0.25 -> 0.50 и т.д.
                int totalInner = Math.Max(0, (Size - 2) * (Size - 2));
                int innerWalls = 0;
                for (int ix = 1; ix < Size - 1; ix++)
                    for (int iz = 1; iz < Size - 1; iz++)
                        if (Grid[ix, iz] == TileType.Wall) innerWalls++;

                float targetMin = Math.Clamp(GameConfig.Instance.World.WallDensityTargetMin, 0f, 0.95f);

                // ВАЖНО: не засоряем фильтр расстояния всеми пустотами коридоров.
                // Для контроля плотности «проёмов» учитываем только проёмы, СОЗДАННЫЕ текущими этапами,
                // а не естественные пустоты. Поэтому исходный набор оставляем пустым, а новые складываем сюда:
                var openingsNew = new HashSet<(int x, int z, bool lr)>();

                bool HasNearbyOpeningForCandidate(int wx, int wz, bool lrAxis, int spacing)
                {
                    if (spacing <= 0) return false;
                    // Проверяем близость только к проёмам той же ориентации (lrAxis)
                    // и делаем осевой (1D по ортогонали) просмотр в пределах spacing.
                    if (lrAxis)
                    {
                        // Кандидат LR (между левым/правым). Смотрим вертикальные отклонения вокруг той же колонки wx.
                        for (int dz = -spacing; dz <= spacing; dz++)
                        {
                            int z = wz + dz;
                            if (z <= 1 || z >= Size - 1) continue;
                            if (openingsNew.Contains((wx, z, true))) return true;
                        }
                    }
                    else
                    {
                        // Кандидат UD (между верхом/низом). Сканируем горизонтально вокруг той же строки wz.
                        for (int dx = -spacing; dx <= spacing; dx++)
                        {
                            int x = wx + dx;
                            if (x <= 1 || x >= Size - 1) continue;
                            if (openingsNew.Contains((x, wz, false))) return true;
                        }
                    }
                    return false;
                }

                // Локальная функция: собрать кандидатов на открытие (стенка между двумя пустотами)
                // Возвращает:
                //  - list: отфильтрованный список кандидатов
                //  - preFilterCount: сколько клеток-стен удовлетворяют условию «между пустотами» до учёта спейсинга
                //  - afterSpacingCount: сколько осталось после фильтра по минимальной дистанции (spacing)
                (List<(int x, int z, bool lr)> list, int preFilterCount, int afterSpacingCount, int effectiveSpacingOut) CollectOpeningCandidates()
                {
                    var list = new List<(int x, int z, bool lr)>();
                    int pre = 0;
                    int after = 0;

                    // Адаптивное ослабление спейсинга при больших значениях ExtraPassagesScale:
                    // если scale > 1, понижаем эффективный спейсинг на floor(scale-1), но не ниже 0.
                    int effectiveSpacing = wcfg.JunctionMinSpacing;
                    if (wcfg.EnableSpacingAwareCarving && wcfg.JunctionMinSpacing > 0)
                    {
                        float scale = MathF.Max(0f, wcfg.ExtraPassagesScale);
                        int relax = (int)MathF.Floor(MathF.Max(0f, scale - 1f));
                        effectiveSpacing = Math.Max(0, wcfg.JunctionMinSpacing - relax);
                    }
                    // Перебираем все внутренние клетки-стены (исключая только периметр 0 и Size-1),
                    // чтобы кандидаты могли появляться и «посередине» длинных стен, и у внутреннего кольца.
                    for (int wx = 1; wx < Size - 1; wx++)
                    {
                        for (int wz = 1; wz < Size - 1; wz++)
                        {
                            if (Grid[wx, wz] != TileType.Wall) continue;
                            // Не ослабляем внутреннее «кольцо» у периметра: пропускаем кандидатов вплотную к рамке
                            if (wx <= 2 || wz <= 2 || wx >= Size - 3 || wz >= Size - 3) continue;
                            bool lr = Grid[wx - 1, wz] == TileType.Empty && Grid[wx + 1, wz] == TileType.Empty;
                            bool ud = Grid[wx, wz - 1] == TileType.Empty && Grid[wx, wz + 1] == TileType.Empty;
                            if (!(lr || ud)) continue;
                            pre++;

                            if (wcfg.EnableSpacingAwareCarving && effectiveSpacing > 0)
                            {
                                if (HasNearbyOpeningForCandidate(wx, wz, lr, effectiveSpacing)) continue;
                            }

                            list.Add((wx, wz, lr));
                            after++;
                        }
                    }
                    return (list, pre, after, effectiveSpacing);
                }

                var initialCandidates = CollectOpeningCandidates();
                var allCandidates = initialCandidates.list;
                int targetOpens = 0;
                if (allCandidates.Count > 0)
                {
                    // Цель — открыть примерно scale * кол-во доступных кандидатов
                    targetOpens = (int)MathF.Round(allCandidates.Count * MathF.Max(0f, wcfg.ExtraPassagesScale));
                }

                int opened = 0;
                while (opened < targetOpens)
                {
                    var collected = CollectOpeningCandidates();
                    var candidates = collected.list;
                    if (candidates.Count == 0)
                    {
                        if (wcfg.DebugWorldGenLogs)
                            Console.WriteLine("[WORLDGEN] extraPassages: нет кандидатов для дополнительных проёмов");
                        break;
                    }
                    if (wcfg.DebugWorldGenLogs)
                    {
                        Console.WriteLine($"[WORLDGEN] extraPassages: preFilter={collected.preFilterCount}, afterSpacing={collected.afterSpacingCount}, candidates={candidates.Count}, targetOpens={targetOpens - opened}, effectiveSpacing={collected.effectiveSpacingOut}");
                    }

                    // Перемешаем и приоритизируем разрыв тупиков
                    Shuffle(candidates);
                    float bias = Math.Clamp(wcfg.JunctionBiasDeadEnds, 0f, 1f);
                    candidates.Sort((a, b) =>
                    {
                        int scoreA = DeadEndReliefScore(a.x, a.z);
                        int scoreB = DeadEndReliefScore(b.x, b.z);
                        float sa = scoreA * (1f + bias);
                        float sb = scoreB * (1f + bias);
                        return sb.CompareTo(sa);
                    });

                    // Анти-симметричный джиттер — перемешаем хвост
                    if (_rng.NextDouble() < Math.Clamp(wcfg.AntiSymmetryJitter, 0f, 1f))
                    {
                        int cut = _rng.Next(0, candidates.Count);
                        var tail = candidates.GetRange(cut, candidates.Count - cut);
                        Shuffle(tail);
                        for (int t = 0; t < tail.Count; t++) candidates[cut + t] = tail[t];
                    }

                    bool openedThisStep = false;
                    foreach (var (wx, wz, lr) in candidates)
                    {
                        // Проверка целевой плотности — оцениваем В ПЕРЕД, без изменения счётчиков, если не открываем
                        if (wcfg.AdaptiveDensityControl && totalInner > 0)
                        {
                            // Если требуется проём толщиной 2, возможно придётся вырезать 2 стены.
                            int wallsToRemove = 1;
                            if (Math.Clamp(wcfg.ExtraPassageWidth, 1, 2) >= 2)
                            {
                                // Оценим, есть ли рядом перпендикулярная стенка для расширения «шахты».
                                if (lr)
                                {
                                    if (wz - 1 >= 1 && Grid[wx, wz - 1] == TileType.Wall) wallsToRemove = Math.Max(wallsToRemove, 2);
                                    else if (wz + 1 <= Size - 2 && Grid[wx, wz + 1] == TileType.Wall) wallsToRemove = Math.Max(wallsToRemove, 2);
                                }
                                else // ud
                                {
                                    if (wx - 1 >= 1 && Grid[wx - 1, wz] == TileType.Wall) wallsToRemove = Math.Max(wallsToRemove, 2);
                                    else if (wx + 1 <= Size - 2 && Grid[wx + 1, wz] == TileType.Wall) wallsToRemove = Math.Max(wallsToRemove, 2);
                                }
                            }

                            float projectedDensity = (float)(innerWalls - wallsToRemove) / totalInner;
                            bool useHard = wcfg.ExtraPassagesUseHardMin;
                            float gate = useHard ? Math.Clamp(wcfg.WallDensityMin, 0f, 0.95f) : targetMin;
                            if (projectedDensity < gate)
                            {
                                if (wcfg.DebugWorldGenLogs)
                                    Console.WriteLine($"[WORLDGEN] extraPassages: стоп по плотности (proj={projectedDensity:P1} < gate={(useHard?"hardMin":"targetMin")}={gate:P1}), opened={opened}/{targetOpens}, candidates={candidates.Count}");
                                openedThisStep = false; // ничего не открыли
                                break; // выходим из foreach
                            }
                        }

                        // Открываем выбранную стенку
                        Grid[wx, wz] = TileType.Empty;
                        int removed = 1;
                        // Добавим в набор новых проёмов (учитываем ориентацию кандидата)
                        openingsNew.Add((wx, wz, lr));

                        // Если задана ExtraPassageWidth>=2 — расширим проём на 1 по перпендикуляру,
                        // чтобы сделать отверстие визуально заметным и устойчивым к последующим шагам.
                        int desiredWidth = Math.Clamp(wcfg.ExtraPassageWidth, 1, 2);
                        if (desiredWidth >= 2)
                        {
                            if (lr)
                            {
                                // проём между левым/правым — попробуем расширить вверх или вниз
                                // детерминированно: выбираем сторону по чётности X+Z, затем fallback на другую при занятости
                                bool carved = false;
                                int[] dzs = ((wx + wz) & 1) == 0 ? new[] { -1, 1 } : new[] { 1, -1 };
                                foreach (var dz in dzs)
                                {
                                    int z2 = wz + dz;
                                    if (z2 >= 1 && z2 <= Size - 2 && Grid[wx, z2] == TileType.Wall)
                                    {
                                        Grid[wx, z2] = TileType.Empty;
                                        removed++;
                                        carved = true;
                                        openingsNew.Add((wx, z2, lr));
                                        break;
                                    }
                                }
                                // если обе стороны уже пустые — ничего не делаем, «ширина 2» уже достигнута структурой
                            }
                            else // ud
                            {
                                // проём между верхом/низом — расширим влево или вправо
                                bool carved = false;
                                int[] dxs = ((wx + wz) & 1) == 0 ? new[] { -1, 1 } : new[] { 1, -1 };
                                foreach (var dx in dxs)
                                {
                                    int x2 = wx + dx;
                                    if (x2 >= 1 && x2 <= Size - 2 && Grid[x2, wz] == TileType.Wall)
                                    {
                                        Grid[x2, wz] = TileType.Empty;
                                        removed++;
                                        carved = true;
                                        openingsNew.Add((x2, wz, lr));
                                        break;
                                    }
                                }
                            }
                        }

                        innerWalls = Math.Max(0, innerWalls - removed);
                        opened++;
                        openedThisStep = true;
                        break;
                    }

                    // Если плотность не позволила ничего открыть — прекращаем весь этап
                    if (!openedThisStep) break;
                }

                if (wcfg.DebugWorldGenLogs)
                {
                    Console.WriteLine($"[WORLDGEN] extraPassages: preFilter={initialCandidates.preFilterCount}, afterSpacing={initialCandidates.afterSpacingCount}, candidates={allCandidates.Count}, targetOpens={targetOpens}, opened={opened}");
                }

                // 4) Увеличим ширину коридоров случайным локальным расширением,
                // избегая глобальной симметрии (никакой привязки к чётности индексов).
                // РАНЕЕ: случайное локальное «расширение коридоров» приводило к фрагментации стен
                // и появлению одиночных кубиков-стен. Чтобы стены преимущественно имели длину > 1,
                // отключаем этот шаг. Достаточно последующего правила минимальной ширины проходов.
                // (Блок оставлен как комментарий для возможной будущей доработки.)
                // var widened = (TileType[,])Grid.Clone();
                // ... (disabled)
                // Array.Copy(widened, Grid, Grid.Length);
            }

            // Снимок перед постобработкой (для возможного отката)
            var snapshotBeforePost = (TileType[,])Grid.Clone();

            // Глобальное правило: минимальная ширина прохода — minWidth (консервативный carve-only для устранения «мостиков»)
            int minWidth = Math.Max(2, wcfg.MinPassageWidth);
            EnforceMinPassageWidth(minWidth);

            // Дополнительное выравнивание линейных коридоров: убираем остаточные «1‑клеточные» коридоры
            // аккуратным чередующимся карвингом вдоль коридора, чтобы получить ширину >= minWidth
            WidenLinearCorridorsToWidth(minWidth);

            // Удалим только истинно одиночные стены (без 4‑соседей‑стен)
            // Но если стен уже мало, пропускаем удаление одиночек, чтобы не разрежать карту ещё сильнее.
            {
                float densityBeforeSingles = ComputeInternalWallDensity();
                // Доп. гистерезис: удаляем одиночки только если плотность заметно выше «жёсткого» минимума
                // (например, на 4 п.п. и более). Это предотвращает ситуацию «и так пусто, а мы ещё и выкинули крохи стен».
                float hardMinSingles = Math.Clamp(GameConfig.Instance.World.WallDensityMin, 0f, 0.95f);
                if (densityBeforeSingles > hardMinSingles + 0.04f)
                    RemoveIsolatedSingletonWalls();
                else if (GameConfig.Instance.World.DebugWorldGenLogs)
                    Console.WriteLine($"[WORLDGEN] Пропускаю RemoveIsolatedSingletonWalls(): density={densityBeforeSingles:P1} близко к порогу {hardMinSingles:P1}");
            }

            // Дополнительные перемычки между параллельными коридорами для повышения «связанности»
            AddExtraJunctions();

            // Защита по минимальной плотности стен во внутренней области.
            // ВАЖНО: допускаем небольшое снижение ниже жёсткого минимума только для шагов,
            // гарантирующих ширину проходов (EnforceMinPassageWidth/Widen*), которое мы уже
            // учли через их собственный eps-бюджет. Поэтому финальная проверка допускает
            // порог (hardMin - eps).
            float density = ComputeInternalWallDensity();
            float hardMin = Math.Clamp(wcfg.WallDensityMin, 0f, 0.95f);
            float eps = Math.Clamp(wcfg.WidthGuaranteeHardMinEps, 0f, 0.2f);
            float finalGate = MathF.Max(0f, hardMin - eps);
            if (density < finalGate)
            {
                // Откатываем постобработку, чтобы не «обнулять» стены
                Array.Copy(snapshotBeforePost, Grid, Grid.Length);
                if (wcfg.DebugWorldGenLogs)
                    Console.WriteLine($"[WORLDGEN] Откат постобработки: плотность стен {density:P1} ниже допускаемого порога {finalGate:P1} (hardMin={hardMin:P1}, eps={eps:P1})");

                // Даже после отката гарантируем минимальную ширину коридоров безопасным выравниванием,
                // которое почти не влияет на общую плотность стен
                EnforceMinPassageWidth(minWidth);
                WidenLinearCorridorsToWidth(minWidth);
            }

            // Для отладки: считаем число свободных клеток
            int emptyCount = 0;
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                    if (Grid[x, z] == TileType.Empty)
                        emptyCount++;
            if (wcfg.DebugWorldGenLogs)
            {
                Console.WriteLine($"[WORLDGEN] Мир {Size}x{Size}: пустых={emptyCount}, стен={Size*Size - emptyCount}, внутренняя плотность стен={ComputeInternalWallDensity():P1} (генератор={wcfg.GenerationType}, seed={(wcfg.Seed?.ToString() ?? "auto")})");
            }
        }

        // Обеспечивает минимальную ширину прохода между стенами в minWidth клеток.
        // Консервативная реализация для minWidth==2: «высекаем» только узкие перемычки (pinch points)
        // вида Wall, имеющая по обе стороны по одной оси Empty (слева/справа или сверху/снизу),
        // не трогая прочие стены. Это сохраняет структуру лабиринта и избегает тотального размывания.
        private void EnforceMinPassageWidth(int minWidth)
        {
            if (minWidth <= 1 || Size <= 2) return;

            // На текущей стадии поддерживаем правило только для ширины 2.
            // Дополнительные ширины можно реализовать повторением «carve pass» по снапшоту исходной решётки.
            var carved = (TileType[,])Grid.Clone();

            // Адаптивный контроль: ограничим количество вырезаний, чтобы не просесть ниже целевой плотности
            var wcfg = GameConfig.Instance.World;
            int totalInner = Math.Max(0, (Size - 2) * (Size - 2));
            int innerWalls = ComputeInternalWallCount();
            // Для гарантирующих правил (минимальная ширина) используем ЖЁСТКИЙ нижний порог,
            // иначе адаптивный target мог блокировать расширение коридоров до 2 клеток.
            float hardMin = Math.Clamp(wcfg.WallDensityMin, 0f, 0.95f);
            float eps = Math.Clamp(wcfg.WidthGuaranteeHardMinEps, 0f, 0.2f);
            // Бюджет «эпсилон‑вырезаний»: сколько стен мы можем ещё снять, не опускаясь ниже (hardMin - eps)
            int epsilonBudget = 0;
            int minWallsAllowed = 0;
            if (totalInner > 0)
            {
                minWallsAllowed = (int)MathF.Ceiling((hardMin - eps) * totalInner);
                epsilonBudget = Math.Max(0, innerWalls - minWallsAllowed);
            }
            int epsUsed = 0;

            // Важная защита: если и так мало стен, не делаем carve-only вырезания перемычек —
            // это ещё сильнее разрежает карту. Дадим шанс этапу Widen* расширить узкие места
            // за счёт пустот, а не за счёт удаления стен.
            float densityBefore = totalInner > 0 ? (float)innerWalls / totalInner : 1f;
            if (densityBefore <= hardMin + 0.06f)
            {
                if (wcfg.DebugWorldGenLogs)
                    Console.WriteLine($"[WORLDGEN] EnforceMinPassageWidth: пропущен (density={densityBefore:P1} близко к порогу {hardMin:P1})");
                return;
            }

            for (int x = 1; x < Size - 1; x++)
            {
                for (int z = 1; z < Size - 1; z++)
                {
                    if (Grid[x, z] != TileType.Wall) continue;

                    bool leftEmpty  = Grid[x - 1, z] == TileType.Empty;
                    bool rightEmpty = Grid[x + 1, z] == TileType.Empty;
                    bool upEmpty    = Grid[x, z - 1] == TileType.Empty;
                    bool downEmpty  = Grid[x, z + 1] == TileType.Empty;

                    bool openX = leftEmpty && rightEmpty;   // зажат по X
                    bool openZ = upEmpty && downEmpty;      // зажат по Z

                    // Узкая перемычка только по одной оси (исключаем перекрёстки)
                    if (openX ^ openZ)
                    {
                        // Ужесточённый предохранитель: по перпендикулярной оси оба соседа должны быть стенами,
                        // чтобы вырезать только истинные «мостики» один-к-одному и не размывать площади.
                        bool guard = openX
                            ? (!upEmpty && !downEmpty)   // оба up/down — стены
                            : (!leftEmpty && !rightEmpty); // оба left/right — стены

                        // Дополнительный локальный предохранитель: вокруг по 3x3 должно быть достаточно стен
                        bool localOk = !wcfg.AdaptiveDensityControl || CountWalls3x3(x, z) >= 4;

                        if (guard && localOk)
                        {
                            if (wcfg.AdaptiveDensityControl && totalInner > 0)
                            {
                                int projected = innerWalls - 1;
                                if (projected < minWallsAllowed)
                                {
                                    // Нельзя опускаться ниже (hardMin - eps)
                                    x = Size - 2; // завершить внешние циклы
                                    break;
                                }
                                float projectedDensity = (float)projected / totalInner;
                                if (projectedDensity < hardMin)
                                {
                                    // Разрешаем «эпсилон» только при наличии бюджета
                                    if (epsilonBudget <= 0)
                                    {
                                        // Бюджет исчерпан — прекращаем проход аккуратно
                                        x = Size - 2; // завершить внешние циклы
                                        break;
                                    }
                                    epsilonBudget--;
                                    epsUsed++;
                                    innerWalls = projected;
                                }
                                else
                                {
                                    innerWalls = projected;
                                }
                            }
                            carved[x, z] = TileType.Empty;
                        }
                    }
                }
            }

            // Гарантируем, что рамка остаётся стеной
            for (int i = 0; i < Size; i++)
            {
                carved[i, 0] = TileType.Wall;
                carved[i, Size - 1] = TileType.Wall;
                carved[0, i] = TileType.Wall;
                carved[Size - 1, i] = TileType.Wall;
            }

            Array.Copy(carved, Grid, Grid.Length);

            if (wcfg.DebugWorldGenLogs && epsUsed > 0)
            {
                Console.WriteLine($"[WORLDGEN] EnforceMinPassageWidth: использован epsilon-бюджет {epsUsed} из первоначального {Math.Max(epsUsed, epsilonBudget + epsUsed)}");
            }
        }

        // Выравнивает линейные коридоры шириной 1 до требуемой ширины (минимум 2),
        // используя чередующийся карвинг соседних ячеек-стен вдоль направления коридора.
        // Сохраняет периметр. Реализация ориентирована на minWidth==2, но повторяет проход для >2.
        private void WidenLinearCorridorsToWidth(int minWidth)
        {
            int passes = Math.Max(0, minWidth - 1);
            if (passes == 0 || Size <= 2) return;

            for (int p = 0; p < passes; p++)
            {
                var widened = (TileType[,])Grid.Clone();
                var wcfg = GameConfig.Instance.World;
                int totalInner = Math.Max(0, (Size - 2) * (Size - 2));
                int innerWalls = ComputeInternalWallCount();
                // Для гарантии минимальной ширины используем жёсткий нижний порог + допустимый эпсилон
                float hardMin = Math.Clamp(wcfg.WallDensityMin, 0f, 0.95f);
                float eps = Math.Clamp(wcfg.WidthGuaranteeHardMinEps, 0f, 0.2f);
                // Рассчитываем бюджет «эпсилон‑вырезаний» на этот проход
                int epsilonBudget = 0;
                int minWallsAllowed = 0;
                if (totalInner > 0)
                {
                    minWallsAllowed = (int)MathF.Ceiling((hardMin - eps) * totalInner);
                    epsilonBudget = Math.Max(0, innerWalls - minWallsAllowed);
                }
                int epsUsed = 0;
                int abortsSuppressed = 0;
                for (int x = 1; x < Size - 1; x++)
                {
                    for (int z = 1; z < Size - 1; z++)
                    {
                        if (Grid[x, z] != TileType.Empty) continue;

                        bool leftEmpty  = Grid[x - 1, z] == TileType.Empty;
                        bool rightEmpty = Grid[x + 1, z] == TileType.Empty;
                        bool upWall     = Grid[x, z - 1] == TileType.Wall;
                        bool downWall   = Grid[x, z + 1] == TileType.Wall;

                        bool upEmpty    = Grid[x, z - 1] == TileType.Empty;
                        bool downEmpty  = Grid[x, z + 1] == TileType.Empty;
                        bool leftWall   = Grid[x - 1, z] == TileType.Wall;
                        bool rightWall  = Grid[x + 1, z] == TileType.Wall;

                        // Горизонтальный узкий коридор: слева и справа пусто, а сверху и снизу — стены (истинно линейный участок)
                        if (leftEmpty && rightEmpty && upWall && downWall)
                        {
                            // Детеминированный выбор стороны по строке, чтобы расширять весь ряд одной стороной
                            bool preferUp = (z & 1) == 0;
                            int tx = x;
                            int tz = preferUp ? z - 1 : z + 1;
                            if (Grid[tx, tz] == TileType.Wall)
                            {
                                if (wcfg.AdaptiveDensityControl && totalInner > 0)
                                {
                                    int projected = innerWalls - 1;
                                    if (projected < minWallsAllowed)
                                    {
                                        // нельзя опуститься ниже (hardMin - eps)
                                        abortsSuppressed++;
                                    }
                                    else
                                    {
                                        float projectedDensity = (float)projected / totalInner;
                                        if (projectedDensity < hardMin)
                                        {
                                            if (epsilonBudget <= 0)
                                            {
                                                // Бюджет исчерпан — учитываем как подавлённое прерывание без спама
                                                abortsSuppressed++;
                                            }
                                            else
                                            {
                                                epsilonBudget--;
                                                epsUsed++;
                                                innerWalls = projected;
                                                widened[tx, tz] = TileType.Empty;
                                            }
                                        }
                                        else
                                        {
                                            innerWalls = projected;
                                            widened[tx, tz] = TileType.Empty;
                                        }
                                    }
                                }
                                else
                                {
                                    widened[tx, tz] = TileType.Empty;
                                }
                            }
                        }

                        // Вертикальный узкий коридор: сверху и снизу пусто, а слева и справа — стены
                        if (upEmpty && downEmpty && leftWall && rightWall)
                        {
                            // Детеминированный выбор стороны по колонке
                            bool preferLeft = (x & 1) == 0;
                            int tx = preferLeft ? x - 1 : x + 1;
                            int tz = z;
                            if (Grid[tx, tz] == TileType.Wall)
                            {
                                if (wcfg.AdaptiveDensityControl && totalInner > 0)
                                {
                                    int projected = innerWalls - 1;
                                    if (projected < minWallsAllowed)
                                    {
                                        abortsSuppressed++;
                                    }
                                    else
                                    {
                                        float projectedDensity = (float)projected / totalInner;
                                        if (projectedDensity < hardMin)
                                        {
                                            if (epsilonBudget <= 0)
                                            {
                                                abortsSuppressed++;
                                            }
                                            else
                                            {
                                                epsilonBudget--;
                                                epsUsed++;
                                                innerWalls = projected;
                                                widened[tx, tz] = TileType.Empty;
                                            }
                                        }
                                        else
                                        {
                                            innerWalls = projected;
                                            widened[tx, tz] = TileType.Empty;
                                        }
                                    }
                                }
                                else
                                {
                                    widened[tx, tz] = TileType.Empty;
                                }
                            }
                        }
                    }
                }

                // Периметр остаётся стеной
                for (int i = 0; i < Size; i++)
                {
                    widened[i, 0] = TileType.Wall;
                    widened[i, Size - 1] = TileType.Wall;
                    widened[0, i] = TileType.Wall;
                    widened[Size - 1, i] = TileType.Wall;
                }

                Array.Copy(widened, Grid, Grid.Length);

                if (wcfg.DebugWorldGenLogs && (epsUsed > 0 || abortsSuppressed > 0))
                {
                    int initialBudget = epsUsed + epsilonBudget;
                    Console.WriteLine($"[WORLDGEN] Widen pass#{p+1}: использован epsilon-бюджет {epsUsed}/{initialBudget}, подавлено прерываний: {abortsSuppressed}");
                }
            }
        }

        // Добавляет дополнительные проёмы между параллельными коридорами в внутренних стенах.
        // Кандидаты — стены, у которых по одну ось по обе стороны пусто (|E W| или |N S|), без касания периметра.
        private void AddExtraJunctions()
        {
            var wcfg = GameConfig.Instance.World;
            float scale = Math.Clamp(wcfg.ExtraJunctionsScale, 0f, 1f);
            if (scale <= 0f) return;

            var candidates = new List<(int x, int z)>();
            for (int x = 2; x < Size - 2; x++)
            {
                for (int z = 2; z < Size - 2; z++)
                {
                    if (Grid[x, z] != TileType.Wall) continue;
                    bool lr = Grid[x - 1, z] == TileType.Empty && Grid[x + 1, z] == TileType.Empty;
                    bool ud = Grid[x, z - 1] == TileType.Empty && Grid[x, z + 1] == TileType.Empty;
                    if (!(lr || ud)) continue;
                    if (wcfg.EnableSpacingAwareCarving && wcfg.JunctionMinSpacing > 0)
                    {
                        if (HasNearbyOpening(x, z, wcfg.JunctionMinSpacing)) continue;
                    }
                    candidates.Add((x, z));
                }
            }

            if (candidates.Count == 0) return;

            // Перемешаем кандидатов и приоритизируем «разрыв тупиков»
            Shuffle(candidates);
            float biasJ = Math.Clamp(wcfg.JunctionBiasDeadEnds, 0f, 1f);
            candidates.Sort((a, b) =>
            {
                int scoreA = DeadEndReliefScore(a.x, a.z);
                int scoreB = DeadEndReliefScore(b.x, b.z);
                float sa = scoreA * (1f + biasJ);
                float sb = scoreB * (1f + biasJ);
                return sb.CompareTo(sa);
            });
            if (_rng.NextDouble() < Math.Clamp(wcfg.AntiSymmetryJitter, 0f, 1f))
            {
                int cut = _rng.Next(0, candidates.Count);
                var tail = candidates.GetRange(cut, candidates.Count - cut);
                Shuffle(tail);
                for (int t = 0; t < tail.Count; t++) candidates[cut + t] = tail[t];
            }

            int openCount = Math.Clamp((int)MathF.Round(candidates.Count * scale), 0, candidates.Count);
            int totalInner = Math.Max(0, (Size - 2) * (Size - 2));
            int innerWalls = ComputeInternalWallCount();
            float targetMin = Math.Clamp(wcfg.WallDensityTargetMin, 0f, 0.95f);
            int opened = 0;
            for (int i = 0; i < candidates.Count && opened < openCount; i++)
            {
                var (x, z) = candidates[i];
                if (wcfg.AdaptiveDensityControl && totalInner > 0)
                {
                    int projected = innerWalls - 1;
                    float projectedDensity = (float)projected / totalInner;
                    if (projectedDensity < targetMin)
                    {
                        if (wcfg.DebugWorldGenLogs)
                            Console.WriteLine($"[WORLDGEN] ExtraJunctions: остановлен на opened={opened}/{openCount} (projDensity={projectedDensity:P1} ниже targetMin={targetMin:P1})");
                        break;
                    }
                    innerWalls = projected;
                }
                Grid[x, z] = TileType.Empty;
                opened++;
            }
        }

        // Проверка: рядом есть уже открытый «проём» (empty, который соединяет два коридора по одной оси)
        private bool HasNearbyOpening(int cx, int cz, int spacing)
        {
            for (int d = -spacing; d <= spacing; d++)
            {
                int rem = spacing - Math.Abs(d);
                for (int e = -rem; e <= rem; e++)
                {
                    int x = cx + d;
                    int z = cz + e;
                    if (x <= 1 || z <= 1 || x >= Size - 1 || z >= Size - 1) continue;
                    if (Grid[x, z] != TileType.Empty) continue;
                    if (IsOpeningEmpty(x, z)) return true;
                }
            }
            return false;
        }

        // Пустая клетка считается «проёмом» если по одной из осей у неё по обе стороны пустота
        private bool IsOpeningEmpty(int x, int z)
        {
            bool lr = Grid[x - 1, z] == TileType.Empty && Grid[x + 1, z] == TileType.Empty;
            bool ud = Grid[x, z - 1] == TileType.Empty && Grid[x, z + 1] == TileType.Empty;
            return lr || ud;
        }

        // Оценка «разрывает ли кандидат тупик»: считаем степени соседних пустых клеток и стимулируем degree<=1
        private int DeadEndReliefScore(int wx, int wz)
        {
            int score = 0;
            // Если слева/справа пусто — посмотрим их степени
            if (wx > 1 && wx < Size - 1)
            {
                if (Grid[wx - 1, wz] == TileType.Empty) score += (DegreeEmpty(wx - 1, wz) <= 1 ? 2 : 0);
                if (Grid[wx + 1, wz] == TileType.Empty) score += (DegreeEmpty(wx + 1, wz) <= 1 ? 2 : 0);
            }
            // Если сверху/снизу пусто — посмотрим их степени
            if (wz > 1 && wz < Size - 1)
            {
                if (Grid[wx, wz - 1] == TileType.Empty) score += (DegreeEmpty(wx, wz - 1) <= 1 ? 2 : 0);
                if (Grid[wx, wz + 1] == TileType.Empty) score += (DegreeEmpty(wx, wz + 1) <= 1 ? 2 : 0);
            }
            return score;
        }

        // Степень пустой клетки по 4‑соседям
        private int DegreeEmpty(int x, int z)
        {
            int d = 0;
            if (Grid[x + 1, z] == TileType.Empty) d++;
            if (Grid[x - 1, z] == TileType.Empty) d++;
            if (Grid[x, z + 1] == TileType.Empty) d++;
            if (Grid[x, z - 1] == TileType.Empty) d++;
            return d;
        }

        // Перемешивание списка (Fisher–Yates)
        private void Shuffle<T>(IList<T> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int j = _rng.Next(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Подсчёт плотности стен во внутренней области (без периметра)
        private float ComputeInternalWallDensity()
        {
            if (Size <= 2) return 1f; // тривиальная решётка — считаем «всё стены»
            int count = 0;
            int total = (Size - 2) * (Size - 2);
            for (int x = 1; x < Size - 1; x++)
                for (int z = 1; z < Size - 1; z++)
                    if (Grid[x, z] == TileType.Wall) count++;
            return total > 0 ? (float)count / total : 1f;
        }

        // Быстрый подсчёт числа внутренних стен (без периметра)
        private int ComputeInternalWallCount()
        {
            if (Size <= 2) return (Size * Size);
            int count = 0;
            for (int x = 1; x < Size - 1; x++)
                for (int z = 1; z < Size - 1; z++)
                    if (Grid[x, z] == TileType.Wall) count++;
            return count;
        }

        // Подсчёт числа стен в окрестности 3x3 вокруг клетки (включая центр)
        private int CountWalls3x3(int cx, int cz)
        {
            int cnt = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int x = cx + dx;
                    int z = cz + dz;
                    if (x <= 0 || z <= 0 || x >= Size - 1 || z >= Size - 1) continue; // игнорируем периметр
                    if (Grid[x, z] == TileType.Wall) cnt++;
                }
            }
            return cnt;
        }

        // Удаляет единичные внутренние клетки-стены, у которых нет 4‑соседей‑стен.
        // Периметр не трогаем.
        private void RemoveIsolatedSingletonWalls()
        {
            var cleaned = (TileType[,])Grid.Clone();
            for (int x = 1; x < Size - 1; x++)
            {
                for (int z = 1; z < Size - 1; z++)
                {
                    if (Grid[x, z] != TileType.Wall) continue;

                    int neighborWalls = 0;
                    if (Grid[x + 1, z] == TileType.Wall) neighborWalls++;
                    if (Grid[x - 1, z] == TileType.Wall) neighborWalls++;
                    if (Grid[x, z + 1] == TileType.Wall) neighborWalls++;
                    if (Grid[x, z - 1] == TileType.Wall) neighborWalls++;

                    if (neighborWalls == 0)
                        cleaned[x, z] = TileType.Empty;
                }
            }

            // Периметр оставляем стеной
            for (int i = 0; i < Size; i++)
            {
                cleaned[i, 0] = TileType.Wall;
                cleaned[i, Size - 1] = TileType.Wall;
                cleaned[0, i] = TileType.Wall;
                cleaned[Size - 1, i] = TileType.Wall;
            }

            Array.Copy(cleaned, Grid, Grid.Length);
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
