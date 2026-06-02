namespace AIWriterPublisher.Api.Services
{
    public interface IImageGenerator
    {
        /// <summary>
        /// Генерирует изображение по готовому техническому промпту
        /// </summary>
        /// <param name="technicalPrompt">Промпт от Агента-Промптера</param>
        /// <param name="aspectRatio">Соотношение сторон (например, "2:3" для обложек)</param>
        /// <returns>URL сгенерированного изображения</returns>
        Task<string> GenerateImageAsync(string technicalPrompt, string aspectRatio = "2:3");
    }
}