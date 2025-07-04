using System;
using System.Numerics;
using Raylib_cs;
using ToolUse.Core.RL;

namespace ToolUse.Core.RaylibThreeD
{
    public class Simulation3D
    {
        public World3D World { get; }
        public Agent3D Seeker { get; set; }
        public Agent3D Hider { get; set; }

        private readonly QAgent _seekerAgent;
        private readonly QAgent _hiderAgent;
        private readonly SimAdapter3D _adapter; // 1. БЫЛО RLAdapter3D — исправлено на SimAdapter3D

        private Camera3D _camera;
        private bool _followAgent = true;
        private bool _showVisionCones = true;
        private bool _showGrid = true;

        public int Session { get; private set; } = 1;
        public float Timer { get; private set; } = 0f;
        public float SeekerScore { get; private set; } = 0f;
        public float HiderScore { get; private set; } = 0f;
        public bool IsHiderVisible { get; private set; } = false;
        private bool _isHiderCaught = false;
        private int _caughtFrames = 0;

        public Simulation3D(int worldSize = 40)
        {
            World = new World3D(worldSize);
            World.GenerateStaticGrid();

            Seeker = new Agent3D(World.GetRandomEmptyPosition(1.0f), true, Raylib.GetRandomValue(0, 359));
            Vector3 hiderPos;
            do
            {
                hiderPos = World.GetRandomEmptyPosition(1.0f);
            } while (Vector3.Distance(Seeker.Position, hiderPos) < 15f);

            Hider = new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));

            var seekerQTable = new QTable();
            var hiderQTable = new QTable();
            _seekerAgent = new QAgent(seekerQTable, 0.1f, 0.1f, 0.9f);
            _hiderAgent = new QAgent(hiderQTable, 0.1f, 0.1f, 0.9f);

            // 2. ПРАВИЛЬНЫЙ ПОРЯДОК АРГУМЕНТОВ
            _adapter = new SimAdapter3D(World, Seeker, Hider);

            InitializeCamera();
        }

        private void InitializeCamera()
        {
            _camera = new Camera3D
            {
                Position = new Vector3(World.Size / 2f, 25f, World.Size / 2f),
                Target = new Vector3(World.Size / 2f, 0f, World.Size / 2f),
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };
        }

        public void Update(float deltaTime)
        {
            Timer += deltaTime;
            UpdateRLAgents(deltaTime);
            UpdateCamera();

            IsHiderVisible = Seeker.CanSee(Hider, World);

            // 3. Корректное детектирование "пойман"
            if (IsHiderVisible)
            {
                if (++_caughtFrames >= Raylib.GetFPS() * 5)
                {
                    _isHiderCaught = true;
                }
            }
            else
            {
                _caughtFrames = 0;
            }

            UpdateScores(deltaTime);

            if (_isHiderCaught || Timer > 60f)
            {
                Restart();
            }
        }

        private void UpdateRLAgents(float deltaTime)
        {
            var seekerState = _adapter.GetSeekerState();
            int seekerAction = _seekerAgent.ChooseAction(seekerState);
            _adapter.ApplyAction(Seeker, seekerAction);

            var hiderState = _adapter.GetHiderState();
            int hiderAction = _hiderAgent.ChooseAction(hiderState);
            _adapter.ApplyAction(Hider, hiderAction);

            // 4. Метод MoveWithCollisionAvoidance требует правильные параметры
            if (seekerAction == 2) Seeker.MoveWithCollisionAvoidance(World, deltaTime, Hider);
            if (hiderAction == 2) Hider.MoveWithCollisionAvoidance(World, deltaTime, Seeker);

            float seekerReward = IsHiderVisible ? 1.0f : -0.1f;
            float hiderReward = IsHiderVisible ? -1.0f : 0.1f;
            _seekerAgent.Learn(seekerState, seekerAction, seekerReward, _adapter.GetSeekerState());
            _hiderAgent.Learn(hiderState, hiderAction, hiderReward, _adapter.GetHiderState());
        }

        private void UpdateCamera()
        {
            if (_followAgent)
            {
                _camera.Target = Seeker.Position;
                _camera.Position = Seeker.Position + new Vector3(-10f, 8f, -10f);
            }
            else
            {
                Raylib.UpdateCamera(ref _camera, CameraMode.Free);
            }
        }

        private void UpdateScores(float deltaTime)
        {
            if (IsHiderVisible) SeekerScore += deltaTime;
            else HiderScore += deltaTime;
        }

        public void Restart()
        {
            Session++;
            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;

            World.GenerateStaticGrid();
            Seeker.Position = World.GetRandomEmptyPosition(1.0f);
            Seeker.Direction = Raylib.GetRandomValue(0, 359);

            Vector3 hiderPosition;
            do
            {
                hiderPosition = World.GetRandomEmptyPosition(1.0f);
            } while (Vector3.Distance(Seeker.Position, hiderPosition) < 15f);

            Hider.Position = hiderPosition;
            Hider.Direction = Raylib.GetRandomValue(0, 359);
        }

        public void Draw()
        {
            Raylib.BeginMode3D(_camera);
            {
                World.Draw(true);
                if (_showGrid) World.DrawGrid();
                Seeker.Draw();
                Hider.Draw();
                if (_showVisionCones)
                {
                    Seeker.DrawVisionCone(World, new Color(255, 0, 0, 80));
                    Hider.DrawVisionCone(World, new Color(0, 255, 0, 80));
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            Raylib.DrawRectangle(5, 5, 250, 145, Raylib.ColorAlpha(Color.Black, 0.6f));
            int y = 10;
            Raylib.DrawText($"Session: {Session}", 10, y, 20, Color.White);
            y += 25;
            Raylib.DrawText($"Time: {Timer:F1}s", 10, y, 20, Color.White);
            y += 25;
            Raylib.DrawText($"Seeker Score: {SeekerScore:F1}", 10, y, 20, Color.Red);
            y += 25;
            Raylib.DrawText($"Hider Score: {HiderScore:F1}", 10, y, 20, Color.Green);
            y += 25;
            Raylib.DrawText($"Visible: {(IsHiderVisible ? "YES" : "NO")}", 10, y, 20,
                IsHiderVisible ? Color.Gold : Color.LightGray);
            if (_isHiderCaught)
            {
                string caughtText = "CAUGHT!";
                int textWidth = Raylib.MeasureText(caughtText, 40);
                Raylib.DrawText(caughtText, (Raylib.GetScreenWidth() - textWidth) / 2,
                    Raylib.GetScreenHeight() / 2 - 20, 40, Color.Gold);
            }
        }

        public void HandleInput()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F))
            {
                _followAgent = !_followAgent;
                if (!_followAgent) Raylib.DisableCursor();
                else Raylib.EnableCursor();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.V)) _showVisionCones = !_showVisionCones;
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _showGrid = !_showGrid;
            if (Raylib.IsKeyPressed(KeyboardKey.R)) Restart();
        }

        public void Reset(Agent3D seeker, Agent3D hider)
        {
            this.Seeker = seeker;
            this.Hider = hider;
            // Дополнительно сбросить состояние мира, счётчики, и т.п., если требуется
        }
    }
}