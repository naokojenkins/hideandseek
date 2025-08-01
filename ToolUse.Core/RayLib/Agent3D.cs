using System;
using System.Numerics;
using Raylib_cs;
using System.Collections.Generic;
using ToolUse.Core.Config;

namespace ToolUse.Core.RaylibThreeD
{
    public class Agent3D
    {
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
                        // === Новый блок: агент видит стену, запоминает координату ===
                        KnownWalls.Add((gridX, gridZ));
                        break;
                    }

                    // Только если не исследована ни визуально, ни физически
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
                // === Также фиксируем стены после физического столкновения ===
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
            if (other != null)
            {
                float minDist = this.AgentRadius + other.AgentRadius;
                if (Vector3.Distance(newPosition, other.Position) < minDist)
                {
                    Vector3 avoidDir = Vector3.Normalize(Position - other.Position);
                    float avoidAngle = MathF.Atan2(avoidDir.Z, avoidDir.X) * 180f / MathF.PI;
                    float currentAngle = Direction;
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

        /// <summary>
        /// Проверка, что агент полностью помещается в пустой клетке (с учётом радиуса).
        /// </summary>
        private bool IsPositionValid(Vector3 pos, World3D world)
        {
            int gridCheckSteps = 8; // Проверяем по кругу
            for (int i = 0; i < gridCheckSteps; i++)
            {
                float angle = 2 * MathF.PI * i / gridCheckSteps;
                float checkX = pos.X + MathF.Cos(angle) * AgentRadius;
                float checkZ = pos.Z + MathF.Sin(angle) * AgentRadius;
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

            return world.HasLineOfSight(Position, other.Position);
        }

        public void Draw()
        {
            Raylib.DrawCapsule(
                Position,
                Position + new Vector3(0, 1.5f, 0),
                AgentRadius, 8, 8, Color
            );
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
