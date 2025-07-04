using System;
using System.Numerics;
using Raylib_cs;

namespace ToolUse.Core.RaylibThreeD
{
    public class Renderer3D
    {
        private Camera3D _camera;
        public Camera3D Camera => _camera;
        public Vector3 CameraTarget { get; set; }
        public float CameraDistance { get; set; } = 15.0f;
        public float CameraHeight { get; set; } = 10.0f;
        private bool _followAgent = true;
        public bool FollowAgent
        {
            get => _followAgent;
            set => _followAgent = value;
        }

        public Renderer3D()
        {
            // Initialize with default values, will be updated when InitializeCamera is called
        }

        public Renderer3D(int worldSize)
        {
            CameraTarget = new Vector3(worldSize / 2f, 0, worldSize / 2f);

            _camera = new Camera3D
            {
                Position = new Vector3(CameraTarget.X, CameraHeight, CameraTarget.Z - CameraDistance),
                Target = CameraTarget,
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };
        }

        public Camera3D InitializeCamera()
        {
            Camera3D camera = new Camera3D();

            camera.Position = new Vector3(10.0f, 10.0f, 10.0f);
            camera.Target = new Vector3(0.0f, 0.0f, 0.0f);
            camera.Up = new Vector3(0.0f, 1.0f, 0.0f); // Y-up
            camera.FovY = 45.0f;
            camera.Projection = CameraProjection.Perspective;

            _camera = camera;
            return camera;
        }

        public void UpdateCamera(Agent3D followAgent = null)
        {
            if (_followAgent && followAgent != null)
            {
                // Follow the agent
                CameraTarget = followAgent.Position;
                _camera.Target = CameraTarget;
                _camera.Position = new Vector3(
                    CameraTarget.X - 8f,
                    CameraHeight,
                    CameraTarget.Z - 8f
                );
            }
            else
            {
                HandleFreeCamera();
            }
        }

        private void HandleFreeCamera()
        {
            float speed = 0.3f;

            // Move forward
            if (Raylib.IsKeyDown(KeyboardKey.W))
            {
                Vector3 forward = Vector3.Normalize(_camera.Target - _camera.Position);
                _camera.Position += forward * speed;
                _camera.Target += forward * speed;
            }
            // Move backward
            if (Raylib.IsKeyDown(KeyboardKey.S))
            {
                Vector3 backward = Vector3.Normalize(_camera.Position - _camera.Target);
                _camera.Position += backward * speed;
                _camera.Target += backward * speed;
            }
            // Move left
            if (Raylib.IsKeyDown(KeyboardKey.A))
            {
                Vector3 left = Vector3.Normalize(Vector3.Cross(_camera.Up, _camera.Target - _camera.Position));
                _camera.Position += left * speed;
                _camera.Target += left * speed;
            }
            // Move right
            if (Raylib.IsKeyDown(KeyboardKey.D))
            {
                Vector3 right = Vector3.Normalize(Vector3.Cross(_camera.Target - _camera.Position, _camera.Up));
                _camera.Position += right * speed;
                _camera.Target += right * speed;
            }
            // Move up
            if (Raylib.IsKeyDown(KeyboardKey.Q))
            {
                _camera.Position += Vector3.UnitY * speed;
                _camera.Target += Vector3.UnitY * speed;
            }
            // Move down
            if (Raylib.IsKeyDown(KeyboardKey.E))
            {
                _camera.Position -= Vector3.UnitY * speed;
                _camera.Target -= Vector3.UnitY * speed;
            }
        }

        public void DrawSky()
        {
            Rlgl.DisableDepthTest();
            Raylib.DrawRectangleGradientV(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), 
                                        new Raylib_cs.Color(135, 206, 235, 255), // Light blue
                                        new Raylib_cs.Color(25, 25, 112, 255));  // Dark blue
            Rlgl.EnableDepthTest();
        }

        public void DrawFloor(int size, float cellSize = 1.0f)
        {
            // Draw large plane as base
            float floorSize = size * cellSize;
            Raylib.DrawPlane(new Vector3(size/2, -0.5f, size/2), new Vector2(floorSize, floorSize), Raylib_cs.Color.DarkGreen);

            // Draw grid above
            for (int x = 0; x <= size; x++)
            {
                Raylib.DrawLine3D(
                    new Vector3(x, 0, 0),
                    new Vector3(x, 0, size),
                    Raylib_cs.Color.LightGray
                );
            }

            for (int z = 0; z <= size; z++)
            {
                Raylib.DrawLine3D(
                    new Vector3(0, 0, z),
                    new Vector3(size, 0, z),
                    Raylib_cs.Color.LightGray
                );
            }
        }

        public void DrawDebugInfo(Camera3D camera, int fps)
        {
            Raylib.DrawFPS(10, 10);

            Raylib.DrawText(
                $"Camera: ({camera.Position.X:F1}, {camera.Position.Y:F1}, {camera.Position.Z:F1})", 
                10, 30, 10, Raylib_cs.Color.White);
        }
    }
}