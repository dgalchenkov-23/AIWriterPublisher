namespace AICoverStudio.Models.DTO
{
    // 1. Универсальный запрос от фронтенда
    public class CharacterGenerationRequest
    {
        // Имя, пол, возраст, раса, шрамы, одежда, фон
        public string UserDescription { get; set; } = string.Empty;         
        // false = генерируем только ЛИЦО (Шаг 1), true = генерируем ТЕЛО (Шаг 2)
        public bool IsFullBody { get; set; } = false;
        // URL или Base64 уже готового лица из Шага 1 (передаётся СТРОГО когда IsFullBody = true)
        public string? FaceReferenceUrl { get; set; }
    }

    // 2. Ответ от бэкенда
    public class CharacterGenerationResponse
    {
        // Промпт, который Кара или Мира составили по описанию автора
        public string GeneratedPrompt { get; set; } = string.Empty;
        // Живой комментарий от ИИ-агента (на Шаге 1 говорит Мира, на Шаге 2 — Кара)
        public string AgentReview { get; set; } = string.Empty;
        // Ссылка на сгенерированное изображение (на Шаге 1 — портрет, на Шаге 2 — финал с FaceSwap)
        public string ImageUrl { get; set; } = string.Empty;
    }
}