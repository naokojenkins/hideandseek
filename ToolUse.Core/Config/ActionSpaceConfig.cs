using System;

namespace ToolUse.Core.Config
{
    /// <summary>
    /// Единый источник правды для пространства действий.
    /// Позволяет явно назначить индексы и общее количество действий.
    /// По умолчанию: 0=L, 1=R, 2=FWD, 3=FWD+L, 4=FWD+R, Count=5.
    /// </summary>
    public class ActionSpaceConfig
    {
        public int TurnLeft { get; set; } = 0;
        public int TurnRight { get; set; } = 1;
        public int Forward { get; set; } = 2;
        public int ForwardLeft { get; set; } = 3;
        public int ForwardRight { get; set; } = 4;
        public int Count { get; set; } = 5;
    }
}
