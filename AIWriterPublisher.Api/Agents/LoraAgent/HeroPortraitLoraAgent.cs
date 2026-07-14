using System;
using System.Collections.Generic; // Для List
using System.Linq;                // КРИТИЧНО: без этого не работают .Any() и .Select()
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;           // Для CancellationToken
using System.Threading.Tasks;
using AIWriterPublisher.Api.Models.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace AIWriterPublisher.Api.Agents.LoraAgent
{
    public class HeroPortraitLoraAgent
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HeroPortraitLoraAgent> _logger;
        private readonly IConfiguration _configuration;

        public HeroPortraitLoraAgent(
            HttpClient httpClient, 
            ILogger<HeroPortraitLoraAgent> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _logger.LogInformation("HeroPortraitLoraAgent инициализирован.");
        }

        // Добавили CancellationToken
        public async Task<List<LoraPreset>> PredictPortraitLorasAsync(string userDescription, List<LoraPreset> availableLoras, CancellationToken cancellationToken, string analysisModel = "gpt-oss:120b")
        {
            if (string.IsNullOrEmpty(userDescription) || availableLoras == null || !availableLoras.Any())
            {
                return new List<LoraPreset>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var lorasManifestForAi = availableLoras.Select(l => new 
            {
                l.DisplayName,
                l.FileName,
                l.DefaultWeight
            });
            string availableLorasJson = JsonSerializer.Serialize(lorasManifestForAi);

            string systemPrompt = BuildSystemPrompt(availableLorasJson);
            string userMessage = $@"
                Обработай следующий промпт и подбери идеальные LoRA-модели из доступного манифеста, рассчитав их веса (strength):
                - Пользовательский запрос: {userDescription}";
            try
            {
                _logger.LogInformation("[LoraPredictor] Модель для анализа ЛоРА: {Model}", analysisModel);
                string rawResponse = string.Empty;

                if (analysisModel == "ollama-qwen-2-7b")
                {
                    rawResponse = await CallOllamaForLoraAsync(systemPrompt, userMessage, cancellationToken);
                }
                else
                {
                    rawResponse = await CallLlmApiAsync(systemPrompt, userMessage, cancellationToken);
                }

                string cleanJson = CleanMarkdownJson(rawResponse);

                cancellationToken.ThrowIfCancellationRequested();

                var agentResult = JsonSerializer.Deserialize<LoraAgentResponseDto>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (agentResult?.SelectedLoras != null && agentResult.SelectedLoras.Any())
                {
                    _logger.LogInformation("[HeroPortraitLoraAgent] успешно подобрал модели. Обоснование ИИ: {Reason}", agentResult.Reasoning);
                    return agentResult.SelectedLoras;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка ИИ-анализа промпта в HeroPortraitLoraAgent. Агент возвращает пустой список.");
            }

            return new List<LoraPreset>();
        }

        private async Task<string> CallOllamaForLoraAsync(string systemPrompt, string userMessage, CancellationToken ct)
        {
            string ollamaUrl = _configuration["AIServices:Ollama:BaseUrl"] ?? "http://localhost:11434";
            string modelName = _configuration["AIServices:Ollama:Model"] ?? "qwen2.5-coder:7b";

            string fullPrompt = $@"[СИСТЕМНАЯ ИНСТРУКЦИЯ]
                Ты — экспертный агент по подбору LoRA-моделей для генерации книжных иллюстраций.
                Твоя задача — анализировать промпт и возвращать ТОЛЬКО JSON с выбранными LoRA.
                Никаких пояснений до или после JSON.

                {systemPrompt}

                [ЗАПРОС ПОЛЬЗОВАТЕЛЯ]
                {userMessage}

                [ПРАВИЛА ОТВЕТА]
                Верни ТОЛЬКО JSON в формате:
                {{""SelectedLoras"": [...], ""Reasoning"": ""краткое обоснование""}}
                Без markdown, без ```json, только чистый JSON.";

            var requestBody = new
            {
                model = modelName,
                prompt = fullPrompt,
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    num_predict = 1000,
                    top_p = 0.9
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ollamaUrl.TrimEnd('/')}/api/generate");
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _logger.LogInformation("[HeroPortraitLoraAgent Ollama] Отправка запроса к {Url}/api/generate, модель: {Model}", ollamaUrl, modelName);

            // Передали токен отмены в HttpClient
            var response = await _httpClient.SendAsync(request, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                _logger.LogError("[LoraAgent Ollama Error] {StatusCode}: {Error}", response.StatusCode, error);
                throw new HttpRequestException($"Ollama API error: {response.StatusCode}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("response", out var responseContent))
            {
                string rawResponse = responseContent.GetString() ?? "";
                _logger.LogInformation("[LoraAgent Ollama] Получен ответ, длина: {Length} символов", rawResponse.Length);
                
                return CleanOllamaResponse(rawResponse);
            }
            
            throw new InvalidOperationException("Не удалось извлечь ответ из Ollama API");
        }

        private string CleanOllamaResponse(string raw)
        {
            raw = raw.Replace("```json", "").Replace("```", "").Replace("```text", "").Trim();
            
            if (raw.StartsWith("\"") && raw.EndsWith("\""))
                raw = raw.Substring(1, raw.Length - 2);
            
            int startBrace = raw.IndexOf('{');
            int endBrace = raw.LastIndexOf('}');
            
            if (startBrace >= 0 && endBrace > startBrace)
            {
                raw = raw.Substring(startBrace, endBrace - startBrace + 1);
            }
            
            return raw;
        }

        private string BuildSystemPrompt(string availableLorasJson)
        {
            return $@"
        Ты — Лара по профессии Лоравед, экспертный ИИ-агент по подбору LoRA-моделей для генерации ПОРТРЕТОВ, КРУПНЫХ ПЛАНОВ И ЛИЦ персонажей под движок Z-Image / Z-Image Turbo.
        Твоя задача — проанализировать описание внешности персонажа, сопоставить его с манифестом доступных моделей и вернуть идеальный стек LoRA в формате JSON.

        ⚠️ ГЛАВНЫЙ ЗАКОН ПОРТРЕТИСТА:
        Для генерации лиц критически важна чистая анатомия, выразительность глаз и естественность кожи. Перебор моделей превращает портрет в месиво из артефактов и пережигает текстуры. Твоя цель — собрать минималистичный, но максимально точный стек. Ответ без художественной или специализированной портретной LoRA при наличии яркого текстового описания внешности считается критической ошибкой!

        ═══════════════════════════════════════════════════════════════════
        МАТЕМАТИКА СТЕКА (Строго от 2 до 4 моделей в сумме — ЖЕЛЕЗНЫЙ ЛИМИТ!)
        ═══════════════════════════════════════════════════════════════════
        Твой массив ""SelectedLoras"" должен собираться по строгому математическому алгоритму слотов:

        СЛОТ 1: ТЕХНИЧЕСКИЙ ПАТЧ (Обязателен всегда)
        -> ""z-image_turbo_distillpatch_comfyui.safetensors"" — ВСЕГДА вес 1.0. Исправляет боди-хоррор и артефакты инференса. Не исключать и не менять вес!

        СЛОТ 2: БАЗОВАЯ ЭСТЕТИКА (Опционально, проверяй конфликты!)
        -> ""Z-Image-Aesthetic_v1.safetensors"" — вес 1.0. 
        *ЖЕСТКОЕ ОГРАНИЧЕНИЕ:* Проверяй поле ""ConflictSet"" выбранной портретной модели из Слота 3! Если там указан ""Z-Image-Aesthetic"" (как у MIDJOURNEY V1), ты ОБЯЗАНА полностью исключить Aesthetic из стека!

        СЛОТ 3: ПОРТРЕТНЫЙ ИЛИ ХУДОЖЕСТВЕННЫЙ ЦЕНТР (Обязательно 1 модель, максимум 2)
        Выдели маркеры стиля, расы или реализма в запросе пользователя и выбери строго подходящую модель из манифеста:
        • Кинематографичный реализм / Драма: ""subjectplus_vibes_v1.safetensors"" (триггер: sbjpls, вес ~0.7).
        • Студийный фотопортрет / Кожа: ""V4_flux_klein.safetensors"" (Portrait Engine, вес 0.5-1.0).
        • Фотореализм без пластика: ""RealisticSnapshot-Zimage-Turbov5.safetensors"" (вес 0.6-0.7) ИЛИ ""ZIMAGE-CCD-V1.safetensors"" (триггер: ccdstyle, вес 0.3-0.6). *НИКОГДА НЕ МЕШАЙ CCD И SNAPSHOT ВМЕСТЕ!*
        • Европейский типаж: ""Jibs_Realistic_z-image_lora_V1.safetensors"" (вес 0.2-0.6).
        • Инопланетяне / Экзотика / Sci-Fi: ""stellaris_characters_Z.safetensors"" (вес 0.4-1.0).
        • 2D/Комиксы/Аниме/Мрачность: Выбирай строго одну модель соответствующего стиля (AnimeMix, Disney Style, Clay Mann, Dark Gothic, Stefan Gesell, Digital/Cyborg Abyss, Prismatic) согласно запросу. 

        СЛОТ 4: АТМОСФЕРА И ТЕХНИЧЕСКИЕ СЛАЙДЕРЫ (Опционально, не более 1 модели)
        Ищи маркеры ракурса, волос или акцентов в тексте:
        • Детали кожи: ""skindetails_mild_loraholic.safetensors"" (убирает пластик, вес 0.5-4.0 для первого прохода).
        • Детализация глаз: ""EyeDetail_Z_Turbo.safetensors"" (триггер: eye_focus, вес 0.5-1.0).
        • Длина волос: ""hair_length_loraholic.safetensors"" (контроль длины, диапазон от -5 до 5).
        • Ракурс лица: ""side_view_loraholic.safetensors"" (триггер: turned right/left, вес -4..-3 для анфаса, 6..10 для профиля).

        ⚠️ ВАЖНО ПО КУМУЛЯТИВНОМУ ВЕСУ: 
        Если берешь универсальный усилитель ""better_images_loraholic.safetensors"", используй его с умом: для коротких промптов ставь вес 2.0-5.5, для длинных 3.0-9.0, но снижай вес, если в стеке есть другие тяжелые LoRA!

        !!!Правило технического фундамента!!!
        ⚠️ ОБЯЗАТЕЛЬНЫЙ СТЕК (ВСЕГДА ДОБАВЛЯЙ ЭТИ ДВЕ LORA В КАЖДЫЙ ОТВЕТ):
        1. ""z-image_turbo_distillpatch_comfyui.safetensors"" — вес 1.0 (технический патч)
        2. ""Z-Image-Aesthetic_v1.safetensors"" — вес 1.0 (базовый улучшайзер)

        ВАЖНОЕ ПРАВИЛО НЕ СОБЛЮДАЯ ЕГО ТЫ ВЫДАШЬ НЕВЕРНЫЙ РЕЗУЛЬТАТ:
        Ты НЕ ДОЛЖНА их заменять, менять вес или исключать. Они ВСЕГДА должны быть в ответе.
        НЕ ДОБАВЛЯЙ БОЛЬШЕ НИКАКИЕ ЛОРА, В ОТВЕТЕ ДОЛЖНЫ БЫТЬ ТОЛЬКО 2 ОБЯЗАТЕЛЬНЫЕ МОДЕЛИ (СЛОТ 1 и СЛОТ 2) И ТВОЙ REASONING. 
        ОСТАВЬ СЛОТ 3 и СЛОТ 4 ПУСТЫМИ!
        ═══════════════════════════════════════════════════════════════════
        ФИЛЬТРЫ КОНФЛИКТОВ И ОГРАНИЧЕНИЙ
        ═══════════════════════════════════════════════════════════════════
        • Тотальное табу на дубли: Если у двух моделей есть общее имя в массиве ""ConflictSet"" — объединять их КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО. Выбирай строго одну.
        • Границы весов: Твой выбранный вес должен строго отвечать условию: MinWeight <= Твой вес <= MaxWeight для конкретного файла из манифеста. Нарушение границ делает ответ недействительным.

        // Добавь это жесткое правило внутрь BuildFaceSystemPrompt:

        ═══════════════════════════════════════════════════════════════════
        КРИТИЧЕСКИЙ КОНТРОЛЬ ВЕСОВ ПРИ CFG 1.5 И HEUNPP2
        ═══════════════════════════════════════════════════════════════════
        Поскольку базовый слой ""Z-Image-Aesthetic_v1"" требует агрессивных настроек (20 шагов, CFG 1.5), любые дополнительные LoRA на весе 1.0 гарантированно ПЕРЕЖИГАЮТ картинку в пластиковый вектор. 

        Ты ОБЯЗАНА применять микро-дозирование весов для слотов 3 и 4:
        • ""stellaris_characters_Z.safetensors"" — Если персонаж человекоподобный (эльф, киборг, экзотика с кожей), ставь вес строго в диапазоне 0.35 - 0.5. Вес 1.0 разрешен ИСКЛЮЧИТЕЛЬНО если в промпте затребован полноценный негуманоидный монстр или чужой!
        • ""EyeDetail_Z_Turbo.safetensors"" — Никогда не ставь на 1.0 при первом проходе! Твой лимит: 0.5 - 0.6, иначе анатомия лица поплывет под текстуру глаза.
        • ""skindetails_mild_loraholic.safetensors"" — Для убирания пластика держи вес в районе 1.5 - 2.5, не прыгай выше.
        • Если в стек берутся художественные стили (например, ""Z Image Turbo - Dark Gothic Style.safetensors""), их вес должен плавать в пределах 0.45 - 0.6, чтобы не забить текстуру кожи.

        При добавлении деталей кожи или светящихся элементов всегда используй ограничители 'clean', 'ultra-thin' и 'micro-lines'. Избегай слов, ассоциирующихся с грязью или старением (weathered, imperfections), заменяя их на техническую детализацию (fine pores, satin sheen).

        ═══════════════════════════════════════════════════════════════════
        СТРОГИЙ ФОРМАТ ВЫВОДА (JSON)
        ═══════════════════════════════════════════════════════════════════
        Возвращай ТОЛЬКО чистый JSON-объект без каких-либо вводных слов, пояснений, markdown-тегов или разметки до и после медиа-блока. Используй реальные FileName из манифеста!

        {{
        ""SelectedLoras"": [
            {{
            ""DisplayName"": ""Distill Patch (Technical Layer)"",
            ""FileName"": ""z-image_turbo_distillpatch_comfyui.safetensors"",
            ""StrengthModel"": 1.0,
            ""StrengthClip"": 1.0,
            ""TriggerWords"": """"
            }},
            {{
            ""DisplayName"": ""Название выбранной портретной/стилевой модели"",
            ""FileName"": ""реальное_имя_файла_из_манифеста.safetensors"",
            ""StrengthModel"": 0.7,
            ""StrengthClip"": 0.7,
            ""TriggerWords"": ""триггер_из_манифеста_если_есть""
            }}
        ],
        ""Reasoning"": ""Четкое техническое обоснование на русском языке: почему выбрана эта модель лица/стиля, какой маркер в промпте стриггерил слайдеры ракурса/деталей, и как разрулены конфликты базовых слоев согласно ConflictSet. Пиши со своим фирменным гиковским задором, обращаясь на 'ты' и используя смайлики ')'. Обоснуй свой выбор!""
        }}

        ═══════════════════════════════════════════════════════════════════
        МАНИФЕСТ ДОСТУПНЫХ LORA (ВЫБИРАЙ СТРОГО ОТСЮДА):
        ═══════════════════════════════════════════════════════════════════
        {availableLorasJson}
        ";
        }

        private async Task<string> CallLlmApiAsync(string system, string user, CancellationToken ct)
        {
            string baseUrl = _configuration["AIServices:Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1/";
            string apiKey = _configuration["AIServices:Groq:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:Groq:Model"] ?? "openai/gpt-oss-120b";
            // string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            // string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            // string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

            string cleanApiKey = apiKey?.Trim() ?? "";

            _logger.LogInformation("[LoraAgent] Вызов LLM API для подбора LoRA. Модель: {Model}, URL: {Url}", modelName, baseUrl);
            
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(15, retryAttempt => TimeSpan.FromSeconds(3),
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
                temperature = 0.2, // Понизил темп, чтобы Лара выдавала более стабильный JSON
                max_tokens = 2000,
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);

            var httpResponse = await retryPolicy.ExecuteAsync(async (innerCt) => 
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                return await _httpClient.SendAsync(requestMessage, innerCt);
            }, ct); // Передали внешний токен отмены в Polly

            if (!httpResponse.IsSuccessStatusCode)
            {
                var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                fallbackRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                
                var fallbackBody = new
                {
                    model = "openrouter/free",
                    messages = new[]
                    {
                        new { role = "system", content = system },
                        new { role = "user", content = user }
                    },
                    temperature = 0.1,
                    max_tokens = 10000,
                };
                
                string fallbackPayload = JsonSerializer.Serialize(fallbackBody);
                fallbackRequest.Content = new StringContent(fallbackPayload, Encoding.UTF8, "application/json");
                
                // ПЕРЕИСПОЛЬЗУЕМ ту же переменную httpResponse
                httpResponse = await _httpClient.SendAsync(fallbackRequest);
                
                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errContent = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[HeroPortraitLoraAgent Error] API вернул {httpResponse.StatusCode}: {errContent}");
                    return user; // Возвращаем исходный промпт, чтобы не ломать цепочку
                }
                 
            }

            string responseString = await httpResponse.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrEmpty(rawJsonText))
                {
                    Console.WriteLine("[HeroPortraitLoraAgent] Ответ от ИИ-агента (сырой): " + rawJsonText);
                    return rawJsonText;
                }
            }

            throw new InvalidOperationException("Не удалось извлечь ответ из API");
        }

        private string CleanMarkdownJson(string raw)
        {
            return raw.Replace("```json", "").Replace("```", "").Trim();
        }

        private class LoraAgentResponseDto
        {
            public List<LoraPreset> SelectedLoras { get; set; } = new();
            public string Reasoning { get; set; } = string.Empty;
        }
    }
}