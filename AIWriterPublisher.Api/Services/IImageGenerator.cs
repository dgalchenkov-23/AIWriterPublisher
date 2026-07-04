using AIWriterPublisher.Api.Models.DTO;
namespace AIWriterPublisher.Api.Services
{
    public interface IImageGenerator
    {
        /// <summary>
        /// Генерирует изображение по готовому техническому промпту
        /// </summary>
        /// <param name="technicalPrompt">Промпт от Агента-Промптера</param>
        /// <param name="aspectRatio">Соотношение сторон (например, "2:3" для обложек)</param>
        /// <param name="artArchitectorSpec">Спецификация арт-актора</param>
        /// <param name="analysisModel">Модель анализа</param>
        /// <returns>URL сгенерированного изображения</returns>
        Task<string> GenerateImageAsync(EngineeringSpecDto engineeringSpec, TechnicalSpecDto artArchitectorSpec = null, string aspectRatio = "2:3", string analysisModel = null);
    }
}