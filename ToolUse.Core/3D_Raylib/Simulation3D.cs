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
        public bool IsHiderCaught => _isHiderCaught;

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
        // Использование кэширования для предотвращения множественных вычислений видимости
        private bool _isHiderVisible = false;
        private float _lastVisibilityCheck = 0f;
        private const float _visibilityCheckInterval = 0.05f; // Проверять видимость не чаще чем раз в 50 мс

        public bool IsHiderVisible 
        { 
            get => _isHiderVisible; 
            private set => _isHiderVisible = value; 
        }
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

            // Оптимизация: проверяем видимость не каждый кадр, а с определенным интервалом
            _lastVisibilityCheck += deltaTime;
            if (_lastVisibilityCheck >= _visibilityCheckInterval)
            {
                IsHiderVisible = Seeker.CanSee(Hider, World);
                _lastVisibilityCheck = 0f;

                // Отладка
                // Console.WriteLine($"Visibility check at {Timer:F2}s: {IsHiderVisible}");
            }

            // Корректное детектирование "пойман"
            if (IsHiderVisible)
            {
                if (++_caughtFrames >= Raylib.GetFPS() * 3) // Уменьшаем время до поимки для более быстрой реакции
                {
                    _isHiderCaught = true;
                    // Console.WriteLine($"Hider caught at {Timer:F2}s!");
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
            // Более наглядное обновление очков с логированием
            if (IsHiderVisible) 
            {
                SeekerScore += deltaTime;
                // Раскомментируйте для отладки
                //Console.WriteLine($"Hider visible! Adding {deltaTime:F2} to Seeker Score. Total: {SeekerScore:F2}");
            }
            else 
            {
                HiderScore += deltaTime;
                //Console.WriteLine($"Hider NOT visible. Adding {deltaTime:F2} to Hider Score. Total: {HiderScore:F2}");
            }
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
                    // Меняем цвет конуса в зависимости от видимости для наглядности
                    Color seekerConeColor = IsHiderVisible ? 
                        new Color(255, 255, 0, 100) : // Желтый, если видит
                        new Color(0, 0, 255, 80);    // Синий, если не видит

                    Seeker.DrawVisionCone(World, seekerConeColor);
                    Hider.DrawVisionCone(World, new Color(0, 255, 0, 80));
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            // Фон для информационной панели с увеличенной высотой
            Raylib.DrawRectangle(5, 5, 250, 210, Raylib.ColorAlpha(Color.Black, 0.7f));

            int y = 10;
            Raylib.DrawText($"Session: {Session}", 10, y, 20, Color.White);
            y += 25;

            // Отображаем таймер с предупреждением, когда приближается конец сессии
            Color timeColor = Timer > 50f ? Color.Red : Color.White;
            Raylib.DrawText($"Time: {Timer:F1}s / 60s", 10, y, 20, timeColor);
            y += 25;

            // Отображаем счет с более выразительным форматированием
            Raylib.DrawText($"Seeker: {SeekerScore:F1}", 10, y, 20, Color.Blue);
            // Добавляем прогресс-бар для счета
            float seekerPercent = SeekerScore / 60f * 100f; // Процент от максимального времени
            Raylib.DrawRectangle(120, y + 5, (int)(100 * (seekerPercent / 100f)), 10, Color.Blue);
            Raylib.DrawRectangleLines(120, y + 5, 100, 10, Color.White);
            y += 25;

            Raylib.DrawText($"Hider: {HiderScore:F1}", 10, y, 20, Color.Green);
            float hiderPercent = HiderScore / 60f * 100f;
            Raylib.DrawRectangle(120, y + 5, (int)(100 * (hiderPercent / 100f)), 10, Color.Green);
            Raylib.DrawRectangleLines(120, y + 5, 100, 10, Color.White);
            y += 25;

            // Более заметное отображение статуса видимости
            Color visibilityColor = IsHiderVisible ? Color.Gold : Color.Gray;
            string visibilityText = IsHiderVisible ? "VISIBLE!" : "Hidden";
            Raylib.DrawText($"Hider: {visibilityText}", 10, y, 20, visibilityColor);

            // Мигающий индикатор, если hider видим
            if (IsHiderVisible && (int)(Timer * 2) % 2 == 0)
            {
                Raylib.DrawRectangle(120, y, 15, 15, Color.Gold);
            }
            y += 25;

            // Информация о статусе видимости конусов
            Raylib.DrawText($"Vision Cones: {(_showVisionCones ? "ON" : "OFF")}", 10, y, 20,
                _showVisionCones ? Color.Lime : Color.Gray);
            y += 25;

            // Информация о статусе сетки
            Raylib.DrawText($"Grid: {(_showGrid ? "ON" : "OFF")}", 10, y, 20,
                _showGrid ? Color.Lime : Color.Gray);
            y += 25;

            // Дополнительная информация о режиме камеры
            Raylib.DrawText($"Camera: {(_followAgent ? "Follow" : "Free")}", 10, y, 20,
                _followAgent ? Color.Lime : Color.Yellow);

            // Сообщение о поимке с эффектами
            if (_isHiderCaught)
            {
                string caughtText = "CAUGHT!";
                int textWidth = Raylib.MeasureText(caughtText, 60); // Увеличиваем размер шрифта

                // Фон для сообщения
                Raylib.DrawRectangle(
                    (Raylib.GetScreenWidth() - textWidth) / 2 - 20,
                    Raylib.GetScreenHeight() / 2 - 40,
                    textWidth + 40,
                    80,
                    Raylib.ColorAlpha(Color.Black, 0.8f));

                // Мигающий текст для привлечения внимания
                Color caughtColor = (int)(Timer * 4) % 2 == 0 ? Color.Gold : Color.Red;

                Raylib.DrawText(caughtText, 
                    (Raylib.GetScreenWidth() - textWidth) / 2,
                    Raylib.GetScreenHeight() / 2 - 30, 
                    60, 
                    caughtColor);
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
            // Обновляем агентов
            this.Seeker = seeker;
            this.Hider = hider;

            // Сбрасываем состояние симуляции
            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;

            // Пересоздаем адаптер для новых агентов
            _adapter.GetType().GetField("_seeker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_adapter, seeker);
            _adapter.GetType().GetField("_hider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_adapter, hider);

            // Перегенерируем карту
            World.GenerateStaticGrid();
        }
    }
}