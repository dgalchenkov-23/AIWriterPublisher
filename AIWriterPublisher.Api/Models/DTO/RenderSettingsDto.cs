using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

public sealed class RenderSettingsDto
{
    // фронт: render_settings.provider ("huggingface" | "pollination")
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "huggingface";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "black-forest-labs/FLUX.1-dev";

    // фронт: render_settings.canvas_preset ("1024x1024" | "512x768")
    [JsonPropertyName("canvas_preset")]
    public string CanvasPreset { get; set; } = "1024x1024";

    // фронт: render_settings.aspect_ratio (например "1:1", "2:3")
    [JsonPropertyName("aspect_ratio")]
    public string AspectRatio { get; set; } = "1:1";

    // фронт: render_settings.dimensions
    [JsonPropertyName("dimensions")]
    public DimensionsDto Dimensions { get; set; } = new();
}

public sealed class DimensionsDto
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1024;
}

