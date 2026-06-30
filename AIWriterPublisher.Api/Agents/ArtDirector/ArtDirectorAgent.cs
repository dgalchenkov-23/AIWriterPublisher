using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIWriterPublisher.Api.Models;

namespace AIWriterPublisher.Api.Agents.ArtDirector
{
    public class ArtDirectorAgent
    {

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ArtDirectorAgent(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<List<ArtConcept>> BrainstormConceptsAsync(string genre, string description)
        {
            string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

            string cleanApiKey = apiKey?.Trim() ?? "";

            // Безопасно настраиваем авторизацию в HttpClient
            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var systemPrompt = @"Вы — профессиональный арт-директор и эксперт по генерации изображений (Prompt Engineer) в книжном издательстве.
                Ваша задача — придумать ровно 3 альтернативные концепции для обложки книги на основе её жанра и синопсиса.

                Вы ДОЛЖНЫ вернуть ответ СТРОГО в формате JSON-массива объектов. Никакого лишнего текста, только чистый массив.

                Каждая концепция должна содержать:
                1. ""title"" — Название концепции на русском.
                2. ""description"" — Краткое художественное описание идеи для автора на русском.
                3. ""visualElements"" — Описание композиции на русском (для понимания общей логики).
                4. ""rawPrompt"" — Профессиональный, максимально детализированный и атмосферный промпт на АНГЛИЙСКОМ языке для генераторов изображений (Midjourney, Stable Diffusion, Flux). 
                    НЕ пишите короткие фразы через запятую. Стройте промпт по структуре:
                    - Основной стиль и композиция (например, ""A cinematic, moody dark fantasy book cover, dynamic action shot..."")
                    - Детальное описание главного объекта, его одежды, позы и взаимодействия со средой (включая его трансформирующуюся тень).
                    - Описание окружения (город, крыши, дождь, вывески, текстуры мокрого камня и отражения в лужах).
                    - Атмосферное освещение и эффекты (volumetric moonlight, thick fog, chiascuro lighting, neon glow, mist).
                    - Технические маркеры качества и рендеринга (hyper-detailed, photorealistic masterpiece, octane render style, 8k).
                    - Параметры вывода (например, обязательно добавляйте в конец флаг --ar 2:3).
                5. ""aspectRatio"" — Рекомендуемое соотношение сторон для обложки этого типа (например, ""2:3"" или ""3:4"").

                Формат JSON, который от вас ожидается:
                [
                {
                    ""title"": ""Название концепции"",
                    ""description"": ""Описание для автора"",
                    ""visualElements"": ""Описание визуальных элементов на русском"",
                    ""rawPrompt"": ""dark fantasy book cover, victorian gothic city, dark rooftops, raining, a man holding a lantern, shadow magic neon alchemy signs, cinematic composition, hyperdetailed, 8k --ar 2:3"",
                    ""aspectRatio"": ""2:3""
                }
                ]";

            var userContent = $"Жанр: {genre}\nСинопсис: {description}\nСгенерируй 3 концепции в формате JSON:";

            var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.5, // Минимальная креативность для точного соблюдения структуры
                    max_tokens = 2000, // Максимальное количество слов в ответе
                };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            var httpResponse = await _httpClient.SendAsync(requestMessage);

            if (!httpResponse.IsSuccessStatusCode)
            {
                string errContent = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[ArtArchitector Error] API вернул {httpResponse.StatusCode}: {errContent}");
                // Возвращаем пустой список вместо падения
                return new List<ArtConcept>(); 
            }

            string responseString = await httpResponse.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                Console.WriteLine($"[ArtDirector Success] получен ответ от модели.");
                
                if (!string.IsNullOrEmpty(rawJsonText))
                {
                    // Очистка от markdown-тегов ```json ... ```                        
                    rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();
                    
                    Console.WriteLine($"[ArtDirector Success] Получен JSON от модели.");
                    Console.WriteLine($"[ArtDirector Success] Raw JSON:\r\n {rawJsonText}\r\n");

                    // Безопасно парсим полученный JSON в список (List) концептов
                    using var parsedDoc = JsonDocument.Parse(rawJsonText);
                    var parsedRoot = parsedDoc.RootElement;

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    // Десериализуем именно как Список!
                    var spec = JsonSerializer.Deserialize<List<ArtConcept>>(parsedRoot.GetRawText(), options);

                    return spec ?? new List<ArtConcept>();
                }
            }

            return new List<ArtConcept>();
        }
    }
}
