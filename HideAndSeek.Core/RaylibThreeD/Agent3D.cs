using System;
using System.Numerics;
using System.Collections.Generic;
using HideAndSeek.Core.Config;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    public partial class Agent3D
    {
        private bool _wasSeen;
        internal Agent3D _seeker;
        internal World3D _world;
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }

        public float Direction
        {
            get => Rotation.Y;
            set
            {
                Rotation = new Vector3(Rotation.X, NormalizeAngle(value), Rotation.Z);
            }
        }

        public float VisionRadius { get; set; }
        public float VisionAngle { get; set; }
        public float AgentRadius { get; set; }
        public float Speed { get; set; }
        public bool IsSeeker { get; set; }
        public Color Color { get; set; }

        // New: Unique Id and individual memory
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public AgentMemory Memory { get; } = new AgentMemory();

        // Флаг: видит ли этот агент свою цель (используется для подсветки конуса у Seeker)
        public bool IsSeeingTarget { get; set; } = false;

        // Ссылка на командный blackboard (общие известные стены, последние известные позиции целей)
        public TeamBlackboard? TeamBoard { get; set; }

        // Глобальный флаг отрисовки конусов взгляда (управляется Simulation3D)
        public static bool ShowVisionCones { get; set; } = true;

        private HashSet<(int x, int z)> ExploredCells { get; } = new();
        private HashSet<(int x, int z)> VisuallyExploredCells { get; } = new();

        public HashSet<(int x, int z)> KnownWalls { get; } = new();

        private int _worldSize = 64;

        public void InitWorldSize(int size) => _worldSize = size;
        public void SetWorld(World3D world) => _world = world;

        public static int ToGridX(float x, int size) => Math.Clamp((int)Math.Floor(x), 0, size - 1);
        public static int ToGridZ(float z, int size) => Math.Clamp((int)Math.Floor(z), 0, size - 1);
        private (int x, int z) ToGridCoords(Vector3 position) => (ToGridX(position.X, _worldSize), ToGridZ(position.Z, _worldSize));

        public int GridX => ToGridX(Position.X, _worldSize);
        public int GridZ => ToGridZ(Position.Z, _worldSize);

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            // Просто используем Instance, getter сам lazy-загрузит singleton
            var cfg = isSeeker ? GameConfig.Instance.Seeker : GameConfig.Instance.Hider;
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, NormalizeAngle(initialRotation), 0);
            _renderYawDeg = NormalizeAngle(initialRotation + (isSeeker ? AgentModelCache.SeekerBaseYawOffsetDeg : AgentModelCache.HiderBaseYawOffsetDeg));
            VisionRadius = cfg.VisionRadius;
            VisionAngle = cfg.VisionAngle;
            Speed = cfg.Speed;
            AgentRadius = cfg.AgentRadius;
            Color = isSeeker
                ? new Color(0, 121, 241, 255)    // Можно вынести в GameConfig, если понадобится настройка
                : new Color(0, 228, 48, 255);

            _prevLogicYawDeg = NormalizeAngle(initialRotation);

            if (IsSeeker)
            {
                ExploredCells.Add(ToGridCoords(position));
            }
        }

        private static float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public void Rotate(float degrees)
        {
            Direction += degrees;
        }

        public int GetExploredCount() => ExploredCells.Count;
        public int GetVisuallyExploredCount() => VisuallyExploredCells.Count;
        public int GetTotalExploredCount() => ExploredCells.Count + VisuallyExploredCells.Count;

        public bool HasExplored(int x, int z) => ExploredCells.Contains((x, z));
        public bool HasVisuallyExplored(int x, int z) => VisuallyExploredCells.Contains((x, z));
        public bool HasExploredAnyway(int x, int z) => ExploredCells.Contains((x, z)) || VisuallyExploredCells.Contains((x, z));
        public bool IsKnownWall(int x, int z) => KnownWalls.Contains((x, z));

        public void ResetExploration()
        {
            ExploredCells.Clear();
            VisuallyExploredCells.Clear();
            KnownWalls.Clear();
            if (IsSeeker)
            {
                ExploredCells.Add(ToGridCoords(Position));
            }
        }

        public int UpdateVisualExploration(World3D world)
        {
            int newCellsExplored = 0;
            int segments = 60;
            float startAngle = Direction - VisionAngle / 2f;
            float endAngle = Direction + VisionAngle / 2f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
                Vector3 dir = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));
                float step = 0.2f;

                for (float t = step; t <= VisionRadius; t += step)
                {
                    Vector3 point = Position + dir * t;
                    int gridX = ToGridX(point.X, world.Size);
                    int gridZ = ToGridZ(point.Z, world.Size);

                    if (point.X < 0 || point.X >= world.Size || point.Z < 0 || point.Z >= world.Size)
                        break;

                    if (world.IsBlocked(gridX, gridZ))
                    {
                        KnownWalls.Add((gridX, gridZ));
                        break;
                    }

                    if (!VisuallyExploredCells.Contains((gridX, gridZ)) && !ExploredCells.Contains((gridX, gridZ)))
                    {
                        VisuallyExploredCells.Add((gridX, gridZ));
                        newCellsExplored++;
                    }
                }
            }
            return newCellsExplored;
        }

        public bool MoveWithCollisionAvoidance(World3D world, float deltaTime, Agent3D other = null)
        {
            // Дальность заглядывания вперёд: чуть больше шага
            float lookahead = MathF.Max(Speed * deltaTime * 2f, 0.6f);

            // Выбираем лучший целевой угол с учётом карты стен и реальной проходимости
            float? bestAngle = GetBestDirection(world, lookahead);

            if (bestAngle.HasValue)
            {
                float target = bestAngle.Value;
                float angleDiff = target - Direction;
                if (angleDiff > 180f) angleDiff -= 360f;
                if (angleDiff < -180f) angleDiff += 360f;

                // Упреждающий разворот к лучшему направлению
                float maxTurn = 15f;
                if (MathF.Abs(angleDiff) > 1f)
                {
                    float turn = Clamp(angleDiff, -maxTurn, maxTurn);
                    Rotate(turn);
                    // Для Seeker сохраняем прежнее поведение: приоритет повороту без шага в этом кадре,
                    // чтобы не "тыкаться" в стену. Для Hider — продолжаем движение, чтобы не замирать при обнаружении.
                    if (IsSeeker)
                        return false;
                    // Hider: не возвращаемся, позволяем смещение вперёд с уже скорректированным направлением
                    // (векторы движения будут пересчитаны ниже при необходимости)
                }
            }
            else
            {
                // Нет хорошего направления — пробуем аккуратно развернуться
                for (int attempt = 1; attempt <= 8; attempt++)
                {
                    float angleOffset = (attempt % 2 == 0) ? attempt * 15 : -attempt * 15;
                    float testAngle = NormalizeAngle(Direction + angleOffset);
                    float testRadians = testAngle * MathF.PI / 180f;
                    Vector3 testForward = new Vector3(
                        MathF.Cos(testRadians) * Speed * deltaTime,
                        0,
                        MathF.Sin(testRadians) * Speed * deltaTime
                    );
                    Vector3 testPosition = Position + testForward;
                    if (IsPositionValid(testPosition, world))
                    {
                        Rotate(Math.Sign(angleOffset) * 10);
                        return false;
                    }
                }
                return false;
            }

            // Пробуем шаг вперёд по (возможно уже скорректированному) направлению
            float radians = Direction * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * Speed * deltaTime,
                0,
                MathF.Sin(radians) * Speed * deltaTime
            );

            Vector3 newPosition = Position + forward;

            if (!IsPositionValid(newPosition, world))
            {
                var gridCoords = ToGridCoords(newPosition);
                if (world.IsBlocked(gridCoords.x, gridCoords.z))
                    KnownWalls.Add(gridCoords);
                // Столкновение — отметили и не двигаемся в этом кадре
                return false;
            }

            if (other != null)
            {
                float minDist = this.AgentRadius + other.AgentRadius;
                float currentDist = Vector3.Distance(newPosition, other.Position);
                float currentAngle = Direction;

                // Hider — упреждающий разворот, если его видит Seeker
                if (!IsSeeker && IsSeenBy(other, world))
                {
                    Vector3 escapeDir = Vector3.Normalize(Position - other.Position);
                    float escapeAngle = MathF.Atan2(escapeDir.Z, escapeDir.X) * 180f / MathF.PI;
                    float angleDiff = escapeAngle - currentAngle;

                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;

                    // Разворот от преследователя, но без остановки: позволяем шагнуть в этом же кадре
                    Rotate(Math.Sign(angleDiff) * 10f);

                    // Пересчитываем вектор движения с учётом нового направления
                    radians = Direction * MathF.PI / 180f;
                    forward = new Vector3(
                        MathF.Cos(radians) * Speed * deltaTime,
                        0,
                        MathF.Sin(radians) * Speed * deltaTime
                    );
                    newPosition = Position + forward;
                    // Не возвращаемся — продолжаем обычную проверку столкновений ниже
                }

                // Избегаем чрезмерного сближения
                if (currentDist < minDist)
                {
                    Vector3 avoidDir = Vector3.Normalize(Position - other.Position);
                    float avoidAngle = MathF.Atan2(avoidDir.Z, avoidDir.X) * 180f / MathF.PI;
                    float angleDiff = avoidAngle - currentAngle;

                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;

                    Rotate(Math.Sign(angleDiff) * 5f);
                    return false;
                }
            }

            // Двигаемся
            Position = newPosition;

            if (IsSeeker)
            {
                var gridCoords = ToGridCoords(Position);
                if (!ExploredCells.Contains(gridCoords))
                {
                    ExploredCells.Add(gridCoords);
                }
            }

            return true;
        }

        // Вариант с учётом нескольких соседей: выбираем ближайшего для реакций уклонения/погони
        public bool MoveWithCollisionAvoidance(World3D world, float deltaTime, IReadOnlyList<Agent3D> others)
        {
            Agent3D nearest = null;
            float nearestDist = float.PositiveInfinity;
            if (others != null)
            {
                foreach (var o in others)
                {
                    if (o == null || ReferenceEquals(o, this)) continue;
                    float d = Vector3.Distance(this.Position, o.Position);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = o;
                    }
                }
            }
            return MoveWithCollisionAvoidance(world, deltaTime, nearest);
        }

        public float? GetBestDirection(World3D world, float lookaheadDistance = 1.0f)
        {
            // Memory-based steering vectors
            var memCfg = GameConfig.Instance.Memory;
            Vector3 targetVec = Vector3.Zero;
            if (Memory.TryGetLastOpponent(out var opp) && opp.Confidence >= memCfg.MinConfidenceForNav)
            {
                Vector3 toOpp = opp.LastPosition - Position;
                if (!IsSeeker) toOpp = -toOpp; // hider moves away
                if (toOpp.LengthSquared() > 1e-6f) targetVec = Vector3.Normalize(toOpp);
            }

            Vector3 repulse = Vector3.Zero;
            foreach (var ally in Memory.GetAllies())
            {
                Vector3 away = Position - ally.LastPosition;
                float dist = away.Length();
                if (dist < 1e-4f) continue;
                // Decay with distance and age via confidence
                float w = ally.Confidence * (1.0f / MathF.Max(0.1f, dist));
                repulse += (away / dist) * w;
            }
            if (repulse.LengthSquared() > 1e-6f) repulse = Vector3.Normalize(repulse);

            // Exploration fallback: forward vector
            float forwardRad = Direction * MathF.PI / 180f;
            Vector3 exploration = new Vector3(MathF.Cos(forwardRad), 0, MathF.Sin(forwardRad));

            float w1 = IsSeeker ? memCfg.SeekerW1_Target : memCfg.HiderW1_Target;
            float w2 = IsSeeker ? memCfg.SeekerW2_AllyRepulsion : memCfg.HiderW2_AllyRepulsion;
            float w3 = IsSeeker ? memCfg.SeekerW3_Exploration : memCfg.HiderW3_Exploration;

            // If we have a fresh target trace, de-emphasize exploration (especially for hider)
            if (targetVec != Vector3.Zero)
            {
                w3 *= IsSeeker ? 0.4f : 0.0f; // seekers still explore a bit, hiders focus on evasion
            }
            else if (repulse != Vector3.Zero)
            {
                // Если есть отталкивание союзников, но нет цели — ослабляем вклад исследования,
                // чтобы предпочесть поворот от скопления союзников.
                w3 *= 0.2f;
            }

            Vector3 steer = w1 * targetVec + w2 * repulse + w3 * exploration;
            Vector3 steerN = steer.LengthSquared() > 1e-6f ? Vector3.Normalize(steer) : Vector3.Zero;

            // Кандидаты — симметричные смещения от текущего направления
            float[] offsets = new float[] { 0, 15, -15, 30, -30, 45, -45, 60, -60, 90, -90, 120, -120, 150, -150, 180 };
            float bestScore = float.NegativeInfinity;
            float? bestAngle = null;

            foreach (var offset in offsets)
            {
                if (repulse != Vector3.Zero && targetVec == Vector3.Zero && Math.Abs(offset) < 1e-6f)
                    continue; // при отталкивании союзников избегаем строго 0° как первого кандидата
                float testAngle = NormalizeAngle(Direction + offset);
                float radians = testAngle * MathF.PI / 180f;
                Vector3 dir = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));

                // Оценка: сколько свободного пространства вперёд + штраф за поворот и известные стены + согласованность с steer
                float score = 0f;
                float step = 0.2f;

                // Штраф, если первый шаг ведёт в уже известную стену
                Vector3 firstPos = Position + dir * 0.6f;
                var firstCell = ToGridCoords(firstPos);
                if (KnownWalls.Contains(firstCell)) score -= 5f;

                bool blocked = false;
                float freeDist = 0f;

                for (float d = step; d <= lookaheadDistance; d += step)
                {
                    Vector3 p = Position + dir * d;
                    int gx = ToGridX(p.X, world.Size);
                    int gz = ToGridZ(p.Z, world.Size);

                    if (!world.IsInside(gx, gz) || world.IsBlocked(gx, gz) || !IsPositionValid(p, world))
                    {
                        if (world.IsBlocked(gx, gz))
                            KnownWalls.Add((gx, gz));
                        blocked = true;
                        break;
                    }

                    freeDist = d;
                }

                score += freeDist;                     // чем дальше свободно — тем лучше
                score -= MathF.Abs(offset) * 0.02f;    // маленький штраф за поворот
                if (!blocked) score += 0.2f;           // бонус, если путь открыт на всю длину

                if (steerN != Vector3.Zero)
                {
                    float align = Vector3.Dot(dir, steerN); // [-1,1]
                    float alignMul = (targetVec != Vector3.Zero) ? 5.0f : 1.0f;
                    score += align * alignMul; // add alignment preference
                }

                // Лёгкий tie-breaker: если есть сила отталкивания союзников,
                // слегка штрафуем движение строго вперёд (0°), чтобы предпочесть поворот при равенстве.
                if (repulse != Vector3.Zero && Math.Abs(offset) < 1e-6f)
                    score -= 0.0001f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAngle = testAngle;
                }
            }

            return bestAngle;
        }

        private bool IsPositionValid(Vector3 pos, World3D world)
        {
            int gridCheckSteps = 16;
            for (int i = 0; i < gridCheckSteps; i++)
            {
                float angle = 2 * MathF.PI * i / gridCheckSteps;
                float checkX = pos.X + MathF.Cos(angle) * AgentRadius * 0.9f;
                float checkZ = pos.Z + MathF.Sin(angle) * AgentRadius * 0.9f;
                int gx = ToGridX(checkX, world.Size);
                int gz = ToGridZ(checkZ, world.Size);

                if (!world.IsInside(gx, gz) || world.IsBlocked(gx, gz))
                    return false;
            }
            return true;
        }

        private static float SmallestAngleDiffDeg(float a, float b)
        {
            float diff = MathF.Abs(a - b) % 360f;
            return diff > 180f ? 360f - diff : diff;
        }

        private static float AngleDegFromVector(Vector3 v)
        {
            float ang = MathF.Atan2(v.Z, v.X) * 180f / MathF.PI;
            if (ang < 0f) ang += 360f;
            return ang;
        }

        // Точная проверка прямой видимости до произвольной точки цели через тот же реймарчинг, что и в визуализации
        private bool HasPreciseLineOfSight(Vector3 target, World3D world)
        {
            float dist = Vector3.Distance(Position, target);
            if (dist < 1e-4f) return true;

            Vector3 dir = Vector3.Normalize(target - Position);
            Vector3 hit = GetPreciseRayEndPoint(Position, dir, dist, world);

            // Если луч дошёл до цели (с небольшим допуском), значит стена не блокирует
            return Vector3.Distance(hit, target) <= 0.05f;
        }

        public bool CanSee(Agent3D other, World3D world)
        {
            // Быстрый отсев по дистанции (учёт радиуса цели)
            float centerDist = Vector3.Distance(Position, other.Position);
            float targetRadius = other.AgentRadius;
            if (centerDist > VisionRadius + targetRadius) return false;

            float halfFov = VisionAngle * 0.5f;
            const float angleEps = 0.5f; // небольшой допуск на неточности вычислений

            // Угол до центра цели
            Vector3 toCenter = Vector3.Normalize(other.Position - Position);
            float centerAngleDeg = AngleDegFromVector(toCenter);

            // Минимальная разница углов до центра и угловой радиус цели
            float centerAngleDiff = SmallestAngleDiffDeg(centerAngleDeg, Direction);

            float phiDeg = 0f; // угловой полурадиус цели
            if (centerDist > 1e-4f)
            {
                float ratio = MathF.Min(1f, targetRadius / centerDist);
                phiDeg = MathF.Asin(ratio) * 180f / MathF.PI;
            }

            // Если целиком вне FOV даже с учётом радиуса цели — нет видимости
            if (centerAngleDiff > (halfFov + phiDeg + angleEps)) return false;

            // Если центр цели в FOV и есть точная LoS до центра — достаточно
            if (centerAngleDiff <= halfFov + angleEps &&
                HasPreciseLineOfSight(other.Position, world))
            {
                return true;
            }

            // Проверяем точки по окружности диска цели с большей плотностью
            const int samples = 64;
            for (int i = 0; i < samples; i++)
            {
                float ang = 2f * MathF.PI * (i / (float)samples);
                Vector3 offset = new Vector3(MathF.Cos(ang), 0f, MathF.Sin(ang)) * targetRadius;
                Vector3 p = other.Position + offset;

                float dist = Vector3.Distance(Position, p);
                if (dist > VisionRadius) continue;

                Vector3 toPoint = Vector3.Normalize(p - Position);
                float pointAngleDeg = AngleDegFromVector(toPoint);
                float diff = SmallestAngleDiffDeg(pointAngleDeg, Direction);
                if (diff <= halfFov + angleEps)
                {
                    if (HasPreciseLineOfSight(p, world))
                        return true;
                }
            }

            return false;
        }

        public bool IsSeenBy(Agent3D other, World3D world)
        {
            // Единая логика: используем CanSee наблюдателя
            return other.CanSee(this, world);
        }






        private Vector3 GetPreciseRayEndPoint(Vector3 start, Vector3 direction, float maxDistance, World3D world)
        {
            float step = 0.05f;
            Vector3 currentPos = start;
            Vector3 lastValidPos = start;
            for (float t = 0; t <= maxDistance; t += step)
            {
                currentPos = start + direction * t;
                int gridX = ToGridX(currentPos.X, world.Size);
                int gridZ = ToGridZ(currentPos.Z, world.Size);
                if (!world.IsInside(gridX, gridZ) || world.IsBlocked(gridX, gridZ))
                {
                    return lastValidPos;
                }
                lastValidPos = currentPos;
            }
            return start + direction * maxDistance;
        }


        public bool[] GetKnownWallsFlat(int worldSize)
        {
            bool[] arr = new bool[worldSize * worldSize];
            foreach (var (x, z) in KnownWalls)
            {
                if (x >= 0 && x < worldSize && z >= 0 && z < worldSize)
                    arr[x + z * worldSize] = true;
            }
            return arr;
        }
    }
}
