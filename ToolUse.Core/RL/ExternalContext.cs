namespace ToolUse.Core.RL
{
    /// <summary>
    /// Внешний контекст, устанавливаемый окружением на один шаг.
    /// Используется для передачи мгновенных флагов, не входящих напрямую в наблюдение.
    /// </summary>
    public class ExternalContext
    {
        /// <summary> Агент является Hider. </summary>
        public bool IsHider { get; set; }

        /// <summary> Hider находится в поле зрения Seeker (любого/основного). </summary>
        public bool IsHiderSeen { get; set; }
    }
}
