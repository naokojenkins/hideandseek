namespace ToolUse.Core
{
    using System;
    using System.Numerics;

    public class Agent
    {
        /* ───── публичные поля-свойства ───── */
        public int   X { get; private set; }
        public int   Y { get; private set; }

        public float Angle        { get; private set; } = 0f;   // 0° = вправо, 90° = вниз
        public float VisionRadius { get; set; }   = 9f;
        public float VisionAngle  { get; set; }   = 90f;

        public bool  IsSeeker { get; }

        /* ───── ctor ───── */
        public Agent(int x, int y, bool isSeeker, float angle = 0f)
        {
            X = x;  Y = y;
            Angle    = angle;
            IsSeeker = isSeeker;
        }

        /* ───── поворот + движение ───── */
        public void Rotate(float deltaDeg) => Angle = (Angle + deltaDeg + 360f) % 360f;

        public void MoveForward(World world)
        {
            float rad = Angle * MathF.PI / 180f;
            int dx = (int)MathF.Round(MathF.Cos(rad));
            int dy = (int)MathF.Round(MathF.Sin(rad));

            int nx = X + dx, ny = Y + dy;
            if (!world.IsBlocked(nx, ny))
            {
                X = nx; Y = ny;
            }
        }

        /* ───── вспом. методы ───── */
        public float GetFacingAngle() => Angle;

        public bool CanSee(Agent target, World world)
        {
            // геометрия
            int dx = target.X - X, dy = target.Y - Y;
            if (dx * dx + dy * dy > VisionRadius * VisionRadius) return false;

            // в конусе?
            Vector2 fwd = new(MathF.Cos(Angle * MathF.PI / 180f),
                MathF.Sin(Angle * MathF.PI / 180f));
            Vector2 toT = Vector2.Normalize(new Vector2(dx, dy));
            float dot   = Vector2.Dot(fwd, toT);
            float th    = MathF.Cos(VisionAngle * MathF.PI / 360f);
            if (dot < th) return false;

            return world.HasLineOfSight(this, target);
        }
    }
}