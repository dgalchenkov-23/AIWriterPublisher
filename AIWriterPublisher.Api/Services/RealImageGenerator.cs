using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIWriterPublisher.Api.Models.DTO;
using Microsoft.Extensions.Configuration;

namespace AIWriterPublisher.Api.Services
{
    public class RealImageGenerator : IImageGenerator
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public RealImageGenerator(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        /// <summary>
        /// Реализация интерфейсного метода. Маппит пропорции и шлет запрос во Flux.
        /// </summary>
        public async Task<string> GenerateImageAsync(EngineeringSpecDto engineeringSpec, TechnicalSpecDto artArchitectorSpec, string analysisModel, string aspectRatio = "2:3")
        {
            // Маппим привычные Кате пропорции обложки в пиксели, которые понимает Flux.1-dev
            // Базируемся на золотом стандарте ~1 мегапиксель для Flux
            string width = "1024";
            string height = "1024";

            if (aspectRatio == "2:3")
            {
                width = "832";
                height = "1248"; // Идеальное вертикальное разрешение под книжную обложку
            }
            else if (aspectRatio == "3:2")
            {
                width = "1248";
                height = "832";
            }
            else if (aspectRatio == "16:9")
            {
                width = "1344";
                height = "768";
            }

            // Вызываем наш прямой enterprise-пайплайн к Hugging Face
            // return await GenerateFluxViaHuggingFaceAsync(technicalPrompt, width, height);
            return await GenerateFluxViaPollinationsAsync(engineeringSpec.PositivePrompt, width, height, artArchitectorSpec);
        }

        /// <summary>
        /// Выделенный метод для работы с Hugging Face Inference API.
        /// </summary>
        public async Task<string> GenerateFluxViaHuggingFaceAsync(string prompt, string width, string height)
        {
            string? hfToken = _configuration["HuggingFace:Token"];
            if (string.IsNullOrEmpty(hfToken))
            {
                throw new InvalidOperationException(
                    "Критическая ошибка: Hugging Face Token не найден в конфигурации (secrets.json / appsettings.json).");
            }

            string modelUrl = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell";

            using var request = new HttpRequestMessage(HttpMethod.Post, modelUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hfToken);

            var payload = new
            {
                inputs = prompt,
                parameters = new
                {
                    width = int.Parse(width),
                    height = int.Parse(height),
                    num_inference_steps = 28 // Оптимальный шаг для проработки деталей во Flux Dev
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Console.WriteLine($"[HF Engine] Отправка запроса на Flux.1-dev. Пропорции в пикселях: {width}x{height}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[HF Engine] Ошибка API: {response.StatusCode} | {errorContent}");
                throw new Exception($"Hugging Face API вернул ошибку: {response.StatusCode} - {errorContent}");
            }

            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
            
            // Возвращаем base64 строку. Контроллер обернет её или отдаст на фронт как надо.
            return Convert.ToBase64String(imageBytes);
        }

        /// <summary>
        /// Выделенный метод для работы с Hugging Face Inference API.
        /// </summary>
        public async Task<string> GenerateFluxViaPollinationsAsync(string prompt, string width, string height, TechnicalSpecDto artArchitectorSpec = null)
        {
            try
            {
                // 1. Формируем единый промпт. В Pollinations ограничения подмешиваются через текстовое отрицание.
                // string combinedPrompt = string.IsNullOrWhiteSpace(negativePrompt) 
                //     ? fullPrompt 
                //     : $"{fullPrompt}. Avoid, negative: {negativePrompt}";

                string encodedPrompt = Uri.EscapeDataString(prompt);

                // Разбираем размер (например, "1024x1024" -> width=1024, height=1024)
                // string[] dimensions = size.Split('x');
                // string width = dimensions.Length > 0 ? dimensions[0] : "1024";
                // string height = dimensions.Length > 1 ? dimensions[1] : "1024";

                // Рандомный seed, чтобы генерации всегда были уникальными и не кэшировались сервером
                int seed = new Random().Next(1, 999999);

                // 2. Собираем URL. Задаем модель flux для качественного инференса
                string imageUrl = $"https://image.pollinations.ai/p/{encodedPrompt}?model=zimage&width={width}&height={height}&seed={seed}&enhance=true&safe=true";
                // string imageUrl = $"https://gen.pollinations.ai/image/{encodedPrompt}?model=flux&width={width}&height={height}&seed={seed}&enhance=true&safe=true";

                // 3. Выполняем запрос к Pollinations
                HttpResponseMessage response = await _httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();

                // Читаем бинарник картинки
                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                
                // Конвертируем в Base64 строку
                string base64Image = Convert.ToBase64String(imageBytes);

                // Возвращаем готовый Data URL, который фронтенд съест напрямую в src
                return $"data:image/jpeg;base64,{base64Image}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pollinations Error]: {ex.Message}");
                throw new Exception($"Ошибка при генерации изображения через Pollinations AI: {ex.Message}", ex);
            }
        }
    }
}