using System;
using System.Numerics;
using Raylib_cs;
using System.Collections.Generic;

namespace ToolUse.Core.RaylibThreeD
{
    public class Agent3D
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Direction
        {
            get => Rotation.Y;
            set => Rotation = new Vector3(Rotation.X, value, Rotation.Z);
        }

        public float VisionRadius { get; set; } = 8.0f;
        public float VisionAngle { get; set; } = 90.0f;
        public bool IsSeeker { get; set; }
        public Color Color { get; set; }
        public float Speed { get; set; } = 2.0f;
        public float AgentRadius { get; set; } = 0.3f; // Радиус для коллизий

        // Клетки, физически посещённые
        private HashSet<(int x, int z)> ExploredCells { get; } = new();
        // Клетки, которые были хотя бы раз видимы
        private HashSet<(int x, int z)> VisuallyExploredCells { get; } = new();

        private static int ToGridX(float x) => (int)MathF.Floor(x);
        private static int ToGridZ(float z) => (int)MathF.Floor(z);
        private static (int x, int z) ToGridCoords(Vector3 position) => (ToGridX(position.X), ToGridZ(position.Z));

        public int GridX => ToGridX(Position.X);
        public int GridZ => ToGridZ(Position.Z);

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, initialRotation, 0);
            Color = isSeeker
                ? new Color(0, 121, 241, 255)    // BLUE
                : new Color(0, 228, 48, 255);    // GREEN

            if (IsSeeker)
            {
                ExploredCells.Add(ToGridCoords(position));
            }
        }

        public void Rotate(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y + degrees, Rotation.Z);
            if (Rotation.Y >= 360f) Rotation = new Vector3(Rotation.X, Rotation.Y - 360f, Rotation.Z);
            if (Rotation.Y < 0f) Rotation = new Vector3(Rotation.X, Rotation.Y + 360f, Rotation.Z);
        }

        public int GetExploredCount() => ExploredCells.Count;
        public int GetVisuallyExploredCount() => VisuallyExploredCells.Count;
        public int GetTotalExploredCount()
        {
            var union = new HashSet<(int, int)>(ExploredCells);
            union.UnionWith(VisuallyExploredCells);
            return union.Count;
        }

        public bool HasExplored(int x, int z) => ExploredCells.Contains((x, z));
        public bool HasVisuallyExplored(int x, int z) => VisuallyExploredCells.Contains((x, z));
        public bool HasExploredAnyway(int x, int z) => ExploredCells.Contains((x, z)) || VisuallyExploredCells.Contains((x, z));

        public void ResetExploration()
        {
            ExploredCells.Clear();
            VisuallyExploredCells.Clear();
            if (IsSeeker)
            {
                ExploredCells.Add(ToGridCoords(Position));
            }
        }

        /// <summary>
        /// Обновляет визуально исследованные клетки.
        /// Возвращает количество новых клеток, которые впервые увидены агентом.
        /// </summary>
        public int UpdateVisualExploration(World3D world)
        {
            if (!IsSeeker) return 0;

            int newCellsExplored = 0;
            int segments = 60;
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));

                float step = 0.2f;
                for (float t = step; t <= VisionRadius; t += step)
                {
                    Vector3 point = Position + direction * t;
                    int gridX = ToGridX(point.X);
                    int gridZ = ToGridZ(point.Z);

                    if (point.X < 0 || point.X >= world.Size || point.Z < 0 || point.Z >= world.Size)
                        break;

                    if (world.IsBlocked(gridX, gridZ))
                        break;

                    // Клетка не должна быть исследована ни физически, ни визуально
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
        /// Проверка коллизии с учетом радиуса агента.
        /// </summary>
        private bool IsPositionBlocked(Vector3 pos, float radius, World3D world)
        {
            int numChecks = 8;
            for (int i = 0; i < numChecks; i++)
            {
                float angle = (float)(2 * Math.PI * i / numChecks);
                float checkX = pos.X + MathF.Cos(angle) * radius;
                float checkZ = pos.Z + MathF.Sin(angle) * radius;
                int gridX = ToGridX(checkX);
                int gridZ = ToGridZ(checkZ);

                if (world.IsBlocked(gridX, gridZ))
                    return true;
            }
            return false;
        }

        public bool MoveWithCollisionAvoidance(World3D world, float deltaTime, Agent3D other = null)
        {
            float radians = Rotation.Y * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * Speed * deltaTime,
                0,
                MathF.Sin(radians) * Speed * deltaTime
            );

            Vector3 newPosition = Position + forward;

            // Проверка коллизии с учетом радиуса
            if (IsPositionBlocked(newPosition, AgentRadius, world))
            {
                bool foundPath = false;
                for (int attempt = 1; attempt <= 8; attempt++)
                {
                    float angleOffset = (attempt % 2 == 0) ? attempt * 15 : -attempt * 15;
                    float testAngle = (Rotation.Y + angleOffset) % 360;
                    float testRadians = testAngle * MathF.PI / 180f;
                    Vector3 testDirection = new Vector3(
                        MathF.Cos(testRadians) * Speed * deltaTime,
                        0,
                        MathF.Sin(testRadians) * Speed * deltaTime
                    );
                    Vector3 testPosition = Position + testDirection;

                    if (!IsPositionBlocked(testPosition, AgentRadius, world))
                    {
                        Rotate(Math.Sign(angleOffset) * 10);
                        foundPath = true;
                        break;
                    }
                }
                return foundPath;
            }

            // Проверка столкновения с другим агентом, если задан
            if (other != null)
            {
                float distanceToOther = Vector3.Distance(newPosition, other.Position);
                float minAgentDist = AgentRadius + other.AgentRadius;
                if (distanceToOther < minAgentDist)
                {
                    Vector3 avoidDirection = Vector3.Normalize(Position - other.Position);
                    float avoidAngle = MathF.Atan2(avoidDirection.Z, avoidDirection.X) * 180f / MathF.PI;
                    float currentAngle = Rotation.Y;

                    float angleDiff = avoidAngle - currentAngle;
                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;

                    Rotate(Math.Sign(angleDiff) * 5f);
                    return false;
                }
            }

            Position = newPosition;

            if (IsSeeker)
            {
                var gridCoords = ToGridCoords(Position);
                // Только добавляем, никогда не удаляем из Visual!
                ExploredCells.Add(gridCoords);
            }

            return true;
        }

        public bool CanSee(Agent3D other, World3D world)
        {
            float distance = Vector3.Distance(Position, other.Position);
            if (distance > VisionRadius) return false;

            Vector3 toOther = Vector3.Normalize(other.Position - Position);
            float angleToOther = MathF.Atan2(toOther.Z, toOther.X) * 180f / MathF.PI;
            if (angleToOther < 0) angleToOther += 360f;

            float currentDirection = Rotation.Y;
            while (currentDirection < 0) currentDirection += 360f;
            while (currentDirection >= 360f) currentDirection -= 360f;

            float angleDiff = Math.Abs(angleToOther - currentDirection);
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;

            if (angleDiff > VisionAngle / 2f) return false;

            return world.HasLineOfSight(this.Position, other.Position);
        }

        public void Draw()
        {
            Raylib.DrawCapsule(
                Position,
                Position + new Vector3(0, 1.5f, 0),
                AgentRadius,
                8,
                8,
                Color
            );
        }

        public void DrawVisionCone(World3D world, Color? visionColor = null)
        {
            Color coneColor = visionColor ?? new Color(255, 255, 0, 80);

            int segments = 60;
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            Vector3 agentPos = Position + new Vector3(0, 0.05f, 0);

            List<Vector3> points = new();
            points.Add(agentPos);

            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));
                Vector3 rayEnd = GetPreciseRayEndPoint(Position, direction, VisionRadius, world);
                points.Add(rayEnd + new Vector3(0, 0.05f, 0));
            }

            Raylib.BeginBlendMode(BlendMode.Alpha);
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 p1 = points[0];
                Vector3 p2 = points[i];
                Vector3 p3 = points[i + 1];

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
                if (currentPos.X < 0 || currentPos.X >= world.Size ||
                    currentPos.Z < 0 || currentPos.Z >= world.Size)
                {
                    return GetBoundaryIntersection(start, direction, lastValidPos, currentPos, world);
                }

                int gridX = ToGridX(currentPos.X);
                int gridZ = ToGridZ(currentPos.Z);

                if (world.IsBlocked(gridX, gridZ))
                {
                    return GetWallIntersection(start, direction, lastValidPos, currentPos, world);
                }
                lastValidPos = currentPos;
            }
            return start + direction * maxDistance;
        }

        private Vector3 GetBoundaryIntersection(Vector3 start, Vector3 direction, Vector3 lastValid, Vector3 firstInvalid, World3D world)
        {
            Vector3 low = lastValid;
            Vector3 high = firstInvalid;

            for (int i = 0; i < 10; i++)
            {
                Vector3 mid = (low + high) * 0.5f;

                if (mid.X >= 0 && mid.X < world.Size && mid.Z >= 0 && mid.Z < world.Size)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }

        private Vector3 GetWallIntersection(Vector3 start, Vector3 direction, Vector3 lastValid, Vector3 firstInvalid, World3D world)
        {
            Vector3 low = lastValid;
            Vector3 high = firstInvalid;

            for (int i = 0; i < 10; i++)
            {
                Vector3 mid = (low + high) * 0.5f;
                int gridX = ToGridX(mid.X);
                int gridZ = ToGridZ(mid.Z);

                if (gridX >= 0 && gridX < world.Size && gridZ >= 0 && gridZ < world.Size &&
                    !world.IsBlocked(gridX, gridZ))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }
    }
}
