using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
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

        public event Action? OnSessionCompleted;

        private QAgent _seekerAgent;
        private QAgent _hiderAgent;
        private readonly SimAdapter3D _adapter;

        private Camera3D _camera;
        private bool _cameraLocked = false; // теперь это "фиксирована ли камера"
        private bool _showVisionCones = true;
        private bool _showGrid = true;

        public int Session { get; private set; } = 1;
        public static int TotalSessions { get; private set; } = 0;
        public float Timer { get; private set; } = 0f;
        public float SeekerScore { get; private set; } = 0f;
        public float HiderScore { get; private set; } = 0f;
        public float ExplorationScore { get; private set; } = 0f;

        private bool _isHiderVisible = false;
        private float _lastVisibilityCheck = 0f;
        private const float _visibilityCheckInterval = 0.05f;

        private static readonly string SessionCounterFile = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "qtables",
            "total_sessions.json");

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        public bool IsHiderVisible
        {
            get => _isHiderVisible;
            private set => _isHiderVisible = value;
        }

        private bool _isHiderCaught = false;
        private int _caughtFrames = 0;

        static Simulation3D()
        {
            LoadTotalSessions();
        }

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

            if (seeker == null)
            {
                Vector3 seekerPos = World.GetRandomEmptyPosition(0f);
                int seekerX = (int)Math.Floor(seekerPos.X);
                int seekerZ = (int)Math.Floor(seekerPos.Z);
                if (World.IsBlocked(seekerX, seekerZ))
                {
                    Console.WriteLine("[WARNING] Seeker generated in blocked position, finding new position...");
                    seekerPos = World.GetRandomEmptyPosition(0f);
                }
                Seeker = new Agent3D(seekerPos, true, Raylib.GetRandomValue(0, 359));
            }
            else
            {
                Seeker = seeker;
            }

            if (hider == null)
            {
                Hider = GenerateHiderPosition(Seeker.Position);
            }
            else
            {
                Hider = hider;
            }

            _seekerAgent = new QAgent(seekerQ, 0.1f, 0.1f, 0.9f);
            _hiderAgent = new QAgent(hiderQ, 0.1f, 0.1f, 0.9f);

            _adapter = new SimAdapter3D(World, Seeker, Hider);
            InitializeCamera();
        }

        private Agent3D GenerateHiderPosition(Vector3 seekerPos)
        {
            int maxAttempts = 100;
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                Vector3 hiderPos = World.GetRandomEmptyPosition(0f);

                int x = (int)Math.Floor(hiderPos.X);
                int z = (int)Math.Floor(hiderPos.Z);

                if (World.IsInside(x, z) && !World.IsBlocked(x, z) &&
                    Vector3.Distance(seekerPos, hiderPos) >= 15f)
                {
                    return new Agent3D(hiderPos, false, Raylib.GetRandomValue(0, 359));
                }

                attempts++;
            }

            Console.WriteLine("[WARNING] Could not find hider position with required distance, using any empty position");
            Vector3 fallbackPos = World.GetRandomEmptyPosition(0f);
            return new Agent3D(fallbackPos, false, Raylib.GetRandomValue(0, 359));
        }

        private void InitializeCamera()
        {
            float center = World.Size / 2f;
            _camera = new Camera3D
            {
                Position = new Vector3(center - 16f, 18f, center - 16f), // Сбоку-сверху
                Target = new Vector3(center, 0f, center),                // Смотрим в центр поля
                Up = Vector3.UnitY,
                FovY = 60f,
                Projection = CameraProjection.Perspective
            };
            _cameraLocked = false;
            Raylib.DisableCursor(); // Свободная камера, мышь захвачена
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
            int visuallyExploredBefore = Seeker.GetVisuallyExploredCount();

            _adapter.ApplyAction(Seeker, seekerAction);
            _adapter.ApplyAction(Hider, hiderAction);

            if (seekerAction == 2) Seeker.MoveWithCollisionAvoidance(World, deltaTime, Hider);
            if (hiderAction == 2) Hider.MoveWithCollisionAvoidance(World, deltaTime, Seeker);

            // Обновляем визуальное исследование
            int newVisuallyExploredCells = Seeker.UpdateVisualExploration(World);

            int exploredAfter = Seeker.GetExploredCount();
            int visuallyExploredAfter = Seeker.GetVisuallyExploredCount();

            int newPhysicallyExploredCells = exploredAfter - exploredBefore;
            int totalNewCells = newPhysicallyExploredCells + newVisuallyExploredCells;

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);

            float seekerReward = IsHiderVisible ? Config.Seeker.RewardWhenHiderVisible : Config.Seeker.RewardWhenHiderHidden;
            float hiderReward = IsHiderVisible ? Config.Hider.RewardWhenVisible : Config.Hider.RewardWhenHidden;

            // Награда за исследование (физическое и визуальное)
            if (totalNewCells > 0)
            {
                float explorationBonus = totalNewCells * Config.Seeker.ExplorationBonusPerCell;
                seekerReward += explorationBonus;
                ExplorationScore += explorationBonus;
                SeekerScore += explorationBonus * Config.Seeker.ExplorationScoreMultiplier;

                // Дополнительная награда за физическое исследование
                if (newPhysicallyExploredCells > 0)
                {
                    float physicalBonus = newPhysicallyExploredCells * Config.Seeker.ExplorationBonusPerCell * 0.5f;
                    seekerReward += physicalBonus;
                    SeekerScore += physicalBonus;
                }
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
            if (!_cameraLocked)
            {
                Raylib.UpdateCamera(ref _camera, CameraMode.Free);
            }
            // если _cameraLocked — ничего не делаем, камера остаётся в текущем положении
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
            TotalSessions++;

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;

            World.GenerateStaticGrid();

            Vector3 seekerPos = World.GetRandomEmptyPosition(0f);

            int seekerX = (int)Math.Floor(seekerPos.X);
            int seekerZ = (int)Math.Floor(seekerPos.Z);
            if (World.IsBlocked(seekerX, seekerZ))
            {
                Console.WriteLine("[WARNING] Seeker generated in blocked position, finding new position...");
                seekerPos = World.GetRandomEmptyPosition(0f);
            }

            Seeker.Position = seekerPos;
            Seeker.Direction = Raylib.GetRandomValue(0, 359);

            Vector3 hiderPosition = World.GetRandomEmptyPosition(0f);
            int attempts = 0;
            while (attempts < 50 && Vector3.Distance(seekerPos, hiderPosition) < 15f)
            {
                hiderPosition = World.GetRandomEmptyPosition(0f);
                attempts++;
            }

            int hiderX = (int)Math.Floor(hiderPosition.X);
            int hiderZ = (int)Math.Floor(hiderPosition.Z);
            if (World.IsBlocked(hiderX, hiderZ))
            {
                Console.WriteLine("[WARNING] Hider generated in blocked position, finding new position...");
                hiderPosition = World.GetRandomEmptyPosition(0f);
            }

            Hider.Position = hiderPosition;
            Hider.Direction = Raylib.GetRandomValue(0, 359);

            Seeker.ResetExploration();

            if (TotalSessions % 10 == 0)
            {
                SaveTotalSessions();
            }

            OnSessionCompleted?.Invoke();
        }

        public void Reset(Agent3D newSeeker, Agent3D newHider)
        {
            Seeker = newSeeker;
            Hider = newHider;

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;

            Seeker.ResetExploration();
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
            Raylib.DrawRectangle(5, 5, 320, 360, new Color(0, 0, 0, 180));

            int y = 10;
            Raylib.DrawText($"Session: {Session} / Total: {TotalSessions}", 10, y, 20, new Color(255,255,255,255));
            y += 25;

            Color timeColor = Timer > (Config.SessionDurationSeconds * 0.9f) ? new Color(230,41,55,255) : new Color(255,255,255,255);
            Raylib.DrawText($"Time: {Timer:F1}s / {Config.SessionDurationSeconds:F0}s", 10, y, 20, timeColor);
            y += 25;

            Raylib.DrawText($"Seeker: {SeekerScore:F1}", 10, y, 20, new Color(0,121,241,255));
            float seekerPercent = SeekerScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(seekerPercent / 100f, 1.0f)), 10, new Color(0,121,241,255));
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, new Color(255,255,255,255));
            y += 25;

            Raylib.DrawText($"Physical: {Seeker.GetExploredCount()}", 10, y, 18, new Color(255, 203, 0, 255)); // YELLOW
            y += 22;

            Raylib.DrawText($"Visual: {Seeker.GetVisuallyExploredCount()}", 10, y, 18, new Color(255, 161, 0, 255)); // ORANGE
            y += 22;

            Raylib.DrawText($"Total: {Seeker.GetTotalExploredCount()}", 10, y, 18, new Color(135, 60, 190, 255)); // PURPLE
            y += 25;

            Raylib.DrawText($"Exploration Score: {ExplorationScore:F1}", 10, y, 18, new Color(135, 60, 190, 255)); // PURPLE
            y += 25;

            Raylib.DrawText($"Hider: {HiderScore:F1}", 10, y, 20, new Color(0,228,48,255));
            float hiderPercent = HiderScore / Config.SessionDurationSeconds * 100f;
            Raylib.DrawRectangle(180, y + 5, (int)(100 * Math.Min(hiderPercent / 100f, 1.0f)), 10, new Color(0,228,48,255));
            Raylib.DrawRectangleLines(180, y + 5, 130, 10, new Color(255,255,255,255));
            y += 25;

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);
            Raylib.DrawText($"Distance: {distance:F1}", 10, y, 18, new Color(128,128,128,255));
            y += 22;

            string visibilityText = IsHiderVisible ? "VISIBLE" : "HIDDEN";
            Color visibilityColor = IsHiderVisible ? new Color(230,41,55,255) : new Color(0,228,48,255);
            Raylib.DrawText($"Hider: {visibilityText}", 10, y, 18, visibilityColor);
            y += 22;

            if (_isHiderCaught)
            {
                Raylib.DrawText("CAUGHT!", 10, y, 24, new Color(230,41,55,255));
            }
        }

        public void HandleInput()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F))
            {
                _cameraLocked = !_cameraLocked;
                if (_cameraLocked)
                {
                    Raylib.EnableCursor(); // Открепить мышь
                }
                else
                {
                    Raylib.DisableCursor(); // Захватить мышь обратно для CameraMode.Free
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.V)) _showVisionCones = !_showVisionCones;
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _showGrid = !_showGrid;
            if (Raylib.IsKeyPressed(KeyboardKey.R)) Restart();
        }

        private static void LoadTotalSessions()
        {
            try
            {
                string directory = Path.GetDirectoryName(SessionCounterFile);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(SessionCounterFile))
                {
                    TotalSessions = 0;
                    Console.WriteLine("[DEBUG] Файл общего счетчика сессий не найден, начинаем с 0");
                    return;
                }

                string json = File.ReadAllText(SessionCounterFile);
                var data = JsonConvert.DeserializeObject<SessionCounterData>(json, JsonSettings);
                TotalSessions = data?.TotalSessions ?? 0;
                Console.WriteLine($"[DEBUG] Загружен общий счетчик сессий: {TotalSessions}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка загрузки общего счетчика сессий: {ex.Message}");
                TotalSessions = 0;
            }
        }

        private static void SaveTotalSessions()
        {
            try
            {
                var data = new SessionCounterData
                {
                    TotalSessions = TotalSessions,
                    LastUpdate = DateTime.Now
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented, JsonSettings);
                File.WriteAllText(SessionCounterFile, json);

                Console.WriteLine($"[DEBUG] Сохранен общий счетчик сессий: {TotalSessions}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка сохранения общего счетчика сессий: {ex.Message}");
            }
        }

        public static void ForceSaveTotalSessions()
        {
            SaveTotalSessions();
        }

        private class SessionCounterData
        {
            public int TotalSessions { get; set; }
            public DateTime LastUpdate { get; set; }
        }
    }
}
