using System.Threading.Tasks;

namespace AIWriterPublisher.Api.Services
{
    public class MockImageGenerator : IImageGenerator
    {
        public async Task<string> GenerateImageAsync(string technicalPrompt, string aspectRatio = "2:3")
        {
            // Имитируем бурную деятельность нейросети (задержка 3 секунды)
            await Task.Delay(3000);

            // Возвращаем плейсхолдер с наложенным текстом промпта, чтобы видеть, что система работает.
            // Сервис picsum.photos или via.placeholder отлично подходят для тестов UI.
            return $"https://picsum.photos/seed/{Guid.NewGuid()}/600/900";
        }
    }
}