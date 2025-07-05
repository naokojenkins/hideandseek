using System;
using System.IO;
using Newtonsoft.Json;

namespace ToolUse.Core.Config
{
    /// <summary>
    /// Класс конфигурации для настройки параметров игры
    /// </summary>
    public class GameConfig
    {
        // Общие параметры
        public float SessionDurationSeconds { get; set; } = 600f;
        public int FramesForCatch { get; set; } = 60; // Сколько кадров должен быть виден hider, чтобы считаться пойманным

        // Параметры для Seeker
        public SeekerConfig Seeker { get; set; } = new SeekerConfig();

        // Параметры для Hider
        public HiderConfig Hider { get; set; } = new HiderConfig();

        // Статический метод для загрузки конфигурации из файла
        public static GameConfig Load(string filePath = "game_config.json")
        {
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonConvert.DeserializeObject<GameConfig>(json);
                    return config ?? new GameConfig();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка загрузки конфигурации: {ex.Message}");
                    return new GameConfig();
                }
            }
            else
            {
                // Создаем файл с настройками по умолчанию
                var defaultConfig = new GameConfig();
                try
                {
                    string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка создания файла конфигурации: {ex.Message}");
                }
                return defaultConfig;
            }
        }

        // Сохранение конфигурации в файл
        public void Save(string filePath = "game_config.json")
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения конфигурации: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Параметры для настройки Seeker (Искателя)
    /// </summary>
    public class SeekerConfig
    {
        // Базовые очки за секунду, когда Hider видим
        public float PointsPerSecondWhenHiderVisible { get; set; } = 1.0f;

        // Базовые очки за секунду, когда Hider не видим
        public float PointsPerSecondWhenHiderHidden { get; set; } = 0.0f;

        // Награда RL-агенту, когда Hider видим
        public float RewardWhenHiderVisible { get; set; } = 1.0f;

        // Награда RL-агенту, когда Hider не видим
        public float RewardWhenHiderHidden { get; set; } = -0.1f;

        // Параметры исследования
        public float ExplorationBonusPerCell { get; set; } = 0.1f; // Бонус за каждую новую исследованную клетку
        public float ExplorationScoreMultiplier { get; set; } = 1.0f; // Множитель для очков за исследование
    }

    /// <summary>
    /// Параметры для настройки Hider (Прячущегося)
    /// </summary>
    public class HiderConfig
    {
        // Базовые очки за секунду, когда Hider видим
        public float PointsPerSecondWhenVisible { get; set; } = 0.0f;

        // Базовые очки за секунду, когда Hider не видим
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;

        // Награда RL-агенту, когда Hider видим
        public float RewardWhenVisible { get; set; } = -1.0f;

        // Награда RL-агенту, когда Hider не видим
        public float RewardWhenHidden { get; set; } = 0.1f;
    }
}
