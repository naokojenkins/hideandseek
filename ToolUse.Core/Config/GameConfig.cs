
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
        public bool ShowSessionTime { get; set; } = true;
        public string TimeFormat { get; set; } = "{0:F1}s / {1:F0}s";
        public int FramesForCatch { get; set; } = 60; // Сколько кадров должен быть виден hider, чтобы считаться пойманным
        
        // Параметры размера игрового поля
        public WorldConfig World { get; set; } = new WorldConfig();
        
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
    /// Параметры размера игрового поля для 2D и 3D версий
    /// </summary>
    public class WorldConfig
    {
        // Размер поля для 2D версии
        public int GridSize2D { get; set; } = 40;
        
        // Размер поля для 3D версии
        public int GridSize3D { get; set; } = 40;
        
        // Размер клетки для 2D версии (в пикселях)
        public int CellSize2D { get; set; } = 16;
        
        // Размер клетки для 3D версии (в единицах мира)
        public float CellSize3D { get; set; } = 1.0f;
        
        // Высота стен в 3D версии
        public float WallHeight3D { get; set; } = 2.0f;
        
        // Параметры генерации комнат
        public int RoomSize { get; set; } = 8; // Размер комнаты в клетках
        
        // Настройки отображения
        public bool ShowGrid { get; set; } = true;
        public bool ShowShadows { get; set; } = true;
    }
    
    /// <summary>
    /// Параметры для настройки Seeker (Искателя)
    /// </summary>
    public class SeekerConfig
    {
        // Базовые очки за секунду, когда Hider видим
        public float PointsPerSecondWhenHiderVisible { get; set; } = 10.0f;
        
        // Базовые очки за секунду, когда Hider не видим
        public float PointsPerSecondWhenHiderHidden { get; set; } = 0.0f;
        
        // Награда RL-агенту, когда Hider видим
        public float RewardWhenHiderVisible { get; set; } = 10.0f;
        
        // Награда RL-агенту, когда Hider не видим
        public float RewardWhenHiderHidden { get; set; } = -0.05f;
        
        // Параметры исследования
        public float ExplorationBonusPerCell { get; set; } = 1.0f; // Бонус за каждую новую исследованную клетку
        public float ExplorationScoreMultiplier { get; set; } = 2.0f; // Множитель для очков за исследование
        
        // Награда за близость к hider'у
        public bool ProximityRewardEnabled { get; set; } = true;
        public float ProximityRewardMultiplier { get; set; } = 5.0f; // Множитель награды за близость
        public float MaxProximityDistance { get; set; } = 15.0f; // Максимальное расстояние для награды за близость
        
        // Награда за движение (мотивирует не стоять на месте)
        public bool MovementRewardEnabled { get; set; } = true;
        public float MovementRewardPerSecond { get; set; } = 0.1f;
        
        // Штраф за бездействие
        public bool IdlePenaltyEnabled { get; set; } = true;
        public float IdlePenaltyPerSecond { get; set; } = -0.2f;
    }
    
    /// <summary>
    /// Параметры для настройки Hider (Прячущегося)
    /// </summary>
    public class HiderConfig
    {
        // Базовые очки за секунду, когда Hider видим
        public float PointsPerSecondWhenVisible { get; set; } = -2.0f;
        
        // Базовые очки за секунду, когда Hider не видим
        public float PointsPerSecondWhenHidden { get; set; } = 1.0f;
        
        // Награда RL-агенту, когда Hider видим
        public float RewardWhenVisible { get; set; } = -10.0f;
        
        // Награда RL-агенту, когда Hider не видим
        public float RewardWhenHidden { get; set; } = 0.2f;
        
        // Награда за поддержание дистанции от seeker'а
        public bool DistanceRewardEnabled { get; set; } = true;
        public float DistanceRewardMultiplier { get; set; } = 2.0f;
        public float MinSafeDistance { get; set; } = 10.0f; // Минимальная безопасная дистанция
    }
}