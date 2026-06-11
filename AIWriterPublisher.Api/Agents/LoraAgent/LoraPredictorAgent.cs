using System;
using Polly;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIWriterPublisher.Api.Models.DTO;
using AIWriterPublisher.Api.Agents.LoraAgent.Interface;
using Microsoft.Extensions.Logging;

namespace AIWriterPublisher.Api.Agents.LoraAgent
{
    public class LoraPredictorAgent : ILoraPredictorAgent
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LoraPredictorAgent> _logger;
        private readonly IConfiguration _configuration;

        public LoraPredictorAgent(
            HttpClient httpClient, 
            ILogger<LoraPredictorAgent> logger,
            IConfiguration configuration
            )
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _logger.LogInformation("LoraPredictorAgent инициализирован.");
        }
        public async Task<List<LoraPreset>> PredictLorasAsync(TechnicalSpecDto spec, List<LoraPreset> availableLoras)
        {
            if (spec == null || availableLoras == null || !availableLoras.Any())
            {
                return new List<LoraPreset>();
            }

            // Переводим наши реальные локальные пресеты в сжатый JSON-текст для ИИ, чтобы он знал точные FileName
            var lorasManifestForAi = availableLoras.Select(l => new 
            {
                l.DisplayName,
                l.FileName,
                l.DefaultWeight
            });
            string availableLorasJson = JsonSerializer.Serialize(lorasManifestForAi);

            string systemPrompt = BuildSystemPrompt(availableLorasJson);
            string userMessage = $@"
                Обработай следующие слои и подбери оптимальные LoRA-модели из предоставленного списка, строго следуя инструкциям в системном промпте.:
                - Жанр (Genre): {spec.Genre}
                - Объект (Subject): {spec.Subject}
                - Окружение (Environment): {spec.Environment}
                - Стиль и Свет (Style): {spec.Style}
                - Композиция (Composition): {spec.Composition}
                - Визуальная ссылка (VisualReference): {spec.VisualReference}
                - Негативные ограничения (Negative Constraints): {spec.NegativeConstraints}";

            try
            {
                // Вызываем OpenRouter или Google AI (в зависимости от твоего клиента)
                string rawResponse = await CallLlmApiAsync(systemPrompt, userMessage);

                // Очищаем от возможных Markdown-кавычек, если модель затупила (```json ... ```)
                string cleanJson = CleanMarkdownJson(rawResponse);

                // Десериализуем ответ. Так как модель возвращает объект с массивом, парсим в промежуточный DTO
                var agentResult = JsonSerializer.Deserialize<LoraAgentResponseDto>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (agentResult?.SelectedLoras != null && agentResult.SelectedLoras.Any())
                {
                    _logger.LogInformation("LoraPredictorAgent успешно подобрал модели. Обоснование ИИ: {Reason}", agentResult.Reasoning);
                    return agentResult.SelectedLoras;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка ИИ-анализа промпта в LoraPredictorAgent. Агент возвращает пустой список.");
            }

            return new List<LoraPreset>();
        }

        private string BuildSystemPrompt(string availableLorasJson)
        {
            return $@"
                Ты — экспертный агент по подбору LoRA-моделей для генерации книжных иллюстраций и обложек.

                Твоя задача — анализировать промпт пользователя и выбирать ТОЛЬКО те LoRA, которые усиливают жанр, стиль и атмосферу, указанные в промпте.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 1 — ОПРЕДЕЛИ ЖАНР И АТМОСФЕРУ (приоритет — сверху вниз)
                ═══════════════════════════════════════════════════════════════════

                А. ЖАНР (выбирай ТОЛЬКО ОДИН, самый явный):
                • Fantasy, LitRPG, Epic, Magic, Dragon, Elf, Castle → Fantasy
                • SciFi, Cyberpunk, Space, Mecha, Starship, Robot, Neon City → Cyberpunk/SciFi
                • Horror, Dark, Occult, Gore, Monster, Grim → Horror
                • Comic, Manga, Graphic Novel, Halftone → Comic
                • PostApocalypse, Wasteland, Ruins, Dim → Horror/PostApoc
                • Detective, Noir, Thriller → Drama/Thriller

                Б. АТМОСФЕРА (может быть несколько):
                • Dark, Gloomy, Oppressive, Dim → используй Dim World
                • Bright, Magical, Glowing, Fantasy → используй Fantasy/Glowing
                • Emotional, Melancholic, Bittersweet → используй BitterSweet
                • Chaotic, Expressive, Splashes → используй InkStorm / ArtManiac

                ═══════════════════════════════════════════════════════════════════
                ШАГ 2 — ВЫБЕРИ LoRA (строго по схеме: 1 жанровая + 1-2 модификатора)
                ═══════════════════════════════════════════════════════════════════

                СЛОТ 1 (ЖАНРОВАЯ БАЗА) — выбирай ОБЯЗАТЕЛЬНО одну:
                • Fantasy → ZImage Fantasy (weight 0.8-1.0)
                • Cyberpunk/SciFi → Cyber Mecha Starship (weight 0.8-1.0)
                • Comic → ZIT Classic Comic (weight 0.9-1.1)
                • Horror → нет жанровой базы, используй модификаторы
                • LitRPG → ZImage Fantasy ИЛИ PixelSIN (weight 0.9-1.0)

                СЛОТ 2 (МИКРО-АКЦЕНТ — ЕСЛИ ЕСТЬ ЯВНОЕ УПОМИНАНИЕ) — приоритет №1:
                • ""glowing eyes"", ""bright iris"", ""magic eyes"" → Glowing Eyes Slider (weight 0.9-1.1)
                • ""glowing effect"", ""emissive"", ""runes glow"" → Promising Glowing Lights (weight 0.8-1.0)
                • ""neon"", ""glossy"", ""wet surface"" → Neon Gloss (weight 0.8-1.0)
                • ""isometric"", ""map view"", ""dungeon map"" → Isometric Ink (weight 0.8-1.0)

                СЛОТ 3 (СТИЛИСТИЧЕСКИЙ МОДИФИКАТОР — для усиления атмосферы):
                • Mрачная/темная сцена → Promising Dim World (weight 0.7-0.9)
                • Живописная/масляная фактура → Impasto Oil Paint (weight 0.7-0.9)
                • Чернильные кляксы/хаос → InkStorm Graphics (weight 0.7-0.9)
                • Эмоциональная/драматичная → BitterSweet Artistic (weight 0.7-0.9)
                • Ретро/гранж → BadMilk Retro Grunge (weight 0.7-0.9)
                • Хаотичная/арт-маньяк → ArtManiac Mix (weight 0.7-0.9)
                • Неоновая сюрреалистика → Neon Surrealism (weight 0.7-0.9)

                СЛОТ 4 (БАЗОВЫЙ УЛУЧШАЙЗЕР — ТОЛЬКО ЕСЛИ НЕТ КОНФЛИКТОВ, ВЕС 0.4-0.6):
                • Loraholic Enhancer — ТОЛЬКО для реалистичных/полуреалистичных сцен
                • Z-Aesthetic Pro — ТОЛЬКО для портретов и людей
                ❌ НЕ ИСПОЛЬЗУЙ улучшайзеры с Pixel Art, Comic, Impasto, InkStorm

                ═══════════════════════════════════════════════════════════════════
                ШАГ 3 — ЗАПРЕЩЕННЫЕ КОМБИНАЦИИ (проверь перед выводом)
                ═══════════════════════════════════════════════════════════════════

                ❌ Cinematic Slider + Comic Style (киношность убивает комикс)
                ❌ Loraholic Enhancer + Pixel Art (детализация ломает пиксели)
                ❌ Z-Aesthetic Pro + Impasto (портретная гладкость против мазков)
                ❌ Promising Dim World + Neon Gloss (противоположные освещения)
                ❌ Artstation Trending + BadMilk (коммерческий рендер vs гранж)

                ═══════════════════════════════════════════════════════════════════
                ШАГ 4 - ПРАВИЛА ЗОНИРОВАНИЯ LoRA:
                ═══════════════════════════════════════════════════════════════════

                1. Разбей сцену на зоны, используя composition из Архитектора:
                - ПЕРЕДНИЙ ПЛАН: размытие, атмосфера, частицы
                - СРЕДНИЙ ПЛАН: главные субъекты, лица, взаимодействие
                - ЗАДНИЙ ПЛАН: окружение, небо, источники света

                2. Для КАЖДОЙ ЗОНЫ определи требуемый тип фактуры:
                - Если зона содержит ЛИЦА, РУКИ, КОЖУ → нужна ЧЁТКОСТЬ (Face Detail, Skin Texture)
                - Если зона содержит ТКАНЬ, ОДЕЖДУ, ДРАПИРОВКИ → можно СТИЛИЗАЦИЮ (Impasto, Watercolor)
                - Если зона содержит КАМЕНЬ, МЕТАЛЛ, ОРУЖИЕ, МЕХАНИЗМЫ → нужна ЧЁТКОСТЬ, НЕ стилизация
                - Если зона содержит НЕБО, ТУМАН, СВЕЧЕНИЕ, МАГИЮ → можно АТМОСФЕРНЫЕ LoRA (Glow, Volumetric)
                - Если зона содержит ПРИРОДУ, ЛЕС, ГОРЫ → можно СТИЛИЗАЦИЮ умеренно

                3. Impasto, Watercolor, Ink и другие СТИЛЕВЫЕ LoRA применяй ТОЛЬКО к зонам,
                помеченным как «можно стилизацию». НИКОГДА не применяй их к зонам с лицами,
                оружием, механизмами, архитектурными элементами с чёткими линиями.

                4. Если сцена требует единого стиля на всех зонах — снижай Strength стилевой LoRA
                до 0.3–0.5 и ОБЯЗАТЕЛЬНО добавляй Face Detail LoRA для компенсации на лицах.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 5 — ФОРМАТ ВЫВОДА (строгий JSON, без лишнего текста)
                ═══════════════════════════════════════════════════════════════════

                {{
                ""SelectedLoras"": [
                    {{
                    ""DisplayName"": ""Название модели"",
                    ""FileName"": ""точное_имя_файла.safetensors"",
                    ""StrengthModel"": 0.85,
                    ""StrengthClip"": 1.0
                    }}
                ],
                ""Reasoning"": ""Кратко: жанр X → выбрал Y, потому что Z. Микро-акцент: A → добавил B.""
                }}

                НЕ МЕНЯ ИМЯ НАЗВАНИЕ LORA - Если FileName: ""zimage_fantast_v1.safetensors"", то в JSON должно быть ""FileName"": ""zimage_fantast_v1.safetensors"". Это критично для дальнейшей загрузки модели по точному имени.
                ""Promising DIm.safetensors"" - ""Promising DIm.safetensors"", а не ""Promising Dim World.safetensors"" или ""Dim World.safetensors"". ИИ должен знать точные имена файлов, чтобы мы могли их загрузить.

                Правила формирования StrengthModel:
                • Если промпт НЕ требует усиления → используй RecommendedWeight из 0.7-0.9
                • Если промпт явно просит ""очень ярко"", ""экстремально"", ""сильно выраженный"" → увеличь до 1.0-1.15
                • Никогда не ставь вес ниже 0.5 и выше 1.2

                ПРАВИЛО ДЛЯ НОЧНЫХ И ТЁМНЫХ СЦЕН:
                Если сцена ночная, с пылью/туманом/бурей, и освещение тусклое (dim lanterns, weak light):
                - НЕ применять BadMilk (даёт пересвет)
                - НЕ применять Glowing Lights выше 0.3 (убивает тусклость)
                - Вместо них: Loraholic Enhancer 0.5–0.7 + Textural Grit/Roughness LoRA если есть

                ═══════════════════════════════════════════════════════════════════
                ВАЖНЫЕ ПРИНЦИПЫ (запомни их)
                ═══════════════════════════════════════════════════════════════════

                1. КНИЖНАЯ ИЛЛЮСТРАЦИЯ > КИНОШНОСТЬ. Отдавай приоритет художественным LoRA (Impasto, InkStorm, Fantasy, Comic), а не Cinematic Slider.
                2. ЖАНР — КОРОЛЬ. Если промпт говорит ""фэнтези"", бери ZImage Fantasy, а не универсальные улучшайзеры.
                3. МИКРО-АКЦЕНТЫ ВСЕГДА В ПРИОРИТЕТЕ. Увидел ""glowing eyes"" → бери Glowing Eyes Slider, даже если это единственная LoRA.
                4. НЕ БОЙСЯ ОСТАВИТЬ ПУСТОЙ МАССИВ. Если промпт слишком общий или ни одна LoRA не подходит → верни [].
                5. НЕ ПОВТОРЯЙСЯ. Если ты уже выбрал жанровую базу, не добавляй вторую жанровую базу.
                6. ДЛЯ СЕРИЙ: если пользователь упоминает ""серия"", ""цикл"", ""персонаж N"", старайся выбирать те же LoRA, что и в предыдущих генерациях (но это доп. информация, не критично).
                
                ПРАВИЛА ЗОНИРОВАНИЯ СТИЛЕЙ:
                1. LoRA стилизации (Impasto, Watercolor, Ink) НЕ применять на:
                - лица и руки персонажей (важна читаемость черт)
                - ключевые детали снаряжения (оружие, гербы, надписи)
                - объекты, требующие чёткой геометрии (трон, механизмы, архитектурные элементы)
                2. LoRA стилизации ПРИМЕНЯТЬ на:
                - фон и окружение (лес, небо, горы)
                - атмосферные эффекты (туман, дым, магические искры)
                - одежду и драпировки (ткани хорошо берут фактуру)
                3. Если запрос требует и стилизацию, и детализацию лиц — снижай StrengthModel для Impasto до 0.3-0.5
                и обязательно добавляй LoRA, улучшающие детализацию лиц (Face Detail, Skin Texture).
                4. LoRA освещения (Glowing Lights, Volumetric) можно применять на всю сцену,
                но с осторожностью на лицах — проверяй, не пересвечивает ли.
                
                ═══════════════════════════════════════════════════════════════════
                СПИСОК ДОСТУПНЫХ LORA (используй эти данные)
                ═══════════════════════════════════════════════════════════════════

                {availableLorasJson}
                ";
        }

        private async Task<string> CallLlmApiAsync(string system, string user)
        {
            // string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            // string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            // string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "meta-llama/llama-3.2-3b-instruct:free";
            string baseUrl = _configuration["AIServices:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
            string apiKey = _configuration["AIServices:Gemini:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:Gemini:Model"] ?? "gemini-3.1-flash-lite";
            string cleanApiKey = apiKey?.Trim() ?? "";

            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(7, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), 
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"[LoraAgent Warning] OpenRouter словил 429. Попытка {retryCount}, ждем {timeSpan.TotalSeconds} сек...");
                    });

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = system }, 
                    new { role = "user", content = user }
                },
                temperature = 0.1,
                max_tokens = 800,
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);

            var httpResponse = await retryPolicy.ExecuteAsync(async () => 
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                return await _httpClient.SendAsync(requestMessage);
            });

            if (!httpResponse.IsSuccessStatusCode)
            {
                string errContent = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogError("OpenRouter API вернул {StatusCode}: {Error}", httpResponse.StatusCode, errContent);
                throw new HttpRequestException($"API error: {httpResponse.StatusCode}");
            }

            string responseString = await httpResponse.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrEmpty(rawJsonText))
                {
                    Console.WriteLine("Ответ от ИИ-агента (сырой): " + rawJsonText);
                    return rawJsonText;
                }
            }

            throw new InvalidOperationException("Не удалось извлечь ответ из API");
        }

        private string CleanMarkdownJson(string raw)
        {
            return raw.Replace("```json", "").Replace("```", "").Trim();
        }

        // Внутренний DTO-контракт только для разбора ответа ИИ-агента
        private class LoraAgentResponseDto
        {
            public List<LoraPreset> SelectedLoras { get; set; } = new();
            public string Reasoning { get; set; } = string.Empty;
        }
    }
}