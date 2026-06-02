using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Agents.PromptEngineer
{
    public class PromptEngineerAgent
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IConfiguration _configuration;

        // Инжектим стандартный HttpClient и конфигурацию
        public PromptEngineerAgent(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _apiKey = configuration["Gemini:ApiKey"] 
                ?? throw new InvalidOperationException("API-ключ Gemini не найден!");
            _configuration = configuration;
        }

        public async Task<string> GenerateTechnicalPromptAsync(TechnicalSpecDto spec)
        {
            // Console.WriteLine($"[PromptEngineer] Received technical spec: {JsonSerializer.Serialize(spec)}");
            // Формируем системный промпт для Google Gemini
            string systemPrompt = @"
                Ты — PromptEngineerAgent, ключевой технический модуль в конвейере генерации книжных обложек ""AI Cover Studio"".
                Твоя единственная задача: взять структурированную техническую спецификацию (Technical Specification) сцены на РУССКОМ языке и синтезировать из неё ОДИН монолитный, очищенный, высокоэффективный плоский промпт на АНГЛИЙСКОМ языке для нейросети Flux.1-dev.

                Входные данные поступают в виде 5 изолированных слоев:
                1. Subject (Главный объект/персонаж)
                2. Environment (Окружение/задний план)
                3. Style (Стилистика, свет, материалы)
                4. Composition (Ракурс, кадрирование)
                5. Genre (Жанр книги для контекста)

                --- КРИТИЧЕСКИЕ ПРАВИЛА ИНЖЕНЕРИИ (ФИЛЬТРЫ) ---

                1. ВЫЖИГАНИЕ АБСТРАКЦИЙ:
                Полностью удаляй любые литературные, эмоциональные и оценочные эпитеты (""мистический"", ""эфирный"", ""невероятный"", ""атмосферный"", ""красивый"", ""ужасающий""). Flux не понимает метафор. Заменяй их на осязаемые физические свойства объектов, геометрию или тип освещения.
               
                2. ПРАВИЛО СВЕТА И ТЕКСТУР:
                Вместо фраз ""таинственный свет"" или ""зловещие тени"", пиши конкретные директивы освещения: ""volumetric lighting"", ""cinematic rim light"", ""harsh chiaroscuro"", ""neon glow"", ""cyberpunk ambient illumination"". Вместо ""древний"" пиши ""weathered texture, moss-covered, cracked stone"".
                --- КРИТИЧЕСКИЕ ПРАВИЛА ---

                1. СОХРАНЯЙ ГЕНДЕР И ВОЗРАСТ:
                Если в Subject есть 'девушка', 'женщина', 'мальчик' — используй 'a young woman', 'a girl', 'a boy'.
                Только если прямо сказано 'существо' без пола — тогда уточни по контексту.

                2. УЖАС — ЧЕРЕЗ КОНКРЕТИКУ:
                Вместо 'страшный' пиши 'sharp teeth, bloodshot eyes'.
                Вместо 'жуткий' пиши 'monstrous silhouette, twisted limbs'.
                Вместо 'зловещий' пиши 'glowing eyes, unnatural shadow'.

                3. ЗАПРЕТ НА 'РЕБЕНОК' (если не указано):
                Если возраст не указан — считай персонажа взрослым (18+).
                Не добавляй 'child-like' без явного указания.

                4. РАЗДЕЛЕНИЕ ПЕРСОНАЖЕЙ:
                Если в сцене более одного персонажа, всегда разделяй их описания по планам и сторонам кадра. Например: 'In the foreground of the room, a young man is standing in a leather jacket, facing left. In the background, a mysterious woman with long flowing hair is sitting on a chair, facing right.'
                Если в сцене присутствует более одного персонажа, 
                ВСЕГДА используй явное разделение по планам и сторонам кадра в самом начале промпта, 
                изолируя их описания ключевыми словами. Избегай смешивания одинаковых атрибутов 
                (например, длинные волосы у обоих) в одном предложении без четкого указания владельца. Формат для двух персонажей: 
                [Персонаж 1, его позиция и полное описание]. В отдельном предложении: [Персонаж 2, его позиция относительно Персонажа 1 и полное описание].

                --- АЛГОРИТМ СБОРКИ ФИНАЛЬНОЙ СТРОКИ (ПРОПОРЦИИ) ---

                Склей все данные в одну англоязычную строку через запятую в следующем строгом порядке:
                [Subject с детальной анатомией/одеждой], [Environment с указанием времени суток и погоды], [Composition: ракурс, фокусное расстояние, положение камеры], [Style: тип камеры, освещение, материалы поверхности, общая визуальная школа]. В самом конце добавь технические маркеры качества: ""commercial photography, cinematic composition, intricate details, UHD"".

                --- ФОРМАТ ОТВЕТА ---
                Ты должен вернуть ТОЛЬКО итоговую плоскую строку на АНГЛИЙСКОМ языке. 
                Никаких вводных фраз (""Here is your prompt:""), никаких кавычек, никаких объяснений и никаких markdown-разметок (без ` ``` `). Только чистый текст промпта.
            ";

            var userContent = $@"
                Обработай следующие слои и собери финальный промпт:
                - Объект (Genre): {spec.Genre}
                - Объект (Subject): {spec.Subject}
                - Окружение (Environment): {spec.Environment}
                - Стиль и Свет (Style): {spec.Style}
                - Композиция (Composition): {spec.Composition}
                - Негативные ограничения (Negative Constraints): {spec.NegativeConstraints}
                ";
            string baseUrl = _configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            string apiKey = _configuration["OpenRouter:ApiKey"] ?? "";

            string modelName = _configuration["OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";
            string cleanApiKey = apiKey?.Trim() ?? "";

            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cleanApiKey}");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");

            // Формируем классический payload для OpenAI-совместимого эндпоинта
            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.1, // Минимальная креативность для точного соблюдения структуры
                max_tokens = 10000, // Максимальное количество слов в ответе
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Console.WriteLine($"[PromptEngineer] Sending request to gpt-oss-120b:free. Payload: {jsonPayload}");

            var httpResponse = await _httpClient.SendAsync(requestMessage);
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errContent = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[PromptEngineer Error] API вернул {httpResponse.StatusCode}: {errContent}");
                return "";
            }
            Console.WriteLine($"[PromptEngineer] Request successful. Parsing response...");
            var responseString = await httpResponse.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrEmpty(rawJsonText))
                {
                    rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();

                    Console.WriteLine($"[PromptEngineer] Raw response: {rawJsonText}");
                }
                return rawJsonText ?? rawJsonText ?? "";
            }
            else
            {
                Console.WriteLine($"[PromptEngineer Error] Не удалось найти 'choices' в ответе модели.");
                return "";
            }

        }
    }
}