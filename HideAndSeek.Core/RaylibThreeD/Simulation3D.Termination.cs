using System;
using System.Numerics;
using System.Collections.Generic;
using Raylib_cs;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Логика определения завершения эпизода и перезапуска вынесена сюда.
    public partial class Simulation3D
    {
        public void Restart()
        {
            _justRestarted = true;

            // Re-sync action repeat with current config (in case it was changed at runtime)
            _actionRepeat = Math.Max(1, Config.ActionRepeat);
            // Re-sync no-progress thresholds from config (runtime changes should apply)
            _noProgressDistanceEps = MathF.Max(0f, Config.NoProgressDistanceEps);
            _noProgressSeconds = MathF.Max(0f, Config.NoProgressSeconds);
            // Re-sync visibility check interval from config (runtime changes should apply)
            _visibilityCheckInterval = MathF.Max(0.001f, Config.VisibilityCheckInterval);
            _lastVisibilityCheck = 0f;
            // Re-sync session duration from config to apply runtime changes
            sessionDurationSeconds = Config.SessionDurationSeconds;

            // Log previous session metrics before resetting
            if (_framesInSession > 0)
                AppendSessionMetrics();

            Session++;
            TotalSessions++;
            SaveTotalSessions();

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = false;

            // reset metrics
            _framesInSession = 0;
            _visibleFrames = 0;
            _sumDistance = 0f;
            _accSeekerReward = 0f;
            _accHiderReward = 0f;
            _noProgressTimer = 0f;
            _lastDistanceForProgress = 0f;
            _lastSeekerVisualExploredForProgress = 0;

            World.GenerateStaticGrid();

            // Новый эпизод — очищаем общие знания команд
            _seekersBoard.Clear();
            _hidersBoard.Clear();

            Vector3 seekerPos = World.GetRandomValidAgentPosition(Config.Seeker.AgentRadius, 0f);
            Vector3 hiderPos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
            int attempts = 0;
            float crossTeamMinSeparation = MathF.Max(Config.MinInitialSeparation, 1.0f);
            int maxAttempts = Math.Max(1, Config.InitialPlacementMaxAttempts);
            while (attempts < maxAttempts)
            {
                float dx = seekerPos.X - hiderPos.X;
                float dz = seekerPos.Z - hiderPos.Z;
                if ((dx * dx + dz * dz) >= (crossTeamMinSeparation * crossTeamMinSeparation))
                    break;
                hiderPos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
                attempts++;
            }
            CheckNaN(seekerPos, "Restart:seekerPos");
            CheckNaN(hiderPos, "Restart:hiderPos");

            Seeker.Position = seekerPos;
            Seeker.Direction = Raylib.GetRandomValue(0, 359);
            Seeker.InitWorldSize(World.Size);
            Seeker.SetWorld(World);
            Seeker.TeamBoard = _seekersBoard;

            Hider.Position = hiderPos;
            Hider.Direction = Raylib.GetRandomValue(0, 359);
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);
            Hider.TeamBoard = _hidersBoard;

            // Если коллекции заданы — респавним всех
            if (Seekers != null && Seekers.Count > 0)
            {
                for (int i = 0; i < Seekers.Count; i++)
                {
                    Vector3 pos = World.GetRandomValidAgentPosition(Config.Seeker.AgentRadius, 0f);
                    Seekers[i].Position = pos;
                    Seekers[i].Direction = Raylib.GetRandomValue(0, 359);
                    Seekers[i].InitWorldSize(World.Size);
                    Seekers[i].SetWorld(World);
                    Seekers[i].TeamBoard = _seekersBoard;
                }
                // Гарантируем, что «первый» совпадает с основным
                Seekers[0].Position = Seeker.Position;
                Seekers[0].Direction = Seeker.Direction;
            }

            if (Hiders != null && Hiders.Count > 0)
            {
                for (int i = 0; i < Hiders.Count; i++)
                {
                    Vector3 pos = World.GetRandomValidAgentPosition(Config.Hider.AgentRadius, 0f);
                    Hiders[i].Position = pos;
                    Hiders[i].Direction = Raylib.GetRandomValue(0, 359);
                    Hiders[i].InitWorldSize(World.Size);
                    Hiders[i].SetWorld(World);
                    Hiders[i].TeamBoard = _hidersBoard;
                }
                Hiders[0].Position = Hider.Position;
                Hiders[0].Direction = Hider.Direction;
            }

            // Seed last valid positions after respawn
            _lastValidPos.Clear();
            RememberAllAgentsValidPositions();

            // Полный сброс исследования и знаний для нового эпизода
            Seeker.ResetExploration();
            Seeker.KnownWalls.Clear();
            Hider.ResetExploration();
            Hider.KnownWalls.Clear();

            if (Seekers != null && Seekers.Count > 0)
            {
                foreach (var s in Seekers)
                {
                    s.ResetExploration();
                    s.KnownWalls.Clear();
                }
            }
            if (Hiders != null && Hiders.Count > 0)
            {
                foreach (var h in Hiders)
                {
                    h.ResetExploration();
                    h.KnownWalls.Clear();
                }
            }

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();

            // Очистка per-agent структур при новом эпизоде
            _prevStateSeekers.Clear();
            _prevStateHiders.Clear();
            _prevActionSeekers.Clear();
            _prevActionHiders.Clear();
            _repeatLeftSeekers.Clear();
            _repeatLeftHiders.Clear();
            _currentActionSeekers.Clear();
            _currentActionHiders.Clear();
            _lastDistToNearestSeeker.Clear();
            _wasHiderVisiblePrevMap.Clear();
            _prevExploreCountsSeekers.Clear();
            _lastDistToNearestHider.Clear();
        }

        public void Reset(Agent3D newSeeker, Agent3D newHider)
        {
            // Re-sync action repeat on agent reset as well
            _actionRepeat = Math.Max(1, Config.ActionRepeat);
            // Re-sync no-progress thresholds from config (runtime changes should apply)
            _noProgressDistanceEps = MathF.Max(0f, Config.NoProgressDistanceEps);
            _noProgressSeconds = MathF.Max(0f, Config.NoProgressSeconds);
            // Re-sync visibility check interval from config (runtime changes should apply)
            _visibilityCheckInterval = MathF.Max(0.001f, Config.VisibilityCheckInterval);
            _lastVisibilityCheck = 0f;
            // Re-sync session duration from config to apply runtime changes
            sessionDurationSeconds = Config.SessionDurationSeconds;

            Seeker = newSeeker;
            Hider = newHider;

            Timer = 0f;
            SeekerScore = 0f;
            HiderScore = 0f;
            ExplorationScore = 0f;
            _isHiderCaught = false;
            _caughtFrames = 0;
            _catchBonusGiven = false;
            _wasHiderVisiblePrev = false;

            Seeker.InitWorldSize(World.Size);
            Seeker.SetWorld(World);
            Seeker.TeamBoard = _seekersBoard;
            Hider.InitWorldSize(World.Size);
            Hider.SetWorld(World);
            Hider.TeamBoard = _hidersBoard;

            // Убедимся, что новые агенты стоят на валидных клетках мира симуляции
            EnsureAgentOnValidCell(Seeker);
            EnsureAgentOnValidCell(Hider);

            // Обновим кэш последних валидных позиций
            _lastValidPos.Clear();
            RememberAllAgentsValidPositions();

            // Полный сброс исследования и знаний
            Seeker.ResetExploration();
            Seeker.KnownWalls.Clear();
            Hider.ResetExploration();
            Hider.KnownWalls.Clear();

            if (Seekers != null && Seekers.Count > 0)
            {
                foreach (var s in Seekers)
                {
                    s.ResetExploration();
                    s.KnownWalls.Clear();
                }
            }
            if (Hiders != null && Hiders.Count > 0)
            {
                foreach (var h in Hiders)
                {
                    h.ResetExploration();
                    h.KnownWalls.Clear();
                }
            }

            _prevPhysicalExplored = Seeker.GetExploredCount();
            _prevVisualExplored   = Seeker.GetVisuallyExploredCount();

            // Сброс per-agent структур
            _prevStateSeekers.Clear();
            _prevStateHiders.Clear();
            _prevActionSeekers.Clear();
            _prevActionHiders.Clear();
            _repeatLeftSeekers.Clear();
            _repeatLeftHiders.Clear();
            _currentActionSeekers.Clear();
            _currentActionHiders.Clear();
            _lastDistToNearestSeeker.Clear();
            _wasHiderVisiblePrevMap.Clear();
            _prevExploreCountsSeekers.Clear();
            _lastDistToNearestHider.Clear();

            CheckNaN(Seeker.Position, "Reset:Seeker.Position");
            CheckNaN(Hider.Position, "Reset:Hider.Position");
        }
    }
}
