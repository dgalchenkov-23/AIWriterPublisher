using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Agents.VisionEditorAgent
{
    /// <summary>
    /// Vision-агент на базе Nex-N2-Pro. 
    /// Анализирует референс и "Магический поток" для точечной редактуры (Z-Image).
    /// </summary>
    public class VisionEditorAgent
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VisionEditorAgent> _logger;

        public VisionEditorAgent(HttpClient httpClient, IConfiguration configuration, ILogger<VisionEditorAgent> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Анализирует изображение и текст пользователя для создания инструкций по редактированию.
        /// </summary>
        public async Task<ComfyUiImg2ImgSpecDto> AnalyzeEditRequestAsync(string imagePath, string magicStream)
        {
            _logger.LogInformation("Vision-анализ референса {Path} с запросом: {Query}", imagePath, magicStream);

            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Файл референса не найден для Vision-анализа", imagePath);

            // 1. Подготовка изображения (Base64 для OpenRouter)
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);

            // 2. Формирование запроса к Nex-N2-Pro
            string systemPrompt = BuildSystemPrompt();
            string userMessage = $"Инструкция пользователя: {magicStream}";

            try
            {
                string rawResponse = await CallVisionApiAsync(systemPrompt, userMessage, base64Image);
                
                // Десериализация ответа в DTO для ComfyUI
                var result = JsonSerializer.Deserialize<ComfyUiImg2ImgSpecDto>(rawResponse, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (result is null)
                {
                    throw new InvalidOperationException("Vision API вернул пустой ответ.");
                }

                // Сохраняем исходный путь, так как модель его не знает
                result.ReferenceImagePath = imagePath;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка Vision-анализа в VisionEditorAgent");
                return new ComfyUiImg2ImgSpecDto { PromptOverride = magicStream, ReferenceImagePath = imagePath };
            }
        }

        private string BuildSystemPrompt()
        {
            return @"Ты — экспертный Vision-агент системы AI Cover Studio. 
            Твоя задача: изучить присланную обложку и применить к ней текстовые правки пользователя.

            ПРАВИЛА:
            1. Опиши изменения на техническом английском языке для модели Flux.
            2. Сосредоточься ТОЛЬКО на вносимых изменениях (Inpainting/Img2Img).
            3. Если пользователь просит что-то удалить, опиши, чем заполнить это место.
            4. Если изменения локализованы (конкретный объект/область), опиши эту область текстом для создания маски.

            Верни СТРОГО JSON-объект по схеме:
            {
            ""enable_inpainting"": true/false,
            ""denoising_strength"": 0.3-0.8,
            ""prompt_override"": ""прямое описание того, что ДОЛЖНО появиться на месте маски, на английском, 5-15 слов"",
            ""mask_description"": ""краткое описание ТОЛЬКО объекта/зоны для замены, 1-3 слова (например: sword, background, chair, sky)""
            }

            ПРАВИЛА:
            - mask_description должен быть максимально кратким (1-3 слова) и описывать ТОЛЬКО заменяемый объект
            - Не добавляй контекст (кто держит, где находится, в какой позе)
            - Если изменения затрагивают всё изображение, оставь mask_description пустой строкой
            - prompt_override должен описывать, что именно нарисовать на месте маски
            - denoising_strength: 0.3-0.5 для лёгких правок, 0.6-0.8 для сильных изменений
            ";
        }

        private async Task<string> CallVisionApiAsync(string system, string user, string base64Image)
        {
            try
            {
                _logger.LogInformation("Отправляем запрос в Vision API с изображением размером {Size} байт", base64Image.Length * 3 / 4);
                string baseUrl = _configuration["AIServices:NEXAGI:BaseUrl"] ?? "https://openrouter.ai/api/v1";
                string apiKey = _configuration["AIServices:NEXAGI:ApiKey"] ?? "";
                string modelName = _configuration["AIServices:NEXAGI:Model"] ?? "nex-agi/nex-n2-pro:free";

                _httpClient.DefaultRequestHeaders.Remove("Authorization");

                string cleanApiKey = apiKey?.Trim() ?? "";

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");

                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                    

                var requestBody = new
                {
                    model = modelName,
                    messages = new object[]
                    {
                        new { role = "system", content = system },
                        new { role = "user", content = new object[] 
                            {
                                new { type = "text", text = user },
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                            }
                        }
                    },
                    temperature = 0.2
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var httpResponse = await _httpClient.SendAsync(requestMessage);
                httpResponse.EnsureSuccessStatusCode();

                var responseString = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("Получен ответ от Vision API: {Response}", responseString);
                using var doc = JsonDocument.Parse(responseString);

                var root = doc.RootElement;
                _logger.LogInformation("Получен ответ от Vision API");
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    string rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
                    _logger.LogInformation("Получен ответ от Vision API: {Response}", rawJsonText);
                    return rawJsonText;
                }
                else
                {
                    _logger.LogWarning("Ответ от Vision API не содержит ожидаемых данных: {Response}", responseString);
                    throw new Exception("Неверный ответ от Vision API: отсутствуют choices");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вызове Vision API в VisionEditorAgent");
                throw;
            }
        }
    }
}