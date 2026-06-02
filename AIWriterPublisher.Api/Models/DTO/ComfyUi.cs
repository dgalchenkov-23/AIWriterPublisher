using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

// Ответ на отправку графа в очередь
public sealed class ComfyQueueResponse
{
    [JsonPropertyName("prompt_id")]
    public string PromptId { get; set; } = string.Empty;
    
    [JsonPropertyName("number")]
    public int Number { get; set; }
}

// Упрощенная структура для проверки статуса через /history
public sealed class ComfyHistoryResponse
{
    // Нам важно лишь узнать, появился ли ключ с нашим prompt_id в ответе
    // API возвращает словарь вида: { "prompt_id": { "outputs": { "node_id": { "images": [...] } } } }
}