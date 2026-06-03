using System.Text.Json.Serialization;

namespace AIWriterPublisher.Api.Models.DTO;

/// <summary>
/// Техническая спецификация для генерации (Инженерный пульт / Декомпозиция)
/// </summary>
public sealed class TechnicalSpecDto
{
    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    // Маппим оба возможных варианта от фронта (style и style_and_light) на одно свойство
    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("composition")]
    public string Composition { get; set; } = string.Empty;

    [JsonPropertyName("negative_constraints")]
    public string NegativeConstraints { get; set; } = string.Empty;
}

/// <summary>
/// Параметры типографики для Canvas Studio (Слой текста)
/// </summary>
public sealed class TypographySpecDto
{
    [JsonPropertyName("title_text")]
    public string TitleText { get; set; } = string.Empty;

    [JsonPropertyName("author_text")]
    public string AuthorText { get; set; } = string.Empty;

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Montserrat";

    [JsonPropertyName("font_size")]
    public int FontSize { get; set; } = 48;

    [JsonPropertyName("text_color")]
    public string TextColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("safe_zones")]
    public SafeZonesDto SafeZones { get; set; } = new();
}

public sealed class SafeZonesDto
{
    [JsonPropertyName("top")]
    public int Top { get; set; } = 100;

    [JsonPropertyName("bottom")]
    public int Bottom { get; set; } = 850;
}

/// <summary>
/// Параметры для локальной ComfyUI / Z-Image точечной коррекции (Img2Img / Inpainting)
/// </summary>
public sealed class ComfyUiImg2ImgSpecDto
{
    [JsonPropertyName("enable_inpainting")]
    public bool EnableInpainting { get; set; } = false;

    [JsonPropertyName("denoising_strength")]
    public double DenoisingStrength { get; set; } = 0.45;

    [JsonPropertyName("prompt_override")]
    public string PromptOverride { get; set; } = string.Empty;

    [JsonPropertyName("reference_image_path")]
    public string ReferenceImagePath { get; set; } = string.Empty;

    [JsonPropertyName("mask_image_path")]
    public string MaskImagePath { get; set; } = string.Empty;
}

/// <summary>
/// Единый входящий запрос к API генерации обложек
/// </summary>
public sealed class GenerationPipelineRequest
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("book_title")]
    public string BookTitle { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Direct Prompt"; // Например: "Direct Prompt", "Inpainting", "Concept Search"

    [JsonPropertyName("technical_spec")]
    public required TechnicalSpecDto TechnicalSpec { get; set; }

    [JsonPropertyName("typography_spec")]
    public TypographySpecDto TypographySpec { get; set; } = new();

    [JsonPropertyName("comfyui_img2img_spec")]
    public ComfyUiImg2ImgSpecDto ComfyUiImg2ImgSpec { get; set; } = new();
}