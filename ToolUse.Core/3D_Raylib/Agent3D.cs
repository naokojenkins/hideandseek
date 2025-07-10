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
        public float VisionRadius { get; set; } = 8.0f;  // Увеличиваем радиус обзора
        public float VisionAngle { get; set; } = 90.0f; // Расширяем угол обзора
        public bool IsSeeker { get; set; }
        public Raylib_cs.Color Color { get; set; }
        public float Speed { get; set; } = 2.0f;

        // Система отслеживания исследованных клеток
        private HashSet<(int x, int z)> ExploredCells { get; } = new HashSet<(int, int)>();

        
        // Для совместимости с 2D версией
        public int X => (int)Math.Round(Position.X);
        public int Y => (int)Math.Round(Position.Z); // В 3D Z соответствует Y из 2D
        public float Angle => Rotation.Y;

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, initialRotation, 0);
            Color = isSeeker ? Raylib_cs.Color.Blue : Raylib_cs.Color.Green;

            // Добавляем стартовую позицию как исследованную
            if (IsSeeker)
            {
                ExploredCells.Add(((int)Math.Round(position.X), (int)Math.Round(position.Z)));
            }
        }

        public void Rotate(float degrees)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y + degrees, Rotation.Z);
            // Нормализуем угол в диапазоне 0-360
            if (Rotation.Y >= 360f) Rotation = new Vector3(Rotation.X, Rotation.Y - 360f, Rotation.Z);
            if (Rotation.Y < 0f) Rotation = new Vector3(Rotation.X, Rotation.Y + 360f, Rotation.Z);
        }

        public void MoveForward(World3D world, float deltaTime)
        {
            float radians = Rotation.Y * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * Speed * deltaTime,
                0,
                MathF.Sin(radians) * Speed * deltaTime
            );
            
            Vector3 newPosition = Position + forward;
            
            // Проверяем коллизии
            if (!world.IsBlocked((int)newPosition.X, (int)newPosition.Z))
            {
                Position = newPosition;

                // Добавляем новую клетку как исследованную для seeker'а
                if (IsSeeker)
                {
                    ExploredCells.Add(((int)Math.Round(Position.X), (int)Math.Round(Position.Z)));
                }
            }
        }

        // Методы для работы с исследованными клетками
        public int GetExploredCount() => ExploredCells.Count;

        public bool HasExplored(int x, int z) => ExploredCells.Contains((x, z));

        public void ResetExploration()
        {
            ExploredCells.Clear();
            if (IsSeeker)
            {
                ExploredCells.Add(((int)Math.Round(Position.X), (int)Math.Round(Position.Z)));
            }
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

            // Проверяем коллизии со стенами
            if (world.IsBlocked((int)newPosition.X, (int)newPosition.Z))
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

                    if (!world.IsBlocked((int)testPosition.X, (int)testPosition.Z))
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

            // Добавляем новую клетку как исследованную для seeker'а
            if (IsSeeker)
            {
                ExploredCells.Add(((int)Math.Round(Position.X), (int)Math.Round(Position.Z)));
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

            // Отладочная информация
            //Console.WriteLine($"Distance: {distance:F2}, AngleToOther: {angleToOther:F2}, Direction: {currentDirection:F2}, AngleDiff: {angleDiff:F2}, Threshold: {VisionAngle/2f:F2}");
    
            if (angleDiff > VisionAngle / 2f) return false;

            // Проверяем линию видимости - ИСПРАВЛЕНИЕ ЗДЕСЬ
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

            // Рисуем направление взгляда - делаем его длиннее и толще для лучшей видимости
            //float radians = Rotation.Y * MathF.PI / 180f;
            /*Vector3 forward = new Vector3(
                MathF.Cos(radians) * 2.0f,  // Увеличиваем длину
                0.1f,
                MathF.Sin(radians) * 2.0f   // Увеличиваем длину
            );
            
            Vector3 startPos = Position + new Vector3(0, 0.8f, 0);
            Vector3 endPos = startPos + forward;*/
            
            // Рисуем толстую желтую линию
            
            
            // Добавляем еще несколько линий рядом для "толщины"
            /*Raylib.DrawLine3D(startPos + new Vector3(0.05f, 0, 0), endPos + new Vector3(0.05f, 0, 0), Raylib_cs.Color.Yellow);
            Raylib.DrawLine3D(startPos + new Vector3(-0.05f, 0, 0), endPos + new Vector3(-0.05f, 0, 0), Raylib_cs.Color.Yellow);
            Raylib.DrawLine3D(startPos + new Vector3(0, 0, 0.05f), endPos + new Vector3(0, 0, 0.05f), Raylib_cs.Color.Yellow);
            Raylib.DrawLine3D(startPos + new Vector3(0, 0, -0.05f), endPos + new Vector3(0, 0, -0.05f), Raylib_cs.Color.Yellow);
        */
        }

       
        
        public void DrawVisionCone(World3D world, Raylib_cs.Color? visionColor = null)
        {
            Raylib_cs.Color coneColor = visionColor ?? new Raylib_cs.Color(255, 255, 0, 80);
    
            int segments = 40; // Увеличиваем количество сегментов для более гладкого конуса
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            Vector3 agentPos = Position + new Vector3(0, 0.05f, 0); // Немного приподнимаем над землей

            // Собираем все точки для полигона
            List<Vector3> points = new List<Vector3>();
            points.Add(agentPos); // Центр конуса

            // Добавляем точки по дуге
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                float radians = angle * MathF.PI / 180f;
        
                // Используем правильную систему координат
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));
                Vector3 rayEnd = GetRayEndPoint(Position, direction, VisionRadius, world);
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
                if (Vector3.Distance(p1, p2) > 0.1f && Vector3.Distance(p2, p3) > 0.1f && Vector3.Distance(p1, p3) > 0.1f)
                {
                    // Рисуем треугольник с обеих сторон для лучшей видимости
                    Raylib.DrawTriangle3D(p1, p2, p3, coneColor);
                    Raylib.DrawTriangle3D(p1, p3, p2, coneColor);
                }
            }
    
            Raylib.EndBlendMode();
    
            // Убираем линии контура - теперь только заливка!
        }

        private Vector3 GetRayEndPoint(Vector3 start, Vector3 direction, float maxDistance, World3D world)
        {
            Vector3 rayEnd = start + direction * maxDistance;

            // Проверяем коллизии по пути с шагом
            float step = 0.5f;
            for (float t = step; t <= maxDistance; t += step)
            {
                Vector3 point = start + direction * t;
                
                // Проверяем границы мира
                if (point.X < 0 || point.X >= world.Size || point.Z < 0 || point.Z >= world.Size)
                {
                    rayEnd = start + direction * (t - step);
                    break;
                }
                
                // Проверяем коллизии
                if (world.IsBlocked((int)point.X, (int)point.Z))
                {
                    rayEnd = start + direction * (t - step);
                    break;
                }
            }

            return rayEnd;
        }
    }
}