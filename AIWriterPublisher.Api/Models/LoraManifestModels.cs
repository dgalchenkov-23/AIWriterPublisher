using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models
{
    /// <summary>
    /// Коренной контейнер для всего списка доступных LoRA в системе.
    /// Используется для десериализации lora_manifest.json
    /// </summary>
    public class LoraRootManifest
    {
        [JsonPropertyName("AvailableLoras")]
        public List<LoraDefinition> AvailableLoras { get; set; } = new();
    }

    /// <summary>
    /// Полное описание LoRA-модели со всеми метаданными для бэкенда и ИИ.
    /// </summary>
    public class LoraDefinition
    {
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public double DefaultWeight { get; set; } = 0.85;
        public string TriggerWords { get; set; } = string.Empty;
        
        // Категории: "Enhancer", "GenreBase", "Modifier"
        public string Category { get; set; } = "Modifier"; 
        public string Description { get; set; } = string.Empty;
        
        // Список жанров, к которым применима (например: ["Fantasy", "LitRPG"] или ["All"])
        public List<string> ApplicableGenres { get; set; } = new();
    }
}