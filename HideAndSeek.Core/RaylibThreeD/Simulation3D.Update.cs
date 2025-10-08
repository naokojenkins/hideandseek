using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Логика шага симуляции (Update) вынесена сюда.
    public partial class Simulation3D
    {
        public void Update(float deltaTime)
        {
            // Используем фиксированный dt: ускорение времени выполняется в Program.cs за счёт подшагов
            float dt = deltaTime;

            Timer += dt;

            // Эффективный порог «кадров видимости» берём напрямую из конфига, без кэширования
            int framesThreshold = Config.EffectiveFramesForCatch;

            // Предсказание завершения эпизода в этом кадре
            bool willCatchThisStep = IsHiderVisible && (_caughtFrames + 1 >= framesThreshold);
            bool willTimeoutThisStep = (Timer >= sessionDurationSeconds);

            UpdateRLAgents(dt, willCatchThisStep, willTimeoutThisStep);

            if (_justRestarted)
            {
                _justRestarted = false;
                return;
            }

            UpdateCamera();

            _lastVisibilityCheck += dt;
            if (_lastVisibilityCheck >= _visibilityCheckInterval)
            {
                IsHiderVisible = AnyHiderVisible();
                _lastVisibilityCheck = 0f;

                // Update individual memories: decay and report sightings
                float now = Timer;
                void ProcessTeam(List<Agent3D> team, List<Agent3D> opponents)
                {
                    foreach (var a in team)
                    {
                        a.Memory.Decay(now);
                    }
                    foreach (var a in team)
                    {
                        // Allies
                        foreach (var b in team)
                        {
                            if (ReferenceEquals(a, b)) continue;
                            if (a.CanSee(b, World))
                            {
                                var kind = a.IsSeeker == b.IsSeeker ? MemoryKind.Ally : MemoryKind.Opponent;
                                a.Memory.ReportSeen(b.Id, kind, b.Position, b.Direction, now);
                            }
                        }
                        // Opponents
                        foreach (var o in opponents)
                        {
                            if (a.CanSee(o, World))
                            {
                                a.Memory.ReportSeen(o.Id, MemoryKind.Opponent, o.Position, o.Direction, now);
                            }
                        }
                    }
                }

                var seekersList = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
                var hidersList  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };
                ProcessTeam(seekersList, hidersList);
                ProcessTeam(hidersList, seekersList);
            }

            if (IsHiderVisible)
            {
                if (++_caughtFrames >= framesThreshold)
                {
                    _isHiderCaught = true;
                }
            }
            else
            {
                _caughtFrames = 0;
            }

            UpdateScores(dt);

            // Track last known valid positions after all updates this frame
            RememberAllAgentsValidPositions();

            if (_isHiderCaught || Timer >= sessionDurationSeconds)
            {
                try { OnSessionCompleted?.Invoke(); } catch { }
                Restart();
                return;
            }
        }

    }
}
