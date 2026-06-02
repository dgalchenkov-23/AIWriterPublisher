using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public sealed class TechnicalSpecDto
{
    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    // фронт: technical_spec.style_and_light
    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("composition")]
    public string Composition { get; set; } = string.Empty;

    [JsonPropertyName("negative_constraints")]
    public string NegativeConstraints { get; set; } = string.Empty;
}

public class GenerationPipelineRequest
{
    public string Genre { get; set; }
    public string Mode { get; set; } = "Direct Prompt"; // Режим «Творец»
    public required TechnicalSpecDto TechnicalSpec { get; set; }
}