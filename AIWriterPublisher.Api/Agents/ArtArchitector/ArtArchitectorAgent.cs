using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Agents.ArtArchitector
{
    /// <summary>
    /// Агент Арт-Архитектор.
    /// Отвечает за перевод художественного описания сцены на русском языке в строгую техническую структурированную спецификацию на русском языке.
    /// </summary>
    public class ArtArchitectorAgent
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ArtArchitectorAgent(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<TechnicalSpecDto> AnalyzeUserInputAsync(string rawPrompt, string model)
        {
            string modelPrompt = "";
            // Если модель "local", то используем упрощённый системный промпт, который не требует сложного синтаксиса и может работать с базовыми LLM, не поддерживающими сложные инструкции. Если модель "remote" (например, GPT-4), то используем расширенный промпт с жёсткими фильтрами и правилами.
            if ( model == "local" )
            {
                modelPrompt = @"
                    Правила для Z-Image (6GB VRAM):
                    1. Максимальная длина промпта — 150-200 слов (экономия памяти)
                    2. Удалять конкретные числа (cm, m, %, kg, years)
                    3. Переводить 'чувства/ощущения' в визуальные образы:
                    - 'страшный' → 'sharp teeth, bloodshot eyes, twisted limbs'
                    - 'испуганный' → 'wide eyes, trembling, pale face, open mouth'
                    4. Обязательный негативный промпт: 'blurry, low quality, bad anatomy, deformed, watermark, text, extra limbs, bad hands'
                    5. Добавлять жанр в начало, коротко: 'fantasy art, horror cover, dark romance'
                    6. Приоритет: лицо > свет > композиция (детали Z-Image дорисует сам)
                    7. Указывать размер: 832x1216 (портрет) или 1024x1024 (квадрат)
                    ";
            }
            else if (model == "nanobanana")
            {
                // Этот промпт предназначен для мощных облачных моделей, которые могут обрабатывать более длинные и сложные инструкции, а также лучше справляются с абстрактными понятиями. Он содержит жёсткие правила по удалению эпитетов и переводу их в конкретные визуальные директивы, что важно для моделей, которые могут "придумывать" детали.
                modelPrompt = @"
                    Правила для Gemini Nano Banana (облачная генерация, высокое качество):
                    1. Максимальная длина промпта — 300-400 слов (модель понимает сложные сцены)
                    2. Сохраняй все числа и пропорции (cm, %, соотношения) — модель их понимает
                    3. Переводить 'чувства/ощущения' в визуальные образы, но более тонко:
                    - 'страшный' → 'dark atmosphere, sharp shadows, ominous lighting'
                    - 'романтичный' → 'soft warm light, gentle expression, dreamy bokeh'
                    4. Всегда добавлять жанр в начало (Fantasy, Romance, Horror, Sci-fi)
                    5. Добавлять технические маркеры в конец: 'cinematic, highly detailed, 8K, professional photography'
                    6. Использовать английский язык (Gemini лучше понимает английские промпты)
                    7. Приоритет: композиция > лицо > свет > атмосфера > детали
                    8. Размер: 1024x1024 или 1024x1536 (для портретных обложек)
                    ";
            } 
            else
            {
                //По умолчанию дефолтный промпт для моделей из hugging face, которые не требуют жёстких фильтров и могут работать с более свободными инструкциями. Он сохраняет базовые правила, но не так строг в формулировках, чтобы быть совместимым с широким спектром моделей.
                modelPrompt = @"
                Правила для SDXL / Flux (локальная генерация, 6-12GB VRAM):
                1. Максимальная длина промпта — 200-250 слов
                2. Сохранять числа, но переводить в пиксели/пропорции
                3. Переводить чувства в визуальные образы + добавлять качественные теги:
                - Добавлять в конец: 'masterpiece, best quality, sharp focus, 8K'
                4. Жанр в начало, но развёрнуто: 'fantasy romance novel cover art'
                5. Обязательно добавлять стандартный негативный промпт:
                'worst quality, low quality, blurry, bad anatomy, bad hands, missing fingers, watermark, text'
                6. Приоритет: лицо > свет > детали > композиция
                7. Указывать размер в пикселях (width/height) — для ComfyUI
                ";
            }
            // Системный промпт, заставляющий модель выдать структурированный JSON на русском языке
            string systemPrompt = @"Ты — Русскоязычный Систематизатор (Art Architector) в системе генерации обложек. 
                Твоя задача — взять творческий или сумбурный поток мыслей пользователя на русском языке и разложить его в СТРОГИЙ структурированный JSON-формат на РУССКОМ языке.

                КРИТИЧЕСКИЕ ПРАВИЛА:
                1. Выдавай ТОЛЬКО чистый JSON. Никакого вводного текста, никаких рассуждений до или после JSON. Не используй markdown-разметку ```json ... ```.
                2. Никаких абстрактных эпитетов ('мистический', 'страшный', 'ужасный', 'эфирный'). Переводи их в физические свойства на русском языке. Например: вместо 'испуганный' -> 'широко раскрытые глаза, застывшая поза, дрожащие руки', вместо 'зловещий лес' -> 'густой туман, искривленные темные ветви сосен, глубокие тени'.
                3. Чётко разделяй сущности по полям JSON-спецификации. Пиши описание СТРОГО на русском языке, чтобы пользователь мог легко прочитать и отредактировать его.
                4. Обогощай описание исходя из контекста жанра и стиля. Например заброшенный пустырь, стая голодных собак -> собаки злые, облезлые, жуткие, с оскаленными зубами. Девушка в платье -> молодая женщина, длинные волосы, легкое платье, романтический образ. Но не добавляй ничего, что не было прямо или косвенно указано пользователем.
                5. Пиши описание исключительно в настоящем времени, как если бы ты описывал сцену для художника. Не используй будущее время или условные конструкции.
                6. Всегда добавляй технические маркеры качества в конце описания: 'коммерческая фотография, кинематографическая композиция, сложные детали, UHD'.
                СТРУКТУРА ОТВЕТА (ОТВЕЧАЙ СТРОГО В ЭТОМ ФОРМАТЕ, ВСЕ ПОЛЯ НА РУССКОМ):
                {
                ""mode"": ""Scene to Art"",
                ""technical_spec"": {
                    ""genre"": ""жанр книги для контекста, например: фэнтези, романтика, хоррор, научная фантастика. Если жанр не указан, отталкивайся от описания сцены для определения жанра"",
                    ""subject"": ""детальное физическое описание главных героев, существ или ключевых объектов (одежда, позы, черты лица, материалы) на русском"",
                    ""environment"": ""описание окружения (локация, растительность, постройки, погода, время суток, атмосфера тумана/света) на русском"",
                    ""style"": ""стилистика, художественные материалы, тип камеры, точные директивы по свету и источникам освещения на русском"",
                    ""composition"": ""ракурс, положение камеры, зонирование холста, фокусное расстояние, распределение весов на сцене на русском"",
                    ""negative_constraints"": ""список запрещённых элементов через запятую на русском (например: текст, водяной знак, размытость, деформация)""
                },
                ""render_settings"": {
                    ""model"": ""nanobanana-pro"",
                    ""size"": ""1024x1024""
                }
            }";

            systemPrompt += "\n\n" + modelPrompt; // Добавляем специфические правила для выбранной модели в системный промпт

             Console.WriteLine($"[ArtArchitector] Системный промпт для модели {model}:\r\n{systemPrompt}\r\n");
             Console.WriteLine($"[ArtArchitector] Полученный от пользователя сырой поток: {rawPrompt}");

            try
            {
                string baseUrl = _configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
                string apiKey = _configuration["OpenRouter:ApiKey"] ?? "";
                // string modelName = _configuration["OpenRouter:Model"] ?? "deepseek/deepseek-v4-flash:free";
                string modelName = _configuration["OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

                string cleanApiKey = apiKey?.Trim() ?? "";

                // Безопасно настраиваем авторизацию в HttpClient
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cleanApiKey}");

                Console.WriteLine($"[ArtArchitector Debug] Ключ получен из конфига? {(!string.IsNullOrEmpty(cleanApiKey) ? $"ДА, длина {cleanApiKey.Length} симв." : "НЕТ, ОН ПУСТОЙ")}");
                
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");

                // Формируем классический payload для OpenAI-совместимого эндпоинта
                var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = rawPrompt }
                    },
                    temperature = 0.1, // Минимальная креативность для точного соблюдения структуры
                    max_tokens = 10000, // Максимальное количество слов в ответе
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                Console.WriteLine($"[ArtArchitector] Отправка Магического потока на разбор в модель {modelName}...");
                var httpResponse = await _httpClient.SendAsync(requestMessage);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errContent = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ArtArchitector Error] API вернул {httpResponse.StatusCode}: {errContent}");
                    return new TechnicalSpecDto { Subject = rawPrompt };
                }

                string responseString = await httpResponse.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                Console.WriteLine($"[ArtArchitector Success] {root}");
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                    Console.WriteLine($"[ArtArchitector Success] получен ответ от модели.");
                    if (!string.IsNullOrEmpty(rawJsonText))
                    {
                        // Очистка от markdown-тегов ```json ... ```                        
                        rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();
                        
                        Console.WriteLine($"[ArtArchitector Success] Получен JSON от модели.");
                        Console.WriteLine($"[ArtArchitector Success] Raw JSON:\r\n {rawJsonText}\r\n");

                        // Безопасно парсим полученный JSON во внутренний DTO
                        using var parsedDoc = JsonDocument.Parse(rawJsonText);
                        var parsedRoot = parsedDoc.RootElement;
                        
                        // Извлекаем только блок technical_spec
                        if (parsedRoot.TryGetProperty("technical_spec", out var specElement))
                        {
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var spec = JsonSerializer.Deserialize<TechnicalSpecDto>(specElement.GetRawText(), options);
                            if (spec != null)
                            {
                                return spec;
                            }
                        }
                    }
                }

                return new TechnicalSpecDto { Subject = rawPrompt };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArtArchitector Крах] Исключение: {ex.Message}. Использование фоллбэка.");
                return new TechnicalSpecDto { Subject = rawPrompt };
            }
        }
    }
}