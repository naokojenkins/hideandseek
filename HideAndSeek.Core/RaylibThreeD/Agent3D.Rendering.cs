using System;
using System.Numerics;
using System.Collections.Generic;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Rendering and visualization routines for Agent3D (models, cones, gaze)
    public partial class Agent3D
    {
        // Animation state (seeker only)
        private Vector3 _prevDrawPos;
        private int _animFrame;
        private int _animIndex;
        private float _animAcc;
        // Smoothed render yaw (deg)
        private float _renderYawDeg;
        private float _prevLogicYawDeg;

        public void Draw()
        {
            if (IsSeeker)
            {
                // Try to draw GLTF seeker model (Bunny) with correct yaw; fallback to capsule
                try
                {
                    AgentModelCache.Init();
                    if (AgentModelCache.IsReady && AgentModelCache.SeekerModel.HasValue)
                    {
                        var model = AgentModelCache.SeekerModel.Value;
                        float targetYaw = NormalizeAngle(Direction + AgentModelCache.SeekerBaseYawOffsetDeg);
                        // Smoothly approach target yaw with capped angular velocity (no snapping/deadzones to avoid jitter)
                        float dtYaw = 0.016f; try { dtYaw = MathF.Max(0.0001f, Raylib.GetFrameTime()); } catch { }
                        _prevLogicYawDeg = Direction;
                        // signed smallest diff in [-180, 180]
                        float signedDiff = targetYaw - _renderYawDeg;
                        if (signedDiff > 180f) signedDiff -= 360f;
                        if (signedDiff < -180f) signedDiff += 360f;
                        float maxYawSpeedDegPerSec = 360f; // moderate smoothing
                        float maxDelta = maxYawSpeedDegPerSec * dtYaw;
                        float applied = MathF.Abs(signedDiff) <= maxDelta ? signedDiff : MathF.Sign(signedDiff) * maxDelta;
                        _renderYawDeg = NormalizeAngle(_renderYawDeg + applied);
                        float yaw = _renderYawDeg;
                        var pos = Position + new Vector3(0, AgentModelCache.SeekerYOffset, 0);
                        var scale = AgentModelCache.SeekerScale;

                        // Decide animation based on movement
                        bool hasAnims = AgentModelCache.SeekerAnimations != null && AgentModelCache.SeekerAnimations.Length > 0;
                        if (hasAnims)
                        {
                            float dt = 0.016f;
                            try { dt = MathF.Max(0.0001f, Raylib.GetFrameTime()); } catch { }
                            float movedDist = Vector3.Distance(Position, _prevDrawPos);
                            bool moving = movedDist > dt * 0.05f; // tiny threshold
                            int targetIndex;
                            // If seeker currently sees any target, switch to alert/Jump animation
                            if (IsSeeker && IsSeeingTarget)
                            {
                                targetIndex = AgentModelCache.SeekerAlertAnimIndex;
                            }
                            else
                            {
                                targetIndex = moving ? AgentModelCache.SeekerWalkAnimIndex : AgentModelCache.SeekerIdleAnimIndex;
                            }
                            if (targetIndex != _animIndex)
                            {
                                _animIndex = targetIndex;
                                _animFrame = 0;
                                _animAcc = 0f;
                            }

                            var anims = AgentModelCache.SeekerAnimations!;
                            var anim = anims[Math.Clamp(_animIndex, 0, anims.Length - 1)];
                            int frames = Math.Max(1, (int)anim.FrameCount);
                            float fps = moving ? 24f : 16f;
                            _animAcc += dt * fps;
                            while (_animAcc >= 1f)
                            {
                                _animAcc -= 1f;
                                _animFrame = (_animFrame + 1) % frames;
                            }
                            try
                            {
                                Raylib.UpdateModelAnimation(model, anim, _animFrame);
                            }
                            catch { /* ignore update errors */ }
                        }

                        // Note: Raylib's positive rotation around +Y appears mirrored vs our yaw convention, use -yaw to align model with gaze/movement
                        Raylib.DrawModelEx(model, pos, Vector3.UnitY, -yaw, scale, Color.White);
                        _prevDrawPos = Position;

                        // Vision cone for seeker if enabled
                        if (ShowVisionCones && _world != null)
                        {
                            var coneCol = IsSeeingTarget ? new Color(255, 255, 0, 80) : new Color(0, 0, 255, 80);
                            DrawVisionCone(_world, coneCol);
                            DrawGazeLine(_world);
                        }
                        return;
                    }
                }
                catch { /* ignore and fallback */ }
            }

            // Try draw hider model
            try
            {
                AgentModelCache.Init();
                if (!IsSeeker && AgentModelCache.HiderModel.HasValue)
                {
                    var model = AgentModelCache.HiderModel.Value;
                    float targetYaw = NormalizeAngle(Direction + AgentModelCache.HiderBaseYawOffsetDeg);
                    float dtYaw = 0.016f; try { dtYaw = MathF.Max(0.0001f, Raylib.GetFrameTime()); } catch { }
                    _prevLogicYawDeg = Direction;
                    float signedDiff = targetYaw - _renderYawDeg;
                    if (signedDiff > 180f) signedDiff -= 360f;
                    if (signedDiff < -180f) signedDiff += 360f;
                    float maxYawSpeedDegPerSec = 360f;
                    float maxDelta = maxYawSpeedDegPerSec * dtYaw;
                    float applied = MathF.Abs(signedDiff) <= maxDelta ? signedDiff : MathF.Sign(signedDiff) * maxDelta;
                    _renderYawDeg = NormalizeAngle(_renderYawDeg + applied);
                    float yaw = _renderYawDeg;
                    var pos = Position + new Vector3(0, AgentModelCache.HiderYOffset, 0);
                    var scale = AgentModelCache.HiderScale;

                    Raylib.DrawModelEx(model, pos, Vector3.UnitY, -yaw, scale, Color.White);

                    if (ShowVisionCones && _world != null)
                    {
                        DrawVisionCone(_world, new Color(255, 165, 0, 80));
                        DrawGazeLine(_world);
                    }
                    return;
                }
            }
            catch { /* ignore and fallback */ }

            // Fallback primitive
            Raylib.DrawCapsule(
                Position,
                Position + new Vector3(0, 1.5f, 0),
                AgentRadius, 8, 8, Color
            );

            if (ShowVisionCones && _world != null)
            {
                DrawVisionCone(_world, new Color(0, 255, 0, 80));
                DrawGazeLine(_world);
            }
        }

        private float GetVisualYawDeg()
        {
            // Use smoothed render yaw for agents with models; otherwise, logical direction
            if (IsSeeker) return _renderYawDeg;
            return AgentModelCache.HiderModel.HasValue ? _renderYawDeg : Direction;
        }

        private float GetGazeYawDeg()
        {
            if (IsSeeker)
                return NormalizeAngle(_renderYawDeg + AgentModelCache.SeekerGazeYawOffsetDeg);
            if (AgentModelCache.HiderModel.HasValue)
                return NormalizeAngle(_renderYawDeg + AgentModelCache.HiderGazeYawOffsetDeg);
            return Direction;
        }

        public void DrawVisionCone(World3D world, Color? visionColor = null)
        {
            Color coneColor = visionColor ?? new Color(255, 255, 0, 80);
            int segments = 60;
            float yaw = GetGazeYawDeg();
            float startAngle = yaw - VisionAngle / 2f;
            float endAngle = yaw + VisionAngle / 2f;
            // Gaze origin: offset to approximate eyes: Y up and a bit forward
            Vector3 forward = new Vector3(MathF.Cos(yaw * MathF.PI / 180f), 0, MathF.Sin(yaw * MathF.PI / 180f));
            Vector3 basePos = Position + new Vector3(0, IsSeeker ? AgentModelCache.SeekerEyeHeight : (AgentModelCache.HiderModel.HasValue ? AgentModelCache.HiderEyeHeight : 0.05f), 0);
            if (IsSeeker)
                basePos += forward * AgentModelCache.SeekerGazeForwardOffset;
            else if (AgentModelCache.HiderModel.HasValue)
                basePos += forward * AgentModelCache.HiderGazeForwardOffset;
            Vector3 agentPos = basePos;

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


        public void DrawGazeLine(World3D world, Color? lineColor = null, float lineYOffset = 0.05f)
        {
            // Рисуем черной линией до столкновения со стеной (или до радиуса зрения)
            Color color = Color.Black;
            float yaw = GetGazeYawDeg();
            float radians = yaw * MathF.PI / 180f;
            Vector3 dir = new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians));

            Vector3 origin = Position + new Vector3(0, IsSeeker ? AgentModelCache.SeekerEyeHeight : lineYOffset, 0);
            if (IsSeeker)
                origin += dir * AgentModelCache.SeekerGazeForwardOffset;
            Vector3 rayEnd = GetPreciseRayEndPoint(Position, dir, VisionRadius, world);
            Vector3 end = rayEnd + new Vector3(0, IsSeeker ? AgentModelCache.SeekerEyeHeight : lineYOffset, 0);

            Raylib.DrawLine3D(origin, end, color);
        }
    }
}