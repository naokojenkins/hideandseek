using System;
using System.Numerics;
using System.Collections.Generic;
using Raylib_cs;
using ToolUse.Core.Config;

namespace ToolUse.Core.RaylibThreeD
{
    public class Agent3D
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

        // Система отслеживания исследованных клеток
        private HashSet<(int x, int z)> ExploredCells { get; } = new();
        private HashSet<(int x, int z)> VisuallyExploredCells { get; } = new();

        // === Карта замеченных стен ===
        public HashSet<(int x, int z)> KnownWalls { get; } = new();

        private int _worldSize = 64; // По умолчанию

        public void InitWorldSize(int size) => _worldSize = size;

        private static int ToGridX(float x, int size) => Math.Clamp((int)Math.Floor(x), 0, size - 1);
        private static int ToGridZ(float z, int size) => Math.Clamp((int)Math.Floor(z), 0, size - 1);
        private (int x, int z) ToGridCoords(Vector3 position) => (ToGridX(position.X, _worldSize), ToGridZ(position.Z, _worldSize));

        public int GridX => ToGridX(Position.X, _worldSize);
        public int GridZ => ToGridZ(Position.Z, _worldSize);

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            var cfg = isSeeker ? GameConfig.Load().Seeker : GameConfig.Load().Hider;
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, NormalizeAngle(initialRotation), 0);
            VisionRadius = cfg.VisionRadius;
            VisionAngle = cfg.VisionAngle;
            Speed = cfg.Speed;
            AgentRadius = cfg.AgentRadius;
            Color = isSeeker
                ? new Color(0, 121, 241, 255)    // BLUE
                : new Color(0, 228, 48, 255);    // GREEN

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

        public void Rotate(float degrees)
        {
            Direction += degrees;
        }

        // === Исследование клеток ===
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

        /// <summary>
        /// Обновляет визуальное исследование и запоминает стены. Возвращает количество новых уникальных визуально исследованных клеток.
        /// </summary>
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

        /// <summary>
        /// Движение вперёд с проверкой коллизий. Агент не может заходить своим телом в стены или на другого агента.
        /// </summary>
        public bool MoveWithCollisionAvoidance(World3D world, float deltaTime, Agent3D other = null)
        {
            // Проверяем, есть ли в направлении движения известная стена
            var bestDirection = GetBestDirection(world);
            if (bestDirection == Vector3.Zero)
            {
                // Нет безопасного направления — пытаемся повернуть
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

            float radians = Direction * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * Speed * deltaTime,
                0,
                MathF.Sin(radians) * Speed * deltaTime
            );

            Vector3 newPosition = Position + forward;

            // Проверяем, не выйдет ли тело агента за пределы пустых клеток (коллизия по AgentRadius)
            if (!IsPositionValid(newPosition, world))
            {
                var gridCoords = ToGridCoords(newPosition);
                if (world.IsBlocked(gridCoords.x, gridCoords.z))
                    KnownWalls.Add(gridCoords);

                // Пробуем повернуть, чтобы обойти препятствие
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

            // Проверка столкновения с другим агентом (по AgentRadius)
            // Проверка столкновения с другим агентом (по AgentRadius)
            // Проверка столкновения с другим агентом (по AgentRadius)
            if (other != null)
            {
                float minDist = this.AgentRadius + other.AgentRadius;
                float currentDist = Vector3.Distance(newPosition, other.Position);

                // Получаем текущий угол агента один раз
                float currentAngle = Direction;

                // Если это Hider и его видят — убегаем
                if (!IsSeeker && IsSeenBy(other, world))
                {
                    Vector3 escapeDir = Vector3.Normalize(Position - other.Position);
                    float escapeAngle = MathF.Atan2(escapeDir.Z, escapeDir.X) * 180f / MathF.PI;
                    float angleDiff = escapeAngle - currentAngle;

                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;

                    Rotate(Math.Sign(angleDiff) * 10f); // Увеличено с 5°
                    return false;
                }

                // Если сталкиваемся — уворачиваемся
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

            Position = newPosition;

            // Добавляем новую клетку как исследованную (только физически)
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

        public Vector3 GetBestDirection(World3D world)
        {
            float[] angles = { 0, 15, -15, 30, -30, 45, -45, 90, -90 }; // Увеличено
            foreach (var angle in angles)
            {
                float testAngle = NormalizeAngle(Direction + angle);
                float testRadians = testAngle * MathF.PI / 180f;
                Vector3 testForward = new Vector3(MathF.Cos(testRadians), 0, MathF.Sin(testRadians));
                Vector3 testPosition = Position + testForward * 1f;
                var gridCoords = ToGridCoords(testPosition);
                if (!KnownWalls.Contains(gridCoords) && world.IsInside(gridCoords.x, gridCoords.z))
                {
                    return testForward;
                }
            }
            return Vector3.Zero;
        }

        /// <summary>
        /// Проверка, что агент полностью помещается в пустой клетке (с учётом радиуса).
        /// </summary>
        private bool IsPositionValid(Vector3 pos, World3D world)
        {
            int gridCheckSteps = 16; // Увеличено с 8
            for (int i = 0; i < gridCheckSteps; i++)
            {
                float angle = 2 * MathF.PI * i / gridCheckSteps;
                float checkX = pos.X + MathF.Cos(angle) * AgentRadius * 0.9f; // Уменьшен радиус
                float checkZ = pos.Z + MathF.Sin(angle) * AgentRadius * 0.9f;
                int gx = ToGridX(checkX, world.Size);
                int gz = ToGridZ(checkZ, world.Size);

                if (!world.IsInside(gx, gz) || world.IsBlocked(gx, gz))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Может ли этот агент видеть другого (угол + расстояние + препятствия)?
        /// </summary>
        public bool CanSee(Agent3D other, World3D world)
        {
            float distance = Vector3.Distance(Position, other.Position);
            if (distance > VisionRadius) return false;

            Vector3 toOther = Vector3.Normalize(other.Position - Position);
            float angleToOther = MathF.Atan2(toOther.Z, toOther.X) * 180f / MathF.PI;
            if (angleToOther < 0) angleToOther += 360f;
            float angleDiff = Math.Abs(angleToOther - Direction);
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;

            if (angleDiff > VisionAngle / 2f) return false;

            return world.HasLineOfSight(Position, other.Position, AgentRadius); // ✅ Теперь с учётом радиуса
        }

        /// <summary>
        /// Проверяет, видит ли другой агент этого агента.
        /// </summary>
        /// <summary>
        /// Проверяет, видит ли другой агент этого агента.
        /// </summary>
        public bool IsSeenBy(Agent3D other, World3D world)
        {
            float distance = Vector3.Distance(other.Position, Position);
            if (distance > other.VisionRadius) return false;

            Vector3 toThis = Vector3.Normalize(Position - other.Position);
            float angleToThis = MathF.Atan2(toThis.Z, toThis.X) * 180f / MathF.PI;
            if (angleToThis < 0) angleToThis += 360f;
            float angleDiff = Math.Abs(angleToThis - other.Direction);
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;

            if (angleDiff > other.VisionAngle / 2f) return false;

            return world.HasLineOfSight(other.Position, Position);
        }

        public void Draw()
        {
            if (IsSeeker)
            {
                // Seeker рисуется как обычно
                Raylib.DrawCapsule(
                    Position,
                    Position + new Vector3(0, 1.5f, 0),
                    AgentRadius, 8, 8, Color
                );
                return;
            }

            // Hider получает визуальную реакцию, если его видят
            bool isSeen = false;

            // === Временное решение: передача Seeker и World напрямую ===
            // Это нужно будет заменить на более правильную реализацию
            // Например, через ссылку на симуляцию или событие
            if (_seeker != null && _world != null)
            {
                isSeen = IsSeenBy(_seeker, _world);
            }

            if (isSeen)
            {
                // === Режим "видимый" — пульсирующий цвет ===
                float pulse = (float)Math.Sin(Environment.TickCount64 * 0.01) * 0.5f + 0.5f;
                Color alertColor = new Color(
                    (int)(Color.R + (255 - Color.R) * pulse),
                    (int)(Color.G * (1 - pulse)),
                    (int)(Color.B * (1 - pulse)),
                    255
                );
                Raylib.DrawCapsule(
                    Position,
                    Position + new Vector3(0, 1.5f, 0),
                    AgentRadius * 1.3f, // Увеличенный радиус для визуального эффекта
                    8, 8, alertColor
                );
                _wasSeen = true;
            }
            else
            {
                // === Режим "невидимый" — обычный рендеринг ===
                Raylib.DrawCapsule(
                    Position,
                    Position + new Vector3(0, 1.5f, 0),
                    AgentRadius, 8, 8, Color
                );
                _wasSeen = false;
            }
        }

        public void DrawVisionCone(World3D world, Color? visionColor = null)
        {
            Color coneColor = visionColor ?? new Color(255, 255, 0, 80);
            int segments = 60;
            float startAngle = Direction - VisionAngle / 2f;
            float endAngle = Direction + VisionAngle / 2f;
            Vector3 agentPos = Position + new Vector3(0, 0.05f, 0);

            List<Vector3> points = new() { agentPos };
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
                Vector3 dir = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));
                Vector3 rayEnd = GetPreciseRayEndPoint(Position, dir, VisionRadius, world);
                points.Add(rayEnd + new Vector3(0, 0.05f, 0));
            }

            Raylib.BeginBlendMode(BlendMode.Alpha);
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 p1 = points[0], p2 = points[i], p3 = points[i + 1];
                if (Vector3.Distance(p1, p2) > 0.01f && Vector3.Distance(p2, p3) > 0.01f && Vector3.Distance(p1, p3) > 0.01f)
                {
                    Raylib.DrawTriangle3D(p1, p2, p3, coneColor);
                    Raylib.DrawTriangle3D(p1, p3, p2, coneColor);
                }
            }
            Raylib.EndBlendMode();
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

        /// <summary>
        /// Возвращает известные агенту стены в виде одномерного массива bool[] (для RL State).
        /// Размер: worldSize * worldSize, порядок: [x + z * worldSize]
        /// </summary>
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