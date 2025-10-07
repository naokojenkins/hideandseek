using System;
using System.Text;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using HideAndSeek.Core.IO;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Логика сбора метрик/логирования и персистентности вынесена сюда.
    public partial class Simulation3D
    {
        private void AppendSessionMetrics()
        {
            try
            {
                float visibilityRatio = _framesInSession > 0 ? (float)_visibleFrames / _framesInSession : 0f;
                float avgDistance = _framesInSession > 0 ? _sumDistance / _framesInSession : 0f;
                MetricsRecorder.Instance.RecordEpisode(
                    totalSession: TotalSessions,
                    sessionTime: Timer,
                    caught: _isHiderCaught,
                    visibilityRatio: visibilityRatio,
                    avgDistance: avgDistance,
                    seekerPhysical: Seeker.GetExploredCount(),
                    seekerVisual: Seeker.GetVisuallyExploredCount(),
                    seekerTotal: Seeker.GetTotalExploredCount(),
                    accSeekerReward: _accSeekerReward,
                    accHiderReward: _accHiderReward);
            }
            catch { }
        }

        private void UpdateScores(float deltaTime)
        {
            if (IsHiderVisible)
            {
                if (Config.Seeker.EnableHudVisibilityPoints)
                    SeekerScore += (Config.Seeker.PointsPerSecondWhenHiderVisible * Config.Seeker.VisibilityPointsScaleHUD) * deltaTime;
                if (Config.Hider.EnableHudVisibilityPoints)
                    HiderScore  += (Config.Hider.PointsPerSecondWhenVisible      * Config.Hider.VisibilityPointsScaleHUD) * deltaTime;
            }
            else
            {
                if (Config.Hider.EnableHudVisibilityPoints)
                    HiderScore  += (Config.Hider.PointsPerSecondWhenHidden      * Config.Hider.VisibilityPointsScaleHUD) * deltaTime;
                if (Config.Seeker.EnableHudVisibilityPoints)
                    SeekerScore += (Config.Seeker.PointsPerSecondWhenHiderHidden * Config.Seeker.VisibilityPointsScaleHUD) * deltaTime;
            }
            if (float.IsNaN(SeekerScore) || float.IsInfinity(SeekerScore))
                throw new Exception($"[NaN/Inf] SeekerScore: {SeekerScore}");
            if (float.IsNaN(HiderScore) || float.IsInfinity(HiderScore))
                throw new Exception($"[NaN/Inf] HiderScore: {HiderScore}");
        }

        private static void LoadTotalSessions()
        {
            try
            {
                string directory = Path.GetDirectoryName(SessionCounterFile);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (!File.Exists(SessionCounterFile))
                {
                    TotalSessions = 0;
                    return;
                }
                string json = File.ReadAllText(SessionCounterFile, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<SessionCounterData>(json, JsonSettings);
                TotalSessions = data?.TotalSessions ?? 0;
            }
            catch { TotalSessions = 0; }
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
                File.WriteAllText(SessionCounterFile, json, Encoding.UTF8);
            }
            catch { }
        }

        // Централизованное логирование числовых проблем (NaN/Inf)
        private void LogNumericIssue(string tag, string details)
        {
            try
            {
                string logsDir = PathService.GetLogsDirectory();
                string file = Path.Combine(logsDir, "numeric_issues.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {details}";
                File.AppendAllLines(file, new[] { line }, Encoding.UTF8);
            }
            catch { }
        }

        private static string FormatVec(Vector3 v)
        {
            bool bad = !(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            return $"(X={v.X}, Y={v.Y}, Z={v.Z}){(bad ? " [BAD]" : "")}";
        }

        // Подробная диагностика состояния симуляции
        public void DumpDiagnostics(Exception? ex = null)
        {
            try
            {
                string logsDir = PathService.GetLogsDirectory();
                string file = Path.Combine(logsDir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");

                var sb = new StringBuilder(4096);
                sb.AppendLine("==== Simulation3D Diagnostics ====");
                sb.AppendLine($"Time: {DateTime.Now:O}");
                if (ex != null)
                {
                    sb.AppendLine("Exception:");
                    sb.AppendLine(ex.ToString());
                }

                sb.AppendLine();
                sb.AppendLine($"Session: {Session}  TotalSessions: {TotalSessions}");
                sb.AppendLine($"Timer: {Timer:F6}  SessionDurationSeconds: {sessionDurationSeconds:F6}");
                sb.AppendLine($"Scores: Seeker={SeekerScore:F6} Hider={HiderScore:F6} Exploration={ExplorationScore:F6}");
                sb.AppendLine($"Flags: IsHiderVisible={IsHiderVisible} IsHiderCaught={_isHiderCaught} CaughtFrames={_caughtFrames}");
                sb.AppendLine($"VisibilityCheck: last={_lastVisibilityCheck:F6} interval={_visibilityCheckInterval:F6}");
                sb.AppendLine($"NoProgress: timer={_noProgressTimer:F6} lastDist={_lastDistanceForProgress:F6} eps={_noProgressDistanceEps:F6} seconds={_noProgressSeconds:F6}");
                sb.AppendLine($"ActionRepeat={_actionRepeat}");

                // Конфиг-критичные параметры
                try
                {
                    int framesThreshold = Config?.EffectiveFramesForCatch ?? 1;
                    sb.AppendLine("Config:");
                    sb.AppendLine($"  TimeScale={Config?.TimeScale}");
                    sb.AppendLine($"  FramesForCatch={Config?.FramesForCatch} -> effective={framesThreshold}");
                    sb.AppendLine($"  Seeker.RotationStepDegrees={Config?.Seeker.RotationStepDegrees}");
                    sb.AppendLine($"  Hider.RotationStepDegrees={Config?.Hider.RotationStepDegrees}");
                }
                catch (Exception cex)
                {
                    sb.AppendLine($"[WARN] Failed to read Config details: {cex.Message}");
                }

                // Мир и камера
                try
                {
                    sb.AppendLine();
                    sb.AppendLine("World/Camera:");
                    sb.AppendLine($"  World.Size={World?.Size}");
                    sb.AppendLine($"  Camera.Position={FormatVec(_camera.Position)}");
                    sb.AppendLine($"  Camera.Target={FormatVec(_camera.Target)}");
                    sb.AppendLine($"  Camera.Up={FormatVec(_camera.Up)}  FovY={_camera.FovY}");
                }
                catch (Exception wex)
                {
                    sb.AppendLine($"[WARN] Failed to dump world/camera: {wex.Message}");
                }

                // Агенты
                try
                {
                    var seekers = (Seekers != null && Seekers.Count > 0) ? Seekers : new List<Agent3D> { Seeker };
                    var hiders  = (Hiders  != null && Hiders.Count  > 0) ? Hiders  : new List<Agent3D> { Hider  };

                    sb.AppendLine();
                    sb.AppendLine($"Seekers ({seekers.Count}):");
                    for (int i = 0; i < seekers.Count; i++)
                    {
                        var s = seekers[i];
                        sb.AppendLine($"  S[{i}] Pos={FormatVec(s.Position)} Dir={s.Direction} IsSeeingTarget={s.IsSeeingTarget}");
                    }

                    sb.AppendLine($"Hiders ({hiders.Count}):");
                    for (int i = 0; i < hiders.Count; i++)
                    {
                        var h = hiders[i];
                        sb.AppendLine($"  H[{i}] Pos={FormatVec(h.Position)} Dir={h.Direction} IsSeeingTarget={h.IsSeeingTarget}");
                    }
                }
                catch (Exception aex)
                {
                    sb.AppendLine($"[WARN] Failed to dump agents: {aex.Message}");
                }

                // Метрики
                sb.AppendLine();
                sb.AppendLine("Metrics:");
                sb.AppendLine($"  FramesInSession={_framesInSession}  VisibleFrames={_visibleFrames}  SumDistance={_sumDistance:F6}");
                sb.AppendLine($"  AccSeekerReward={_accSeekerReward:F6}  AccHiderReward={_accHiderReward:F6}");

                // Внутренние карты (только размеры)
                try
                {
                    sb.AppendLine();
                    sb.AppendLine("Internal maps:");
                    sb.AppendLine($"  _prevStateSeekers={_prevStateSeekers.Count}");
                    sb.AppendLine($"  _prevStateHiders={_prevStateHiders.Count}");
                    sb.AppendLine($"  _prevActionSeekers={_prevActionSeekers.Count}");
                    sb.AppendLine($"  _prevActionHiders={_prevActionHiders.Count}");
                    sb.AppendLine($"  _repeatLeftSeekers={_repeatLeftSeekers.Count}");
                    sb.AppendLine($"  _repeatLeftHiders={_repeatLeftHiders.Count}");
                    sb.AppendLine($"  _currentActionSeekers={_currentActionSeekers.Count}");
                    sb.AppendLine($"  _currentActionHiders={_currentActionHiders.Count}");
                    sb.AppendLine($"  _lastDistToNearestSeeker={_lastDistToNearestSeeker.Count}");
                    sb.AppendLine($"  _lastDistToNearestHider={_lastDistToNearestHider.Count}");
                    sb.AppendLine($"  _prevExploreCountsSeekers={_prevExploreCountsSeekers.Count}");
                }
                catch (Exception mex)
                {
                    sb.AppendLine($"[WARN] Failed to dump maps: {mex.Message}");
                }

                // Сохранение
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);

                try
                {
                    Console.WriteLine($"[DEBUG] Диагностика сохранена: {file}");
                }
                catch { }
            }
            catch (Exception ex2)
            {
                try
                {
                    Console.WriteLine($"[ERROR] Не удалось сохранить диагностику: {ex2}");
                }
                catch { }
            }
        }

        public static void ForceSaveTotalSessions() => SaveTotalSessions();

        // Public API to reset the global total sessions counter and persist it
        public static void ResetTotalSessions()
        {
            try
            {
                TotalSessions = 0;
                SaveTotalSessions();
            }
            catch { }
        }
    }
}
