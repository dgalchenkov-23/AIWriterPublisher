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

                3. УНИЧТОЖЕНИЕ СТОКОВОГО МУСОРА (ANTI-STOCK FILTER):
                Категорически запрещено использовать бессмысленные маркеры ""качества"": ""masterpiece"", ""best quality"", ""8K"", ""4K"", ""UHD"", ""trending on artstation"", ""award winning"". Вместо них описывай физику кадра: ""sharp focus"", ""highly detailed material textures"", ""defined fine details"", ""cinematic film grain"".

                4. РАЗРЕШЕНИЕ СТИЛИСТИЧЕСКИХ КОНФЛИКТОВ (ПРИОРИТЕТ ЖАНРА):
                Жанр книги (Genre) имеет абсолютный приоритет над слоем Style. 
                Если слой Style содержит маркеры глянца или коммерции (""commercial photography"", ""bright look""), но при этом в Genre или в описании сцены передан мрачный, напряженный или жесткий контекст (хоррор, триллер, дарк-фэнтези, нуар, постапокалипсис, драма):
                - Немедленно УДАЛЯЙ и ЗАМЕНЯЙ ""commercial photography"" на кинематографические или сырые фото-эквиваленты: ""raw candid photography"", ""gritty cinematic film still"", ""documentary photo"".
                - Полностью БЛОКИРУЙ и вырезай мягкие стоковые модификаторы света (""soft lighting"", ""diffused ambient light"").

                Придерживайся этих правил с фанатичной строгостью. Твоя цель — создать промпт, который будет максимально понятен и эффективен для Flux.1-dev, без всякого мусора и абстрактных терминов, которые могут ввести модель в заблуждение.
                Помни, что ты — инженер, а не поэт. Убирай всё лишнее, что не является конкретной визуальной директивой, и всегда адаптируй описание под контекст жанра, чтобы усилить синергию между стилем и содержанием сцены.

                Правило 1: Тотальная дедупликация хвоста. Если токен или его близкий синоним уже использован, агент обязан вырезать его при финальной сборке строки.

                Правило 2: Логика освещения. Нельзя сочетать diffuse (рассеянный) и chiaroscuro (контрастный). Должен быть выбран один ведущий тип света.

                Правило 3: Приоритет «Сырого кадра». Чтобы убить арт-эффект, нужно принудительно внедрять маркеры реальной оптики и убирать избыточную детализацию описания бэкграунда.

                --- КРИТИЧЕСКИЕ ПРАВИЛА ДЛЯ ПЕРСОНАЖЕЙ ---

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

                --- СИНЕРГИЯ И КРОСС-ЖАНРОВАЯ АТМОСФЕРА ---

                1. ДЛЯ МРАЧНЫХ, ТЕМНЫХ И НАПРЯЖЕННЫХ КОНТЕКСТОВ (Хоррор, Триллер, Нуар, Постап, Руины):
                - Форсируй глубокие тени и низкий ключ: ""harsh chiaroscuro"", ""low-key lighting"".
                - Намертво закрепляй в конце промпта эмоциональный хвост: ""dark atmosphere, low key lighting, deep shadows, desaturated colors, gloomy mood"".
                - Если сцена имеет явные пугающие или заброшенные элементы, добавь: ""eerie, unsettling, grim, bleak"".

                2. ДЛЯ СВЕТЛЫХ И ВОЗДУШНЫХ КОНТЕКСТОВ (Романтика, Любовь, Мелодрама, Легкое Фэнтези):
                - Исключай понятия ""harsh"", ""dramatic"", ""heavy shadows"".
                - Используй теплую, пастельную и мягкую палитру.
                - Намертво закрепляй в конце промпта хвост: ""soft lighting, warm tones, pastel colors, dreamy atmosphere"".

                3. ДЛЯ ИНЫХ ЖАНРОВ (Киберпанк, Научная фантастика, Эпическое фэнтези):
                - Адаптируй освещение и текстуры под контекст (например, ""vibrant neon grading, crisp tech definitions"" для киберпанка или ""magical volumetric glows, ethereal light shafts"" для классического фэнтези).

                --- АЛГОРИТМ СБОРКИ ФИНАЛЬНОЙ СТРОКИ (ПРОПОРЦИИ) ---
                Склей все данные в одну англоязычную строку без переносов строк, используя запятые как разделители, в следующем строгом порядке:
                1. [Core Subject]: Главный объект/персонажи с детальной анатомией, позами, одеждой и текстурой кожи/материалов.
                2. [Environment]: Окружение, задний план, погодные условия, четкое время суток и состояние неба.
                3. [Composition]: Ракурс, положение камеры, фокусное расстояние, глубина резкости (например, ""shallow depth of field, sharp focus on foreground"").
                4. [Style & Lighting]: Физический тип съемки (""cinematic film still"" или ""raw photography""), точные директивы освещения, цветокоррекция.
                5. [Genre & Mood Tail]: Обязательный хвост атмосферных токенов, жестко соответствующий правилам синергии жанра.

                КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО бездумно дописывать в конец ""commercial photography, UHD, 8k"". Техническое качество должно быть вплетено через физику кадра (например: ""sharp focus, highly detailed textures, photorealistic film grain"").

                --- ФОРМАТ ОТВЕТА ---
                Ты должен вернуть ТОЛЬКО итоговую плосскую строку на АНГЛИЙСКОМ языке. 
                Никаких вводных фраз, никаких кавычек, никаких объяснений и никаких markdown-разметок (без каких-либо ```). Только чистый, готовый к отправке в модель текст промпта.";
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