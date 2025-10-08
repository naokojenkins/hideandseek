using System;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using HideAndSeek.Core.RL;

namespace HideAndSeek.Core.RaylibThreeD
{
    // RL-процедуры одного шага (выбор действий, движение, награды, обучение)
    public partial class Simulation3D
    {
        private void UpdateRLAgents(float deltaTime, bool isTerminalByCatchThisStep, bool isTerminalByTimeoutThisStep)
        {
            // Списки активных агентов (если коллекции пусты — используем одиночные)
            var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
            var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

            // Единая семантика действий из конфига
            var actCfg = Config.Actions;

            // 1) Состояния ДО действия (state_t)
            var seekerStatesBefore = new Dictionary<Agent3D, State>(seekers.Count);
            var hiderStatesBefore  = new Dictionary<Agent3D, State>(hiders.Count);

            foreach (var s in seekers)
            {
                var target = GetNearestOpponent(s, hiders);
                var ad = new SimAdapter3D(World, s, hiders);
                var st = ad.GetSeekerState();
                CheckNaN(st.ToArray(World.Size), "seekerState_before");
                seekerStatesBefore[s] = st;

                if (!_lastDistToNearestHider.ContainsKey(s))
                    _lastDistToNearestHider[s] = Vector3.Distance(s.Position, target.Position);

                if (!_prevExploreCountsSeekers.ContainsKey(s))
                    _prevExploreCountsSeekers[s] = (s.GetExploredCount(), s.GetVisuallyExploredCount());
            }
            foreach (var h in hiders)
            {
                var watcher = GetNearestOpponent(h, seekers);
                var ad = new SimAdapter3D(World, watcher, h);
                var st = ad.GetHiderState();
                CheckNaN(st.ToArray(World.Size), "hiderState_before");
                hiderStatesBefore[h] = st;

                if (!_lastDistToNearestSeeker.ContainsKey(h))
                    _lastDistToNearestSeeker[h] = Vector3.Distance(h.Position, watcher.Position);
                if (!_wasHiderVisiblePrevMap.ContainsKey(h))
                    _wasHiderVisiblePrevMap[h] = false;
            }

            // Терминальность текущего шага (поимка или тайм-аут)
            bool isTerminalThisStep = isTerminalByCatchThisStep || isTerminalByTimeoutThisStep;

            // Подготовка catch-бонуса на кадр: поровну на всех Seeker (в кадре поимки)
            bool giveCatchBonus = isTerminalByCatchThisStep && !_catchBonusGiven;
            float perSeekerCatchBonus = giveCatchBonus ? (Config.Seeker.CatchBonus / Math.Max(1, seekers.Count)) : 0f;

            // 2) Выбор действия (action_t) с учётом action repeat
            foreach (var s in seekers)
            {
                if (!_repeatLeftSeekers.TryGetValue(s, out int left) || left <= 0)
                {
                    // Если Seeker замечен противником (любой Hider видит его) — принудительно используем exploit
                    bool isSeenByOpponentNow = hiders.Any(h => h.CanSee(s, World));

                    long a;
                    if (isSeenByOpponentNow)
                    {
                        // Временно обнуляем epsilon через публичный API, сохранив текущее значение
                        float epsBackup = _seekerAgent.Epsilon;
                        try { _seekerAgent.SetEpsilon(0f); } catch { }

                        // Базовое жадное действие от DQN
                        a = _seekerAgent.ChooseAction(seekerStatesBefore[s].ToArray(World.Size));

                        // Эвристика «беги от ближайшего Hider»: поворот+движение, увеличивающие дистанцию
                        try
                        {
                            var nearest = GetNearestOpponent(s, hiders);
                            var away = Vector3.Normalize(s.Position - nearest.Position);
                            if (float.IsNaN(away.X) || float.IsNaN(away.Z)) away = new Vector3(0,0,1);

                            float desiredYaw = MathF.Atan2(away.X, away.Z) * (180f / MathF.PI);
                            // Нормализуем углы в [-180,180]
                            float curYaw = ((s.Direction % 360f) + 540f) % 360f - 180f;
                            float diff = desiredYaw - curYaw;
                            diff = ((diff + 540f) % 360f) - 180f;

                            var act = Config.Actions;
                            float alignDeg = Config.Seeker.AlignThresholdDegrees > 0f
                                ? Config.Seeker.AlignThresholdDegrees
                                : Config.Seeker.RotationStepDegrees * Config.Seeker.TurnAlignFactor;
                            if (MathF.Abs(diff) < alignDeg)
                            {
                                a = act.Forward;
                            }
                            else if (diff > 0)
                            {
                                a = act.ForwardRight >= 0 ? act.ForwardRight : act.TurnRight;
                            }
                            else
                            {
                                a = act.ForwardLeft >= 0 ? act.ForwardLeft : act.TurnLeft;
                            }
                        }
                        catch { /* fallback to DQN action */ }

                        // Восстановим исходный epsilon
                        try { _seekerAgent.SetEpsilon(epsBackup); } catch { }
                    }
                    else
                    {
                        a = _seekerAgent.ChooseAction(seekerStatesBefore[s].ToArray(World.Size));
                    }

                    _currentActionSeekers[s] = a;
                    _repeatLeftSeekers[s] = _actionRepeat - 1;
                }
                else
                {
                    _repeatLeftSeekers[s] = left - 1;
                }

                // Зафиксируем пары (state_t, action_t) для текущего кадра
                _prevStateSeekers[s] = seekerStatesBefore[s];
                _prevActionSeekers[s] = _currentActionSeekers[s];
            }
            foreach (var h in hiders)
            {
                if (!_repeatLeftHiders.TryGetValue(h, out int left) || left <= 0)
                {
                    // Принудительный exploitation для хайдера, если он видим и режим включён
                    bool isSeenNow = seekers.Any(s => s.CanSee(h, World));
                    bool forceExploit = Config.Hider.ForceExploitWhenSeen && isSeenNow;

                    long a;
                    if (forceExploit)
                    {
                        float epsBackup = _hiderAgent.Epsilon;
                        try { _hiderAgent.SetEpsilon(0f); } catch { }
                        // Базовое жадное действие
                        a = _hiderAgent.ChooseAction(hiderStatesBefore[h].ToArray(World.Size));

                        // Эвристика «беги от ближайшего Seeker» при видимости
                        try
                        {
                            var visibleSeekers = seekers.Where(s => s.CanSee(h, World)).ToList();
                            var threat = visibleSeekers.Count > 0 ? GetNearestOpponent(h, visibleSeekers) : GetNearestOpponent(h, seekers);
                            var away = Vector3.Normalize(h.Position - threat.Position);
                            if (float.IsNaN(away.X) || float.IsNaN(away.Z)) away = new Vector3(0,0,1);

                            float desiredYaw = MathF.Atan2(away.X, away.Z) * (180f / MathF.PI);
                            float curYaw = ((h.Direction % 360f) + 540f) % 360f - 180f;
                            float diff = desiredYaw - curYaw;
                            diff = ((diff + 540f) % 360f) - 180f;

                            var act = Config.Actions;
                            float alignDeg = Config.Hider.AlignThresholdDegrees > 0f
                                ? Config.Hider.AlignThresholdDegrees
                                : Config.Hider.RotationStepDegrees * Config.Hider.TurnAlignFactor;
                            if (MathF.Abs(diff) < alignDeg)
                            {
                                a = act.Forward;
                            }
                            else if (diff > 0)
                            {
                                a = act.ForwardRight >= 0 ? act.ForwardRight : act.TurnRight;
                            }
                            else
                            {
                                a = act.ForwardLeft >= 0 ? act.ForwardLeft : act.TurnLeft;
                            }
                        }
                        catch { /* fallback to DQN action */ }

                        // Восстановим исходный epsilon
                        try { _hiderAgent.SetEpsilon(epsBackup); } catch { }
                    }
                    else
                    {
                        a = _hiderAgent.ChooseAction(hiderStatesBefore[h].ToArray(World.Size));
                    }

                    _currentActionHiders[h] = a;
                    _repeatLeftHiders[h] = _actionRepeat - 1;
                }
                else
                {
                    _repeatLeftHiders[h] = left - 1;
                }

                _prevStateHiders[h] = hiderStatesBefore[h];
                _prevActionHiders[h] = _currentActionHiders[h];
            }

            // 3) Применяем повороты (часть действия)
            foreach (var s in seekers)
            {
                float rot = Config.Seeker.RotationStepDegrees;
                long aNow = _currentActionSeekers.TryGetValue(s, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.TurnLeft || aNow == actCfg.ForwardLeft) s.Rotate(-rot);
                if (aNow == actCfg.TurnRight || aNow == actCfg.ForwardRight) s.Rotate(+rot);
            }
            foreach (var h in hiders)
            {
                float rot = Config.Hider.RotationStepDegrees;
                long aNow = _currentActionHiders.TryGetValue(h, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.TurnLeft || aNow == actCfg.ForwardLeft) h.Rotate(-rot);
                if (aNow == actCfg.TurnRight || aNow == actCfg.ForwardRight) h.Rotate(+rot);
            }

            // 4) Движение вперёд (часть действия) с учётом соседей
            foreach (var s in seekers)
            {
                long aNow = _currentActionSeekers.TryGetValue(s, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.Forward || aNow == actCfg.ForwardLeft || aNow == actCfg.ForwardRight)
                {
                    var neighbors = new List<Agent3D>();
                    foreach (var s2 in seekers) if (!ReferenceEquals(s2, s)) neighbors.Add(s2);
                    neighbors.AddRange(hiders);

                    var filtered = new List<Agent3D>(neighbors.Count);
                    bool hadOverlaps = false;
                    foreach (var n in neighbors)
                    {
                        if (!IsFiniteVec(s.Position) || !IsFiniteVec(n.Position))
                        {
                            try { LogNumericIssue("NeighborsFilter.Seeker", $"Non-finite pos: self={s.Position}, other={n.Position}"); } catch { }
                            continue;
                        }
                        float d = Vector3.Distance(s.Position, n.Position);
                        if (float.IsNaN(d) || float.IsInfinity(d) || d < 1e-5f)
                        {
                            hadOverlaps = true;
                            try { LogNumericIssue("NeighborsFilter.Seeker", $"Too close/invalid distance: d={d} self={s.Position} other={n.Position}"); } catch { }
                            continue;
                        }
                        filtered.Add(n);
                    }

                    try
                    {
                        s.MoveWithCollisionAvoidance(World, deltaTime, filtered);
                    }
                    catch (ArithmeticException ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Seeker", $"ArithmeticException: {ex.Message} self={s.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Seeker", $"Exception: {ex.Message} self={s.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                }
            }
            foreach (var h in hiders)
            {
                long aNow = _currentActionHiders.TryGetValue(h, out var act) ? act : actCfg.Forward;
                if (aNow == actCfg.Forward || aNow == actCfg.ForwardLeft || aNow == actCfg.ForwardRight)
                {
                    var neighbors = new List<Agent3D>();
                    foreach (var h2 in hiders) if (!ReferenceEquals(h2, h)) neighbors.Add(h2);
                    neighbors.AddRange(seekers);

                    var filtered = new List<Agent3D>(neighbors.Count);
                    bool hadOverlaps = false;
                    foreach (var n in neighbors)
                    {
                        if (!IsFiniteVec(h.Position) || !IsFiniteVec(n.Position))
                        {
                            try { LogNumericIssue("NeighborsFilter.Hider", $"Non-finite pos: self={h.Position}, other={n.Position}"); } catch { }
                            continue;
                        }
                        float d = Vector3.Distance(h.Position, n.Position);
                        if (float.IsNaN(d) || float.IsInfinity(d) || d < 1e-5f)
                        {
                            hadOverlaps = true;
                            try { LogNumericIssue("NeighborsFilter.Hider", $"Too close/invalid distance: d={d} self={h.Position} other={n.Position}"); } catch { }
                            continue;
                        }
                        filtered.Add(n);
                    }

                    try
                    {
                        h.MoveWithCollisionAvoidance(World, deltaTime, filtered);
                    }
                    catch (ArithmeticException ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Hider", $"ArithmeticException: {ex.Message} self={h.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { LogNumericIssue("MoveWithCollisionAvoidance.Hider", $"Exception: {ex.Message} self={h.Position} neighbors={filtered.Count} hadOverlaps={hadOverlaps} dt={deltaTime}"); } catch { }
                    }
                }
            }

            // 5) Побочные эффекты шага: обновление визуального исследования
            foreach (var s in seekers) s.UpdateVisualExploration(World);
            foreach (var h in hiders)  h.UpdateVisualExploration(World);

            // Карта текущей видимости для всех Hider
            var hiderVisibleNow = new Dictionary<Agent3D, bool>(hiders.Count);
            foreach (var h in hiders)
                hiderVisibleNow[h] = seekers.Any(s => s.CanSee(h, World));

            // 6) Состояния ПОСЛЕ действия (state_{t+1})
            var seekerStatesAfter = new Dictionary<Agent3D, State>(seekers.Count);
            var hiderStatesAfter  = new Dictionary<Agent3D, State>(hiders.Count);

            foreach (var s in seekers)
            {
                var target = GetNearestOpponent(s, hiders);
                var ad = new SimAdapter3D(World, s, hiders);
                var st = ad.GetSeekerState();
                seekerStatesAfter[s] = st;
            }
            foreach (var h in hiders)
            {
                var watcher = GetNearestOpponent(h, seekers);
                var ad = new SimAdapter3D(World, watcher, h);
                var st = ad.GetHiderState();
                hiderStatesAfter[h] = st;
            }

            // 7) Награды и запись переходов за текущий шаг
            bool anyVisibleNow = hiderVisibleNow.Values.Any(v => v);
            bool detectionNow = anyVisibleNow && !_wasHiderVisiblePrev;
            int seekersSeeingNow = seekers.Count(s => hiders.Any(h => s.CanSee(h, World)));
            int hidersSeeingNow = hiders.Count(h => seekers.Any(s => h.CanSee(s, World)));

            foreach (var s in seekers)
            {
                var prev = _prevExploreCountsSeekers.TryGetValue(s, out var p) ? p : (0, 0);
                int afterPhysical = s.GetExploredCount();
                int afterVisual   = s.GetVisuallyExploredCount();
                int newPhysical = Math.Max(0, afterPhysical - prev.Item1);
                int newVisual   = Math.Max(0, afterVisual   - prev.Item2);

                bool seesAny = hiders.Any(h => s.CanSee(h, World));
                bool isSeenByOpponentNow = hiders.Any(h => h.CanSee(s, World));
                s.IsSeeingTarget = seesAny;

                if (seesAny)
                {
                    foreach (var t in hiders.Where(h => s.CanSee(h, World)))
                        _seekersBoard.ReportSeenTarget(t, t.Position, Timer);
                }

                float reward = ComputeSeekerRewardFor(s, newPhysical, newVisual, seesAny, isSeenByOpponentNow);

                if (detectionNow && seesAny && seekersSeeingNow > 0)
                {
                    reward += Config.Seeker.DetectBonus / seekersSeeingNow;
                }

                if (giveCatchBonus)
                    reward += perSeekerCatchBonus;

                long actionThisStep = _currentActionSeekers.TryGetValue(s, out var actNowS) ? actNowS : actCfg.Forward;
                bool isRotationAction = (actionThisStep == actCfg.TurnLeft || actionThisStep == actCfg.TurnRight ||
                                         actionThisStep == actCfg.ForwardLeft || actionThisStep == actCfg.ForwardRight);
                if (isRotationAction && newPhysical == 0 && newVisual == 0)
                {
                    float rotPen = MathF.Max(0f, Config.Seeker.RotationPenalty);
                    if (isSeenByOpponentNow)
                        rotPen *= MathF.Max(0f, Config.Seeker.RotationPenaltyWhenFleeFactor);
                    reward -= rotPen;
                }

                var nearestForS = GetNearestOpponent(s, hiders);
                float curDistS = Vector3.Distance(s.Position, nearestForS.Position);
                float lastDistS = _lastDistToNearestHider.TryGetValue(s, out var prevDistS) ? prevDistS : curDistS;

                bool noProgress = (curDistS > lastDistS - _noProgressDistanceEps) && newPhysical == 0 && newVisual == 0 && !seesAny;
                if (noProgress)
                {
                    float accum = _noProgressPenaltyAccumSeekers.TryGetValue(s, out var a) ? a : 0f;
                    accum = MathF.Min(Config.Seeker.NoProgressPenaltyMax, accum + MathF.Max(0f, Config.Seeker.NoProgressPenaltyStep));
                    _noProgressPenaltyAccumSeekers[s] = accum;
                    reward -= accum;
                }
                else
                {
                    _noProgressPenaltyAccumSeekers[s] = 0f;
                }

                _lastDistToNearestHider[s] = curDistS;

                var stateBefore = seekerStatesBefore[s];
                var stateAfter  = seekerStatesAfter[s];
                _seekerAgent.Store(stateBefore.ToArray(World.Size), actionThisStep, reward, stateAfter.ToArray(World.Size), isTerminalThisStep);
                _accSeekerReward += reward;

                _prevExploreCountsSeekers[s] = (afterPhysical, afterVisual);
            }

            _wasHiderVisiblePrev = anyVisibleNow;

            if (giveCatchBonus) _catchBonusGiven = true;

            foreach (var h in hiders)
            {
                bool visibleNow = hiderVisibleNow[h];

                foreach (var t in seekers.Where(s => h.CanSee(s, World)))
                    _hidersBoard.ReportSeenTarget(t, t.Position, Timer);

                long actionThisStep = _currentActionHiders.TryGetValue(h, out var actNowH) ? actNowH : actCfg.Forward;

                var nearestForH = GetNearestOpponent(h, seekers);
                float curDistH = Vector3.Distance(h.Position, nearestForH.Position);
                float lastDistH = _lastDistToNearestSeeker.TryGetValue(h, out var prevDistH) ? prevDistH : curDistH;

                float reward = ComputeHiderRewardFor(h, seekers, visibleNow);

                bool wasVisibleBefore = _wasHiderVisiblePrevMap.TryGetValue(h, out var wasVis) && wasVis;
                bool detectionNowH = visibleNow && !wasVisibleBefore;
                if (detectionNowH && hidersSeeingNow > 0)
                {
                    reward += Config.Hider.DetectBonus / hidersSeeingNow;
                }

                bool isRotationActionH =
                    (actionThisStep == actCfg.TurnLeft || actionThisStep == actCfg.TurnRight ||
                     actionThisStep == actCfg.ForwardLeft || actionThisStep == actCfg.ForwardRight);
                bool improved = curDistH > lastDistH; // increased distance = improved escape position
                if (isRotationActionH && !improved && visibleNow)
                {
                    float rotPenH = MathF.Max(0f, Config.Hider.RotationPenalty);
                    rotPenH *= MathF.Max(0f, Config.Hider.RotationPenaltyWhenFleeFactor);
                    reward -= rotPenH;
                }

                var stateBefore = hiderStatesBefore[h];
                var stateAfter  = hiderStatesAfter[h];
                _hiderAgent.Store(stateBefore.ToArray(World.Size), actionThisStep, reward, stateAfter.ToArray(World.Size), isTerminalThisStep);
                _accHiderReward += reward;

                _wasHiderVisiblePrevMap[h] = visibleNow;
            }

            // 8) Обучение (один вызов на роль)
            if (EnableLearning)
            {
                _seekerAgent.Learn();
                _hiderAgent.Learn();
            }

            // Синхронизация знаний команды
            MergeTeamKnowledge();

            // Метрики (оставляем в терминах «первой» пары для совместимости HUD)
            _framesInSession++;
            if (IsHiderVisible) _visibleFrames++;
            _sumDistance += Vector3.Distance(Seeker.Position, Hider.Position);

            // Раннее завершение при отсутствии прогресса (ориентируемся на «первую» пару)
            if (!IsHiderVisible)
            {
                float dist = Vector3.Distance(Seeker.Position, Hider.Position);
                float distDelta = MathF.Abs(dist - _lastDistanceForProgress);
                int visExplored = Seeker.GetVisuallyExploredCount();
                int visDelta = visExplored - _lastSeekerVisualExploredForProgress;

                if (distDelta < _noProgressDistanceEps && visDelta <= 0)
                {
                    _noProgressTimer += deltaTime;
                    if (_noProgressTimer >= _noProgressSeconds)
                    {
                        Restart();
                        return;
                    }
                }
                else
                {
                    _noProgressTimer = 0f;
                    _lastDistanceForProgress = dist;
                    _lastSeekerVisualExploredForProgress = visExplored;
                }
            }
            else
            {
                _noProgressTimer = 0f;
                _lastDistanceForProgress = Vector3.Distance(Seeker.Position, Hider.Position);
                _lastSeekerVisualExploredForProgress = Seeker.GetVisuallyExploredCount();
            }
        }
    }
}
