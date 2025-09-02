using System;

namespace HideAndSeek.Core.RaylibThreeD
{
    // Топ-уровневый класс для сериализации счётчика сессий
    internal class SessionCounterData
    {
        public int TotalSessions { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}
