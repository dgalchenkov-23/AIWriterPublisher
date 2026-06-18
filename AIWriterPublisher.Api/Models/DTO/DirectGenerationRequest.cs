using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public class DirectGenerationRequest
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; } // "raw-parse" или "layers"
    [JsonPropertyName("raw_prompt")]
    public string? RawPrompt { get; set; }
    [JsonPropertyName("analysis_model")]
    public string? AnalysisModel { get; set; }
    [JsonPropertyName("has_reference")]
    public bool? HasReference { get; set; }
    [JsonPropertyName("technical_spec")]
    public TechnicalSpecDto? TechnicalSpec { get; set; }
    [JsonPropertyName("render_settings")]
    public RenderSettingsDto? RenderSettings { get; set; }
    // Новые поля для сквозной поддержки Img2Img, если Катя редактирует прямо внутри пульта
    [JsonPropertyName("base_image_base64")]
    public string? BaseImageBase64 { get; set; } 
    [JsonPropertyName("denoising_strength")]
    public double? DenoisingStrength { get; set; }
}