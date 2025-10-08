using System;
using System.Numerics;
using System.Collections.Generic;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Вспомогательные функции восприятия/геометрии
    public partial class Simulation3D
    {
        private Agent3D GetNearestOpponent(Agent3D agent, List<Agent3D> opponents)
        {
            if (opponents == null || opponents.Count == 0) return agent.IsSeeker ? Hider : Seeker;
            Agent3D best = opponents[0];
            float bestD = Vector3.Distance(agent.Position, best.Position);
            for (int i = 1; i < opponents.Count; i++)
            {
                float d = Vector3.Distance(agent.Position, opponents[i].Position);
                if (d < bestD) { bestD = d; best = opponents[i]; }
            }
            return best;
        }

        // Цель находится в секторе и радиусе наблюдателя, но не видна (окклюзия препятствием)
        private bool IsOccludedByWall(Agent3D observer, Agent3D target)
        {
            float maxDist = observer.VisionRadius;
            float halfFov = observer.VisionAngle * 0.5f;

            Vector3 toTarget = target.Position - observer.Position;
            float dist = toTarget.Length();
            if (dist > maxDist) return false;
            if (dist < 1e-5f) return false;

            float yawRad = observer.Direction * MathF.PI / 180f;
            var forward = new Vector3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            Vector3 dir = Vector3.Normalize(toTarget);
            float dot = Math.Clamp(Vector3.Dot(forward, dir), -1f, 1f);
            float angleDeg = MathF.Acos(dot) * (180f / MathF.PI);
            if (angleDeg > halfFov) return false;

            return !observer.CanSee(target, World);
        }

        private bool AnyHiderVisible()
        {
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };
            foreach (var h in hiders)
                foreach (var s in seekers)
                    if (s.CanSee(h, World)) return true;
            return false;
        }
    }
}
