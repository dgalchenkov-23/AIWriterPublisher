using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public class DirectGenerationRequest
{
    public string? Mode { get; set; } // "raw-parse" или "layers"
    public string? RawPrompt { get; set; }
    public TechnicalSpecDto? TechnicalSpec { get; set; }
    public RenderSettingsDto? RenderSettings { get; set; }
    
    // Новые поля для сквозной поддержки Img2Img, если Катя редактирует прямо внутри пульта
    public string? BaseImageBase64 { get; set; } 
    public double? DenoisingStrength { get; set; }
}