using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public sealed class DirectGenerationResponse
{
    // Можно вернуть ссылку или dataURL/base64 — фронт ленте всё равно, главное чтобы было image-src.
    [JsonPropertyName("image_base64")]
    public string ImageBase64 { get; set; } = string.Empty;

    // Заполняется только если режим был "raw-parse" и бэк распарсил свободный текст.
    [JsonPropertyName("parsed_spec")]
    public TechnicalSpecDto? ParsedSpec { get; set; }
}