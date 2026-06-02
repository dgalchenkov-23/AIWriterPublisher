namespace AIWriterPublisher.Api.Models
{
    public class CoverGenerationRequest
    {
        public string Genre { get; set; } = string.Empty;
        public string VisualElements { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public string russianSpec { get; set; } = string.Empty;
    }

    public class CoverGenerationResponse
    {
        public string TechnicalPrompt { get; set; } = string.Empty; // Что наинженерил второй агент
        public string ImageUrl { get; set; } = string.Empty;       // Ссылка на готовую картинку от Mock-генератора
    }
}