using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public sealed class DirectGenerationRequest
{
    // "self-prompt" (строгие слои) или "raw-parse" (магический поток)
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "self-prompt";

    // Заполняется только для режима "raw-parse"
    [JsonPropertyName("rawPrompt")]
    public string? RawPrompt { get; set; }

    [JsonPropertyName("technicalSpec")]
    public TechnicalSpecDto? TechnicalSpec { get; set; }

    [JsonPropertyName("render_settings")]
    public RenderSettingsDto? RenderSettings { get; set; }
}