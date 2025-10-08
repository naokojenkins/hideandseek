using System;
using System.Numerics;
using System.Collections.Generic;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Санитизация сцены и валидация чисел/позиции
    public partial class Simulation3D
    {
        private void CheckNaN(float[] arr, string tag)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (float.IsNaN(arr[i]) || float.IsInfinity(arr[i]))
                {
                    try { LogNumericIssue(tag, $"Array<float> length={arr.Length}, badIndex={i}, value={arr[i]}"); } catch { }
                    throw new Exception($"[NaN/Inf] {tag}: Index {i} value {arr[i]}");
                }
            }
        }
        private void CheckNaN(Vector3 v, string tag)
        {
            if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z))
            {
                try { LogNumericIssue(tag, $"Vector3 value=({v.X}, {v.Y}, {v.Z})"); } catch { }
                throw new Exception($"[NaN/Inf] {tag}: {v}");
            }
        }

        // Вспомогательная проверка без броска исключения
        private static bool IsFiniteVec(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                     float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        // Санитизация сцены перед отрисовкой: камера и позиции агентов
        private void SanitizeScene()
        {
            // Камера
            if (!IsFiniteVec(_camera.Position) || !IsFiniteVec(_camera.Target) || !IsFiniteVec(_camera.Up) ||
                float.IsNaN(_camera.FovY) || float.IsInfinity(_camera.FovY))
            {
                InitializeCamera();
            }

            // Агенты: используем актуальные списки, если они заданы
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            void FixAgent(Agent3D a)
            {
                var p = a.Position;
                bool badPos = float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                              float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z);
                bool validNow = !badPos && IsPositionValidForWorld(p, a.AgentRadius);
                if (!validNow)
                {
                    // 1) Try restore last known good position
                    if (_lastValidPos.TryGetValue(a, out var last) && IsFiniteVec(last) && IsPositionValidForWorld(last, a.AgentRadius))
                    {
                        a.Position = last;
                    }
                    else
                    {
                        // 2) Try to find a nearby valid spot around current position (small spiral search)
                        Vector3 basePos = p;
                        bool placed = false;
                        float[] radii = new float[] { 0.2f, 0.4f, 0.8f, 1.2f, 1.6f, 2.0f };
                        foreach (var r in radii)
                        {
                            int steps = Math.Max(8, (int)(r * 16));
                            for (int i = 0; i < steps; i++)
                            {
                                float ang = (2f * MathF.PI) * (i / (float)steps);
                                var cand = new Vector3(basePos.X + MathF.Cos(ang) * r, basePos.Y, basePos.Z + MathF.Sin(ang) * r);
                                if (IsPositionValidForWorld(cand, a.AgentRadius))
                                {
                                    a.Position = cand;
                                    placed = true;
                                    break;
                                }
                            }
                            if (placed) break;
                        }
                        if (!placed)
                        {
                            // 3) As a last resort, fallback to random valid position (rare)
                            a.Position = World.GetRandomValidAgentPosition(a.AgentRadius, 0f);
                        }
                    }
                }
                if (float.IsNaN(a.Direction) || float.IsInfinity(a.Direction))
                {
                    a.Direction = 0f;
                }

                // Update last valid after potential correction
                if (IsFiniteVec(a.Position) && IsPositionValidForWorld(a.Position, a.AgentRadius))
                {
                    _lastValidPos[a] = a.Position;
                }
            }

            foreach (var s in seekers) FixAgent(s);
            foreach (var h in hiders)  FixAgent(h);
        }

        // Проверка валидности позиции агента относительно текущего мира Simulation3D.World
        private bool IsPositionValidForWorld(Vector3 pos, float radius)
        {
            if (World == null) return false;
            int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                float ang = 2 * MathF.PI * i / steps;
                float checkX = pos.X + MathF.Cos(ang) * radius * 0.9f;
                float checkZ = pos.Z + MathF.Sin(ang) * radius * 0.9f;

                int gx = Math.Clamp((int)MathF.Floor(checkX), 0, World.Size - 1);
                int gz = Math.Clamp((int)MathF.Floor(checkZ), 0, World.Size - 1);

                if (!World.IsInside(gx, gz) || World.IsBlocked(gx, gz))
                    return false;
            }
            return true;
        }

        // Remember last known valid positions for all current agents
        private void RememberAllAgentsValidPositions()
        {
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };
            foreach (var a in seekers)
                if (IsFiniteVec(a.Position) && IsPositionValidForWorld(a.Position, a.AgentRadius))
                    _lastValidPos[a] = a.Position;
            foreach (var a in hiders)
                if (IsFiniteVec(a.Position) && IsPositionValidForWorld(a.Position, a.AgentRadius))
                    _lastValidPos[a] = a.Position;
        }

        // Гарантирует, что агент стоит на валидной позиции текущего мира; при необходимости переносит
        private void EnsureAgentOnValidCell(Agent3D agent)
        {
            if (!IsPositionValidForWorld(agent.Position, agent.AgentRadius))
            {
                agent.Position = World.GetRandomValidAgentPosition(agent.AgentRadius, 0f);
            }
        }
    }
}
