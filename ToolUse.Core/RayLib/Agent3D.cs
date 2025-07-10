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
        
        // Свойство для более удобного доступа к направлению (угол по оси Y)
        public float Direction
        {
            get => Rotation.Y;
            set => Rotation = new Vector3(Rotation.X, value, Rotation.Z);
        }
        
        public float VisionRadius { get; set; } = 8.0f;
        public float VisionAngle { get; set; } = 90.0f;
        public bool IsSeeker { get; set; }
        public Raylib_cs.Color Color { get; set; }
        public float Speed { get; set; } = 2.0f;

        // Система отслеживания исследованных клеток
        private HashSet<(int x, int z)> ExploredCells { get; } = new HashSet<(int, int)>();
        private HashSet<(int x, int z)> VisuallyExploredCells { get; } = new HashSet<(int, int)>();

        // Вспомогательные методы для единообразного преобразования координат
        private static int ToGridX(float x) => (int)Math.Floor(x);
        private static int ToGridZ(float z) => (int)Math.Floor(z);
        private static (int x, int z) ToGridCoords(Vector3 position) => (ToGridX(position.X), ToGridZ(position.Z));

        // Свойства для получения координат сетки
        public int GridX => ToGridX(Position.X);
        public int GridZ => ToGridZ(Position.Z);

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, initialRotation, 0);
            Color = isSeeker ? Raylib_cs.Color.Blue : Raylib_cs.Color.Green;

            // Добавляем стартовую позицию как исследованную
            if (IsSeeker)
            {
                ExploredCells.Add(ToGridCoords(position));
            }
        }

        public void Rotate(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y + degrees, Rotation.Z);
            // Нормализуем угол в диапазоне 0-360
            if (Rotation.Y >= 360f) Rotation = new Vector3(Rotation.X, Rotation.Y - 360f, Rotation.Z);
            if (Rotation.Y < 0f) Rotation = new Vector3(Rotation.X, Rotation.Y + 360f, Rotation.Z);
        }

        // Методы для работы с исследованными клетками
        public int GetExploredCount() => ExploredCells.Count;
        public int GetVisuallyExploredCount() => VisuallyExploredCells.Count;
        public int GetTotalExploredCount() => ExploredCells.Count + VisuallyExploredCells.Count;

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

        // Новый метод для обновления визуального исследования
        public int UpdateVisualExploration(World3D world)
        {
            if (!IsSeeker) return 0;

            int newCellsExplored = 0;
            int segments = 60;
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            // Проходим по всем лучам в конусе зрения
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));

                // Трассируем луч с мелким шагом
                float step = 0.2f;
                for (float t = step; t <= VisionRadius; t += step)
                {
                    Vector3 point = Position + direction * t;
                    int gridX = ToGridX(point.X);
                    int gridZ = ToGridZ(point.Z);

                    // Проверяем границы мира
                    if (point.X < 0 || point.X >= world.Size || point.Z < 0 || point.Z >= world.Size)
                        break;

                    // Если попали в стену, останавливаем трассировку этого луча
                    if (world.IsBlocked(gridX, gridZ))
                        break;

                    // Если клетка еще не исследована визуально и не исследована физически
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
            float radians = Rotation.Y * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * Speed * deltaTime,
                0,
                MathF.Sin(radians) * Speed * deltaTime
            );

            Vector3 newPosition = Position + forward;

            // Проверяем коллизии со стенами - используем единый метод
            if (world.IsBlocked(ToGridX(newPosition.X), ToGridZ(newPosition.Z)))
            {
                // Пробуем найти путь в обход препятствия
                bool foundPath = false;

                // Попробуем повернуть влево или вправо и проверить, есть ли там путь
                for (int attempt = 1; attempt <= 8; attempt++)
                {
                    // Чередуем повороты влево и вправо, увеличивая угол
                    float angleOffset = (attempt % 2 == 0) ? attempt * 15 : -attempt * 15;
                    float testAngle = (Rotation.Y + angleOffset) % 360;

                    float testRadians = testAngle * MathF.PI / 180f;
                    Vector3 testDirection = new Vector3(
                        MathF.Cos(testRadians) * Speed * deltaTime,
                        0,
                        MathF.Sin(testRadians) * Speed * deltaTime
                    );

                    Vector3 testPosition = Position + testDirection;

                    // Используем единый метод для проверки коллизий
                    if (!world.IsBlocked(ToGridX(testPosition.X), ToGridZ(testPosition.Z)))
                    {
                        // Нашли свободный путь - немного поворачиваем в этом направлении
                        Rotate(Math.Sign(angleOffset) * 10);
                        foundPath = true;
                        break;
                    }
                }

                return foundPath; // Не смогли двигаться вперед
            }

            // Проверяем столкновение с другим агентом, если указан
            if (other != null)
            {
                float distanceToOther = Vector3.Distance(newPosition, other.Position);
                if (distanceToOther < 0.8f) // Минимальная дистанция между агентами
                {
                    // Слегка поворачиваем в сторону от другого агента
                    Vector3 avoidDirection = Vector3.Normalize(Position - other.Position);
                    float avoidAngle = MathF.Atan2(avoidDirection.Z, avoidDirection.X) * 180f / MathF.PI;
                    float currentAngle = Rotation.Y;

                    // Определяем кратчайшее направление поворота
                    float angleDiff = avoidAngle - currentAngle;
                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;

                    Rotate(Math.Sign(angleDiff) * 5f);
                    return false; // Не смогли двигаться вперед из-за другого агента
                }
            }

            // Путь свободен, двигаемся
            Position = newPosition;

            // Добавляем новую клетку как исследованную для seeker'а (физическое исследование)
            if (IsSeeker)
            {
                var gridCoords = ToGridCoords(Position);
                ExploredCells.Add(gridCoords);
                
                // Удаляем из визуально исследованных, если была там (физическое исследование приоритетнее)
                VisuallyExploredCells.Remove(gridCoords);
            }

            return true;
        }

        public bool CanSee(Agent3D other, World3D world)
        {
            // Проверяем расстояние
            float distance = Vector3.Distance(Position, other.Position);
            if (distance > VisionRadius) return false;

            // Проверяем угол обзора
            Vector3 toOther = Vector3.Normalize(other.Position - Position);
            float angleToOther = MathF.Atan2(toOther.Z, toOther.X) * 180f / MathF.PI;

            // Нормализуем угол в диапазоне 0-360
            if (angleToOther < 0) angleToOther += 360f;

            // Нормализуем угол текущего направления (для уверенности)
            float currentDirection = Rotation.Y;
            while (currentDirection < 0) currentDirection += 360f;
            while (currentDirection >= 360f) currentDirection -= 360f;

            // Вычисляем разницу между углами
            float angleDiff = Math.Abs(angleToOther - currentDirection);
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;

            if (angleDiff > VisionAngle / 2f) return false;

            // Проверяем линию видимости
            return world.HasLineOfSight(this.Position, other.Position);
        }

        public void Draw()
        {
            // Рисуем агента как капсулу
            Raylib.DrawCapsule(
                Position + new Vector3(0, 0, 0),
                Position + new Vector3(0, 1.5f, 0),
                0.3f, 
                8, 
                8, 
                Color
            );
        }
        
        public void DrawVisionCone(World3D world, Raylib_cs.Color? visionColor = null)
        {
            Raylib_cs.Color coneColor = visionColor ?? new Raylib_cs.Color(255, 255, 0, 80);
    
            int segments = 60; // Увеличиваем количество сегментов для более гладкого конуса
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            Vector3 agentPos = Position + new Vector3(0, 0.05f, 0);

            // Собираем все точки для полигона
            List<Vector3> points = new List<Vector3>();
            points.Add(agentPos); // Центр конуса

            // Добавляем точки по дуге с улучшенным алгоритмом определения столкновений
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
        
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));
                Vector3 rayEnd = GetPreciseRayEndPoint(Position, direction, VisionRadius, world);
                points.Add(rayEnd + new Vector3(0, 0.05f, 0));
            }

            // Включаем альфа-блендинг для полупрозрачности
            Raylib.BeginBlendMode(BlendMode.Alpha);
    
            // Рисуем заливку как набор треугольников
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 p1 = points[0];      // Центр конуса
                Vector3 p2 = points[i];      // Текущая точка
                Vector3 p3 = points[i + 1];  // Следующая точка
        
                // Проверяем, что точки не совпадают
                if (Vector3.Distance(p1, p2) > 0.01f && Vector3.Distance(p2, p3) > 0.01f && Vector3.Distance(p1, p3) > 0.01f)
                {
                    // Рисуем треугольник с обеих сторон для лучшей видимости
                    Raylib.DrawTriangle3D(p1, p2, p3, coneColor);
                    Raylib.DrawTriangle3D(p1, p3, p2, coneColor);
                }
            }
    
            Raylib.EndBlendMode();
        }

        private Vector3 GetPreciseRayEndPoint(Vector3 start, Vector3 direction, float maxDistance, World3D world)
        {
            // Используем более точный алгоритм трассировки луча
            float step = 0.05f; // Уменьшаем шаг для более точного определения коллизий
            Vector3 currentPos = start;
            Vector3 lastValidPos = start;
            
            for (float t = 0; t <= maxDistance; t += step)
            {
                currentPos = start + direction * t;
                
                // Проверяем границы мира
                if (currentPos.X < 0 || currentPos.X >= world.Size || 
                    currentPos.Z < 0 || currentPos.Z >= world.Size)
                {
                    // Находим точную точку пересечения с границей
                    return GetBoundaryIntersection(start, direction, lastValidPos, currentPos, world);
                }
                
                // Используем единый метод для преобразования координат
                int gridX = ToGridX(currentPos.X);
                int gridZ = ToGridZ(currentPos.Z);
                
                // Проверяем коллизии с стенами
                if (world.IsBlocked(gridX, gridZ))
                {
                    // Находим точную точку пересечения со стеной
                    return GetWallIntersection(start, direction, lastValidPos, currentPos, world);
                }
                
                lastValidPos = currentPos;
            }

            return start + direction * maxDistance;
        }

        private Vector3 GetBoundaryIntersection(Vector3 start, Vector3 direction, Vector3 lastValid, Vector3 firstInvalid, World3D world)
        {
            // Бинарный поиск для точного определения пересечения с границей
            Vector3 low = lastValid;
            Vector3 high = firstInvalid;
            
            for (int i = 0; i < 10; i++) // 10 итераций достаточно для хорошей точности
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
            // Бинарный поиск для точного определения пересечения со стеной
            Vector3 low = lastValid;
            Vector3 high = firstInvalid;
            
            for (int i = 0; i < 10; i++) // 10 итераций достаточно для хорошей точности
            {
                Vector3 mid = (low + high) * 0.5f;
                
                // Используем единый метод для преобразования координат
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