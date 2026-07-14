using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO
{
    // 1. Универсальный запрос от фронтенда
    public sealed class CharacterGenerationRequest
    {
        // Имя, пол, возраст, раса, шрамы, одежда, фон
        [JsonPropertyName("UserDescription")]
        public string UserDescription { get; set; } = string.Empty;         
        // false = генерируем только ЛИЦО (Шаг 1), true = генерируем ТЕЛО (Шаг 2)
        [JsonPropertyName("IsFullBody")]
        public bool IsFullBody { get; set; } = false;
        // URL или Base64 уже готового лица из Шага 1 (передаётся СТРОГО когда IsFullBody = true)
        [JsonPropertyName("FaceReferenceUrl")]
        public string? FaceReferenceUrl { get; set; }
        [JsonPropertyName("Model")]
        public string Model { get; set; } = "ComfyUI"; // Модель генерации (по умолчанию ComfyUI)
        [JsonPropertyName("AnalysisModel")]
        public string AnalysisModel { get; set; } = "gpt-oss:120b"; // Модель анализа (по умолчанию Realistic)
        [JsonPropertyName("SessionId")]
        public string? SessionId { get; set; }
    }

    // 2. Ответ от бэкенда
    public class CharacterGenerationResponse
    {
        // Промпт, который Кара или Мира составили по описанию автора
        [JsonPropertyName("GeneratedPrompt")]
        public string GeneratedPrompt { get; set; } = string.Empty;
        // Живой комментарий от ИИ-агента (на Шаге 1 говорит Мира, на Шаге 2 — Кара)
        [JsonPropertyName("AgentReview")]
        public string AgentReview { get; set; } = string.Empty;
        // Ссылка на сгенерированное изображение (на Шаге 1 — портрет, на Шаге 2 — финал с FaceSwap)
        [JsonPropertyName("ImageUrl")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}