using System;
using System.Numerics;
using System.Linq;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Логика отрисовки и оверлеев вынесена сюда.
    public partial class Simulation3D
    {
        private void InitializeCamera()
        {
            _camera = new Camera3D
            {
                Position = new Vector3(World.Size / 2f, 25f, World.Size / 2f + 0.01f),
                Target = new Vector3(World.Size / 2f, 0f, World.Size / 2f),
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };
            _fixedCameraState = _camera;
        }

        private void UpdateCamera()
        {
            if (!_followAgent)
            {
                Raylib.UpdateCamera(ref _camera, CameraMode.Free);
                _fixedCameraState = _camera;
            }
            else
            {
                _camera = _fixedCameraState;
            }
        }

        public void HandleInput()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F))
            {
                _followAgent = !_followAgent;
                if (_followAgent)
                {
                    _fixedCameraState = _camera;
                    Raylib.EnableCursor();
                }
                else
                {
                    _camera = _fixedCameraState;
                    Raylib.DisableCursor();
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.V))
            {
                _showVisionCones = !_showVisionCones;
                Agent3D.ShowVisionCones = _showVisionCones;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.G)) _showGrid = !_showGrid;
            if (Raylib.IsKeyPressed(KeyboardKey.R)) Restart();
        }

        public void Draw()
        {
            // Защита от NaN/Inf в камере и позициях агентов
            SanitizeScene();

            Raylib.BeginMode3D(_camera);
            {
                World.Draw(true);
                if (_showGrid) World.DrawGrid();

                // Рисуем всех агентов, если заданы списки; иначе — одиночные
                if (Seekers != null && Seekers.Count > 0)
                {
                    foreach (var s in Seekers) s.Draw();
                }
                else
                {
                    Seeker.Draw();
                }

                if (Hiders != null && Hiders.Count > 0)
                {
                    foreach (var h in Hiders) h.Draw();
                }
                else
                {
                    Hider.Draw();
                }

                if (_showVisionCones)
                {
                    // Конусы взглядов рисуются непосредственно в Agent3D.Draw
                }
            }
            Raylib.EndMode3D();

            DrawHUD();
        }

        private void DrawHUD()
        {
            // FPS overlay (top-right)
            try
            {
                int fps = Raylib.GetFPS();
                string fpsText = $"FPS: {fps}";
                int fpsFont = 16;
                int fpsPad = 8;
                int fpsW = Raylib.MeasureText(fpsText, fpsFont);
                int screenW = Raylib.GetScreenWidth();
                // shadow
                Raylib.DrawText(fpsText, screenW - fpsW - fpsPad + 1, fpsPad + 1, fpsFont, new Color(0,0,0,180));
                // text
                Raylib.DrawText(fpsText, screenW - fpsW - fpsPad, fpsPad, fpsFont, Color.Yellow);
            }
            catch { }

            // Параметры оформления (уменьшенные шрифты)
            int pad = 8;
            int headerFont = 18;
            int lineFont = 16;
            int barHeight = 8;
            int barWidth = 90; // короче, чтобы не закрывать текст очков
            int headerStep = headerFont + 6;
            int lineStep = lineFont + 4;

            // Данные по командам
            var seekersList = (Seekers != null && Seekers.Count > 0) ? Seekers : new System.Collections.Generic.List<Agent3D> { Seeker };
            var hidersList  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new System.Collections.Generic.List<Agent3D> { Hider  };

            int seekersCount = seekersList.Count;
            int hidersCount  = hidersList.Count;

            int seekersSeeing = 0;
            foreach (var s in seekersList)
                if (hidersList.Any(h => s.CanSee(h, World))) seekersSeeing++;

            int visibleHiders = 0;
            foreach (var h in hidersList)
                if (seekersList.Any(s => s.CanSee(h, World))) visibleHiders++;

            // Формируем строки
            string l1 = $"Session: {Session} / Total: {TotalSessions}";
            Color timeColor = Timer > (sessionDurationSeconds * 0.9f) ? Color.Red : Color.White;
            string l2 = $"Time: {Timer:F1}s / {sessionDurationSeconds:F0}s";

            string sLine = $"Seekers: {seekersCount}  |  Seeing: {seekersSeeing}";
            string sScore = $"Score: {SeekerScore:F1}";
            float seekerPercent = MathF.Max(0f, MathF.Min(1f, sessionDurationSeconds > 0f ? SeekerScore / sessionDurationSeconds : 0f));

            string hLine = $"Hiders: {hidersCount}  |  Visible: {visibleHiders}";
            string hScore = $"Score: {HiderScore:F1}";
            float hiderPercent = MathF.Max(0f, MathF.Min(1f, sessionDurationSeconds > 0f ? HiderScore / sessionDurationSeconds : 0f));

            float distance = Vector3.Distance(Seeker.Position, Hider.Position);
            string distLine = $"Distance (S0-H0): {distance:F1}";

            string visibilityText = IsHiderVisible ? "VISIBLE" : "HIDDEN";

            // Доп. метрики для баров
            float timePercent = Math.Clamp(sessionDurationSeconds > 0f ? (Timer / sessionDurationSeconds) : 0f, 0f, 1f);
            int effectiveFramesForCatch = Config.EffectiveFramesForCatch;
            string catchLine = $"Catch: {_caughtFrames}/{effectiveFramesForCatch}";
            int l2W = Raylib.MeasureText(l2, headerFont);
            int catchW = Raylib.MeasureText(catchLine, lineFont);

            // Подсчет размеров подложки с учётом текстов и баров справа от них
            int maxTextW = 0;
            int l1W = Raylib.MeasureText(l1, headerFont);
            maxTextW = Math.Max(maxTextW, l1W);
            maxTextW = Math.Max(maxTextW, l2W); // только текст, для смещения бара

            int sLineW = Raylib.MeasureText(sLine, lineFont);
            int sScoreTextW = Raylib.MeasureText(sScore, lineFont);
            int hLineW = Raylib.MeasureText(hLine, lineFont);
            int hScoreTextW = Raylib.MeasureText(hScore, lineFont);
            int visTextW = Raylib.MeasureText($"Visibility: {visibilityText}", lineFont);

            // Ширина строк без баров
            maxTextW = Math.Max(maxTextW, sLineW);
            maxTextW = Math.Max(maxTextW, hLineW);
            maxTextW = Math.Max(maxTextW, Raylib.MeasureText(distLine, lineFont));

            // Строки с барами: текст + отступ + бар [+ отступ + подпись]
            int timeLineTotalW = l2W + 8 + barWidth;
            int seekerScoreTotalW = sScoreTextW + 8 + barWidth;
            int hiderScoreTotalW = hScoreTextW + 8 + barWidth;
            int visibilityTotalW = visTextW + 8 + barWidth + 8 + catchW;

            maxTextW = Math.Max(maxTextW, timeLineTotalW);
            maxTextW = Math.Max(maxTextW, seekerScoreTotalW);
            maxTextW = Math.Max(maxTextW, hiderScoreTotalW);
            maxTextW = Math.Max(maxTextW, visibilityTotalW);

            int blockW = maxTextW + pad * 2 + 4;
            int extraBottomPad = 8; // немного увеличим высоту подложки, чтобы нижняя строка не упиралась в границу
            int blockH = pad + headerStep + headerStep + lineStep + barHeight + lineStep + barHeight + lineStep + barHeight + lineStep + barHeight + lineStep + pad + extraBottomPad + 4;

            int x = pad;
            int y = pad;

            // Подложка
            Color bg = new Color(0, 0, 0, 140);
            Raylib.DrawRectangle(x - 2, y - 2, blockW + 4, blockH + 4, new Color(0, 0, 0, 80));
            Raylib.DrawRectangle(x, y, blockW, blockH, bg);

            // Линия 1: Session/Total
            Raylib.DrawText(l1, x + pad, y + pad, headerFont, Color.White);
            y += headerStep;

            // Линия 2: Time
            Raylib.DrawText(l2, x + pad, y, headerFont, timeColor);
            // Рисуем таймер-бар справа от текста времени
            int timeBarX = x + pad + l2W + 8;
            int timeBarY = y + (headerFont - barHeight) / 2 + 2;
            int timeBarW = (int)(barWidth * timePercent);
            // Гарантируем, что бар не выйдет за пределы подложки
            int timeAvail = (x + blockW - pad) - timeBarX;
            int timeBgW = Math.Max(0, Math.Min(barWidth, timeAvail));
            int timeFillW = Math.Max(0, Math.Min(timeBarW, timeAvail));
            Raylib.DrawRectangle(timeBarX, timeBarY, timeBgW, barHeight, new Color(40, 40, 40, 180));
            Raylib.DrawRectangle(timeBarX, timeBarY, timeFillW, barHeight, Color.SkyBlue);
            y += headerStep;

            // Seekers
            Raylib.DrawText("Seekers", x + pad, y, lineFont, new Color(50, 205, 50, 255));
            y += lineStep;
            Raylib.DrawText(sLine, x + pad, y, lineFont, new Color(50, 205, 50, 255));
            y += lineStep;
            Raylib.DrawText(sScore, x + pad, y, lineFont, new Color(50, 205, 50, 255));
            int sBarX = x + pad + Raylib.MeasureText(sScore, lineFont) + 8;
            int sBarY = y + (lineFont - barHeight) / 2 + 2;
            int sBarW = (int)(barWidth * seekerPercent);
            int sAvail = (x + blockW - pad) - sBarX;
            int sBgW = Math.Max(0, Math.Min(barWidth, sAvail));
            int sFillW = Math.Max(0, Math.Min(sBarW, sAvail));
            Raylib.DrawRectangle(sBarX, sBarY, sBgW, barHeight, new Color(40, 40, 40, 180));
            Raylib.DrawRectangle(sBarX, sBarY, sFillW, barHeight, new Color(50, 205, 50, 255));
            y += lineStep;

            // Hiders
            Raylib.DrawText("Hiders", x + pad, y, lineFont, Color.Orange);
            y += lineStep;
            Raylib.DrawText(hLine, x + pad, y, lineFont, Color.Orange);
            y += lineStep;
            Raylib.DrawText(hScore, x + pad, y, lineFont, Color.Orange);
            int hBarX = x + pad + Raylib.MeasureText(hScore, lineFont) + 8;
            int hBarY = y + (lineFont - barHeight) / 2 + 2;
            int hBarW = (int)(barWidth * hiderPercent);
            int hAvail = (x + blockW - pad) - hBarX;
            int hBgW = Math.Max(0, Math.Min(barWidth, hAvail));
            int hFillW = Math.Max(0, Math.Min(hBarW, hAvail));
            Raylib.DrawRectangle(hBarX, hBarY, hBgW, barHeight, new Color(40, 40, 40, 180));
            Raylib.DrawRectangle(hBarX, hBarY, hFillW, barHeight, Color.Orange);
            y += lineStep;

            // Distance & Visibility
            Raylib.DrawText(distLine, x + pad, y, lineFont, Color.SkyBlue);
            y += lineStep;
            Raylib.DrawText($"Visibility: {visibilityText}", x + pad, y, lineFont, IsHiderVisible ? Color.Red : Color.Green);
            // Catch progress bar to the right
            int catchX = x + pad + Raylib.MeasureText($"Visibility: {visibilityText}", lineFont) + 8;
            int catchY = y + (lineFont - barHeight) / 2 + 2;
            int catchBarW = (int)(barWidth * Math.Clamp(effectiveFramesForCatch > 0 ? (_caughtFrames / (float)effectiveFramesForCatch) : 0f, 0f, 1f));
            int catchAvail = (x + blockW - pad) - catchX;
            int catchBgW = Math.Max(0, Math.Min(barWidth, catchAvail));
            int catchFillW = Math.Max(0, Math.Min(catchBarW, catchAvail));
            Raylib.DrawRectangle(catchX, catchY, catchBgW, barHeight, new Color(40, 40, 40, 180));
            Raylib.DrawRectangle(catchX, catchY, catchFillW, barHeight, Color.Red);
            // and label at the end of bar
            int catchTextX = catchX + catchBgW + 8;
            Raylib.DrawText(catchLine, catchTextX, y, lineFont, Color.Yellow);
            y += lineStep;

            // Служебная метка
            if (_isHiderCaught)
                Raylib.DrawText("CAUGHT!", x, y, 18, Color.Red);
        }
    }
}
