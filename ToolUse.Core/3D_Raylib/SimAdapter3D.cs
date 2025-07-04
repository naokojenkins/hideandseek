using System;
using System.Numerics;
using ToolUse.Core.RL;

namespace ToolUse.Core.RaylibThreeD
{
    public class SimAdapter3D
    {
        private readonly World3D _world;
        private readonly Agent3D _seeker;
        private readonly Agent3D _hider;

        public SimAdapter3D(World3D world, Agent3D seeker, Agent3D hider)
        {
            _world = world;
            _seeker = seeker;
            _hider = hider;
        }

        public State GetSeekerState()
        {
            return new State(
                _seeker.X,
                _seeker.Y,
                _hider.X,
                _hider.Y,
                IsVisible()
            );
        }

        public State GetHiderState()
        {
            return new State(
                _hider.X,
                _hider.Y,
                _seeker.X,
                _seeker.Y,
                IsVisible()
            );
        }

        public bool IsVisible()
        {
            return _seeker.CanSee(_hider, _world);
        }

        /// <summary>
        /// Get direction from agent to target as a normalized angle
        /// </summary>
        public float GetDirectionToTarget(Agent3D from, Agent3D to)
        {
            Vector3 toTarget = to.Position - from.Position;
            float angle = MathF.Atan2(toTarget.Z, toTarget.X) * 180f / MathF.PI;

            // Normalize to 0-360 range
            if (angle < 0) angle += 360f;

            // Отладочная информация для проверки правильности расчета углов
            //Console.WriteLine($"Direction from ({from.Position.X:F1},{from.Position.Z:F1}) to ({to.Position.X:F1},{to.Position.Z:F1}): {angle:F1}°");

            return angle;
        }

        /// <summary>
        /// Calculate angle difference between agent's current direction and direction to target
        /// </summary>
        public float GetAngleDifference(Agent3D from, Agent3D to)
        {
            float targetAngle = GetDirectionToTarget(from, to);

            // Нормализуем текущий угол агента
            float currentAngle = from.Direction;
            while (currentAngle < 0) currentAngle += 360f;
            while (currentAngle >= 360f) currentAngle -= 360f;

            float angleDiff = Math.Abs(currentAngle - targetAngle);

            // Ensure we get the smallest angle
            if (angleDiff > 180f) angleDiff = 360f - angleDiff;

            // Отладочная информация
            //Console.WriteLine($"Agent at direction {currentAngle:F1}°, target at {targetAngle:F1}°, diff: {angleDiff:F1}°");

            return angleDiff;
        }

        /// <summary>
        /// Get distance between agents
        /// </summary>
        public float GetDistance()
        {
            return Vector3.Distance(_seeker.Position, _hider.Position);
        }

        public void ApplyAction(Agent3D agent, int action)
        {
            switch (action)
            {
                case 0:
                    agent.Rotate(-30f);
                    break; // Поворот влево
                case 1:
                    agent.Rotate(+30f);
                    break; // Поворот вправо
                case 2:
                    // Движение вперед обрабатывается отдельно
                    break;
            }
        }
    }
}