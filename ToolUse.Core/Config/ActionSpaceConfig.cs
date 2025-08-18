using System;

namespace ToolUse.Core.Config
{
    /// <summary>
    /// Единый источник правды для пространства действий.
    /// Позволяет явно назначить индексы и общее количество действий.
    /// По умолчанию: 0=L, 1=R, 2=FWD, 3=FWD+L, 4=FWD+R, 5=IDLE, Count=6.
    /// </summary>
    public class ActionSpaceConfig
    {
        public int TurnLeft { get; set; } = 0;
        public int TurnRight { get; set; } = 1;
        public int Forward { get; set; } = 2;
        public int ForwardLeft { get; set; } = 3;
        public int ForwardRight { get; set; } = 4;

        /// <summary>
        /// Агент остаётся на месте (без поворота и перемещения).
        /// </summary>
        public int Idle { get; set; } = 5;

        public int Count { get; set; } = 6;
    }
}
