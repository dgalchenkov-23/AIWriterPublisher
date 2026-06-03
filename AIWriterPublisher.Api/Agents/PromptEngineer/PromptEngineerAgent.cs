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
        private readonly IConfiguration _configuration;

        // Инжектим HttpClient, зарегистрированный в Program.cs (через AddHttpClient)
        public PromptEngineerAgent(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> GenerateTechnicalPromptAsync(TechnicalSpecDto spec)
        {
            // Системный промпт для Flux.1-dev / Z-Image через gpt-oss-120b
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
                Если в сцене более одного персонажа, ВСЕГДА используй явное разделение по планам и сторонам кадра в самом начале промпта, изолируя их описания ключевыми словами. Формат для двух персонажей: 
                [Персонаж 1, его позиция и полное описание]. В отдельном предложении: [Персонаж 2, его позиция относительно Персонажа 1 и полное описание].

                --- СПЕЦИАЛЬНЫЕ ПРАВИЛА ДЛЯ АТМОСФЕРЫ ---
                - Если сцена содержит слова 'заброшенный', 'пустырь', 'сумерки', 'туман', 'пасмурно' — сцена должна быть тёмной и мрачной.
                - НЕ используй слова 'soft', 'diffused', 'ambient' в таких сценах.
                - ВСЕГДА добавляй в конец промпта: 'dark atmosphere, low key lighting, deep shadows, desaturated colors, gloomy mood'
                - Для жутких сцен дополнительно: 'eerie, unsettling, grim, bleak'

                --- СПЕЦИАЛЬНЫЕ ПРАВИЛА ДЛЯ РОМАНТИКИ ---
                - Если сцена содержит слова 'романтика', 'любовь', 'пара', 'влюбленные' — сцена должна быть светлой, теплой и воздушной.
                - НЕ используй слова 'harsh', 'dramatic', 'cinematic' в таких сценах.
                - ВСЕГДА добавляй в конец промпта: 'soft lighting, warm tones, pastel colors, dreamy atmosphere'

                --- АЛГОРИТМ СБОРКИ ФИНАЛЬНОЙ СТРОКИ (ПРОПОРЦИИ) ---
                Склей все данные в одну англоязычную строку через запятую в следующем строгом порядке:
                [Subject с детальной анатомией/одеждой], [Environment с указанием времени суток и погоды], [Composition: ракурс, фокусное расстояние, положение камеры], [Style: тип камеры, освещение, материалы поверхности, общая визуальная школа]. В самом конце добавь технические маркеры качества: ""commercial photography, cinematic composition, intricate details, UHD"".

                --- ФОРМАТ ОТВЕТА ---
                Ты должен вернуть ТОЛЬКО итоговую плоскую строку на АНГЛИЙСКОМ языке. 
                Никаких вводных фраз, никаких кавычек, никаких объяснений и никаких markdown-разметок (без ```). Только чистый текст промпта.";

            var userContent = $@"
                Обработай следующие слои и собери финальный промпт:
                - Жанр (Genre): {spec.Genre}
                - Объект (Subject): {spec.Subject}
                - Окружение (Environment): {spec.Environment}
                - Стиль и Свет (Style): {spec.Style}
                - Композиция (Composition): {spec.Composition}
                - Негативные ограничения (Negative Constraints): {spec.NegativeConstraints}";

            // Читаем из актуальной безопасной иерархии конфигурации
            string baseUrl = _configuration["AiServices:OpenRouter:BaseUrl"] ?? "[https://openrouter.ai/api/v1](https://openrouter.ai/api/v1)";
            string apiKey = _configuration["AiServices:OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter ApiKey не настроен!");
            string modelName = _configuration["AiServices:OpenRouter:PrimaryModel"] ?? "openai/gpt-oss-120b:free";

            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.1, 
                max_tokens = 2000 // Для плоского промпта 2000 токенов — с огромным запасом
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var httpResponse = await _httpClient.SendAsync(requestMessage);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errContent = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[PromptEngineer Error] API вернул {httpResponse.StatusCode}: {errContent}");
                    return string.Empty;
                }

                var responseString = await httpResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    string? rawText = choices[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrEmpty(rawText))
                    {
                        // Очищаем от возможных артефактов разметки, если модель ослушалась
                        rawText = rawText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();
                        Console.WriteLine($"[PromptEngineer Success] Сгенерирован промпт: {rawText}");
                        return rawText;
                    }
                }

                Console.WriteLine($"[PromptEngineer Error] Не удалось извлечь контент из choices.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PromptEngineer Крах] Исключение: {ex.Message}");
                return string.Empty;
            }
        }
    }
}