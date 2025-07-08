using System;
using System.Numerics;
using Raylib_cs;
using ToolUse.Core.RL;
using ToolUse.Core.Config;

namespace ToolUse.Core.RaylibThreeD
{
    public class Simulation3D
    {
        public World3D World { get; }
        public Agent3D Seeker { get; set; }
        public Agent3D Hider { get; set; }
        public bool IsHiderCaught => _isHiderCaught;
        private float sessionDurationSeconds;

        public void SetSessionDuration(float seconds)
        {
            sessionDurationSeconds = seconds;
        }

        public GameConfig Config { get; private set; }

        // ✅ Добавлено событие
        public event System.Action OnSessionCompleted;

        private QAgent _seekerAgent;
        private QAgent _hiderAgent;
        private readonly SimAdapter3D _adapter;

        private Camera3D _camera;
        private bool _followAgent = true;
        private bool _showVisionCones = true;
        private bool _showGrid = true;

        public int Session { get; private set; } = 1;
        public float Timer { get; private set; } = 0f;
        public float SeekerScore { get; private set; } = 0f;
        public float HiderScore { get; private set; } = 0f;
        public float ExplorationScore { get; private set; } = 0f;

        private bool _isHiderVisible = false;
        private float _lastVisibilityCheck = 0f;
        private const float _visibilityCheckInterval = 0.05f;

        public bool IsHiderVisible
        {
            get => _isHiderVisible;
            private set => _isHiderVisible = value;
        }

        private bool _isHiderCaught = false;
        private int _caughtFrames = 0;

        public Simulation3D(
            int worldSize,
            Agent3D seeker,
            Agent3D hider,
            QTable seekerQ,
            QTable hiderQ,
            string configPath = "game_config.json")
        {
            Config = GameConfig.Load(configPath);
            World = new World3D(worldSize);
            World.GenerateStaticGrid();

            Seeker = seeker ?? new Agent3D(World.GetRandomEmptyPosition(1.0f), true, Raylib.GetRandomValue(0, 359));
            Hider = hider ?? GenerateHiderPosition(Seeker.Position);

            _seekerAgent = new QAgent(seekerQ, 0.1f, 0.1f, 0.9f);
            _hiderAgent = new QAgent(hiderQ, 0.1f, 0.1f, 0.9f);

            _adapter = new SimAdapter3D(World, Seeker, Hider);
            InitializeCamera();
        }

        private Agent3D GenerateHiderPosition(Vector3 seekerPos)
        {
            Vector3 hiderPos;
            do
            {
                hiderPos = World.GetRandomEmptyPosition(1.0f);
            } while (Vector3.Distance(seekerPos, hiderPos) < 15f);

            return new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));
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

            _lastVisibilityCheck += deltaTime;
            if (_lastVisibilityCheck >= _visibilityCheckInterval)
            {
                IsHiderVisible = Seeker.CanSee(Hider, World);
                _lastVisibilityCheck = 0f;
            }

            if (IsHiderVisible)
            {
                if (++_caughtFrames >= Config.FramesForCatch)
                {
                    _isHiderCaught = true;
                }
            }
            else
            {
                _caughtFrames = 0;
            }

            UpdateScores(deltaTime);

            if (_isHiderCaught || Timer > Config.SessionDurationSeconds)
            {
                Restart();
            }
        }

        private void UpdateRLAgents(float deltaTime)
        {
            var seekerState = _adapter.GetSeekerState();
            int seekerAction = _seekerAgent.ChooseAction(seekerState);

            var hiderState = _adapter.GetHiderState();
            int hiderAction = _hiderAgent.ChooseAction(hiderState);

            Vector3 seekerOldPos = Seeker.Position;
            Vector3 hiderOldPos = Hider.Position;
            int exploredBefore = Seeker.GetExploredCount();

            _adapter.ApplyAction(Seeker, seekerAction);
            _adapter.ApplyAction(Hider, hiderAction);

            if (seekerAction == 2) Seeker.MoveWithCollisionAvoidance(World, deltaTime, Hider);
            if (hiderAction == 2) Hider.MoveWithCollisionAvoidance(World, deltaTime, Seeker);

            int exploredAfter = Seeker.GetExploredCount();
            int newCellsExplored = exploredAfter - exploredBefore;

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);

            float seekerReward = IsHiderVisible ? Config.Seeker.RewardWhenHiderVisible : Config.Seeker.RewardWhenHiderHidden;
            float hiderReward = IsHiderVisible ? Config.Hider.RewardWhenVisible : Config.Hider.RewardWhenHidden;

            if (newCellsExplored > 0)
            {
                float explorationBonus = newCellsExplored * Config.Seeker.ExplorationBonusPerCell;
                seekerReward += explorationBonus;
                ExplorationScore += explorationBonus;
                SeekerScore += explorationBonus * Config.Seeker.ExplorationScoreMultiplier;
            }

            if (Config.Seeker.ProximityRewardEnabled && distance <= Config.Seeker.MaxProximityDistance)
            {
                float proximityReward = (Config.Seeker.MaxProximityDistance - distance) / Config.Seeker.MaxProximityDistance;
                proximityReward *= Config.Seeker.ProximityRewardMultiplier * deltaTime;
                seekerReward += proximityReward;
                SeekerScore += proximityReward;
            }

            if (Config.Seeker.MovementRewardEnabled)
            {
                float seekerMovement = Vector3.Distance(seekerOldPos, Seeker.Position);
                if (seekerMovement > 0.1f)
                {
                    float movementReward = Config.Seeker.MovementRewardPerSecond * deltaTime;
                    seekerReward += movementReward;
                    SeekerScore += movementReward;
                }
                else if (Config.Seeker.IdlePenaltyEnabled)
                {
                    float idlePenalty = Config.Seeker.IdlePenaltyPerSecond * deltaTime;
                    seekerReward += idlePenalty;
                    SeekerScore += idlePenalty;
                }
            }

            if (Config.Hider.DistanceRewardEnabled && distance >= Config.Hider.MinSafeDistance)
            {
                float distanceReward = (distance - Config.Hider.MinSafeDistance) / Config.Hider.MinSafeDistance;
                distanceReward = Math.Min(distanceReward, 1.0f);
                distanceReward *= Config.Hider.DistanceRewardMultiplier * deltaTime;
                hiderReward += distanceReward;
                HiderScore += distanceReward;
            }

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
            if (IsHiderVisible)
            {
                SeekerScore += Config.Seeker.PointsPerSecondWhenHiderVisible * deltaTime;
                HiderScore += Config.Hider.PointsPerSecondWhenVisible * deltaTime;
            }
            else
            {
                HiderScore += Config.Hider.PointsPerSecondWhenHidden * deltaTime;
                SeekerScore += Config.Seeker.PointsPerSecondWhenHiderHidden * deltaTime;
            }

            ExplorationScore += 0f;
        }

        public void Restart()
        {
            Session++;
            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
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

            Seeker.ResetExploration();

            // ✅ Вызываем событие завершения сессии
            OnSessionCompleted?.Invoke();
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
                    Color seekerConeColor = IsHiderVisible ? new Color(255, 255, 0, 100) : new Color(0, 0, 255, 80);
                    Seeker.DrawVisionCone(World, seekerConeColor);
                    Hider.DrawVisionCone(World, new Color(0, 255, 0, 80));
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            Raylib.DrawRectangle(5, 5, 320, 320, Raylib.ColorAlpha(Color.Black, 0.7f));

            int y = 10;
            Raylib.DrawText($"Session: {Session}", 10, y, 20, Color.White);
            y += 25;

            Color timeColor = Timer > (Config.SessionDurationSeconds * 0.9f) ? Color.Red : Color.White;
            Raylib.DrawText($"Time: {Timer:F1}s / {Config.SessionDurationSeconds:F0}s", 10, y, 20, timeColor);
            y += 25;

            Raylib.DrawText($"Seeker: {SeekerScore:F1}", 10, y, 20, Color.Blue);
            float seekerPercent = SeekerScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(seekerPercent / 100f, 1.0f)), 10, Color.Blue);
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, Color.White);
            y += 25;

            Raylib.DrawText($"Explored: {Seeker.GetExploredCount()}", 10, y, 20, Color.Purple);
            y += 25;

            Raylib.DrawText($"Exploration Score: {ExplorationScore:F1}", 10, y, 18, Color.Purple);
            y += 25;

            Raylib.DrawText($"Hider: {HiderScore:F1}", 10, y, 20, Color.Green);
            float hiderPercent = HiderScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(hiderPercent / 100f, 1.0f)), 10, Color.Green);
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, Color.White);
            y += 25;

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);
            Color distanceColor = distance < 5f ? Color.Red : distance < 10f ? Color.Yellow : Color.White;
            Raylib.DrawText($"Distance: {distance:F1}", 10, y, 18, distanceColor);
            y += 25;

            Color visibilityColor = IsHiderVisible ? Color.Gold : Color.Gray;
            string visibilityText = IsHiderVisible ? "VISIBLE!" : "Hidden";
            Raylib.DrawText($"Hider: {visibilityText}", 10, y, 20, visibilityColor);

            if (IsHiderVisible && (int)(Timer * 2) % 2 == 0)
            {
                Raylib.DrawRectangle(120, y, 15, 15, Color.Gold);
            }
            y += 25;

            Raylib.DrawText($"Vision Cones: {(_showVisionCones ? "ON" : "OFF")}", 10, y, 18,
                _showVisionCones ? Color.Lime : Color.Gray);
            y += 25;

            Raylib.DrawText($"Grid: {(_showGrid ? "ON" : "OFF")}", 10, y, 18,
                _showGrid ? Color.Lime : Color.Gray);
            y += 25;

            Raylib.DrawText($"Camera: {(_followAgent ? "Follow" : "Free")}", 10, y, 18,
                _followAgent ? Color.Lime : Color.Yellow);
            y += 25;

            Raylib.DrawText("=== Scoring Info ===", 10, y, 16, Color.White);
            y += 20;

            if (Config.Seeker.ProximityRewardEnabled)
            {
                Raylib.DrawText($"Proximity Bonus: {(distance <= Config.Seeker.MaxProximityDistance ? "Active" : "Inactive")}",
                    10, y, 14, distance <= Config.Seeker.MaxProximityDistance ? Color.Green : Color.Gray);
                y += 18;
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

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;

            _adapter.GetType().GetField("_seeker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_adapter, seeker);
            _adapter.GetType().GetField("_hider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_adapter, hider);

            World.GenerateStaticGrid();
            Seeker.ResetExploration();
        }
    }
}