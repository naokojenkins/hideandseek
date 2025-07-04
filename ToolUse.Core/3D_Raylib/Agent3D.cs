using System;
using System.Numerics;
using Raylib_cs;

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

        public float VisionRadius { get; set; } = 5.0f;
        public float VisionAngle { get; set; } = 60.0f;
        public bool IsSeeker { get; set; }
        public Raylib_cs.Color Color { get; set; }
        public float Speed { get; set; } = 2.0f;
        
        // Для совместимости с 2D версией
        public int X => (int)Math.Round(Position.X);
        public int Y => (int)Math.Round(Position.Z); // В 3D Z соответствует Y из 2D
        public float Angle => Rotation.Y;

        public Agent3D(Vector3 position, bool isSeeker, float initialRotation = 0f)
        {
            Position = position;
            IsSeeker = isSeeker;
            Rotation = new Vector3(0, initialRotation, 0);
            Color = isSeeker ? Raylib_cs.Color.Red : Raylib_cs.Color.Green;
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
            float angleDiff = Math.Abs(angleToOther - Rotation.Y);
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;
            
            if (angleDiff > VisionAngle / 2f) return false;

            // Проверяем линию видимости
            return world.HasLineOfSight(this, other);
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

            // Рисуем направление взгляда
            float radians = Rotation.Y * MathF.PI / 180f;
            Vector3 forward = new Vector3(
                MathF.Cos(radians) * 0.8f,
                0.1f,
                MathF.Sin(radians) * 0.8f
            );
            
            Raylib.DrawLine3D(
                Position + new Vector3(0, 0.8f, 0),
                Position + new Vector3(0, 0.8f, 0) + forward,
                Raylib_cs.Color.Yellow
            );
        }

        public void DrawVisionCone(World3D world, Raylib_cs.Color? visionColor = null)
        {
            // Рисуем конус видимости полупрозрачными линиями
            float startAngle = Rotation.Y - VisionAngle / 2f;
            float endAngle = Rotation.Y + VisionAngle / 2f;

            // Используем переданный цвет или устанавливаем по умолчанию
            Raylib_cs.Color coneColor = visionColor ?? new Raylib_cs.Color(255, 255, 0, 60);

            for (float angle = startAngle; angle <= endAngle; angle += 3f)
            {
                float radians = angle * MathF.PI / 180f;
                Vector3 direction = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));

                // Трассируем луч до препятствия
                float maxDistance = VisionRadius;
                Vector3 rayEnd = Position + direction * maxDistance;

                // Проверяем коллизии по пути
                for (float t = 0.2f; t <= maxDistance; t += 0.2f)
                {
                    Vector3 point = Position + direction * t;
                    if (world.IsBlocked((int)point.X, (int)point.Z))
                    {
                        rayEnd = Position + direction * (t - 0.2f);
                        break;
                    }
                }

                Raylib.DrawLine3D(
                    Position + new Vector3(0, 0.3f, 0),
                    rayEnd + new Vector3(0, 0.3f, 0),
                    coneColor
                );
            }
        }
    }
}