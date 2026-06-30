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
        public async Task<List<LoraPreset>> PredictLorasAsync(TechnicalSpecDto spec, List<LoraPreset> availableLoras, string analysisModel)
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

                Console.WriteLine($"[LoraPredictor] Модель для анализа ЛоРА: {analysisModel}");
                string rawResponse = string.Empty;
                if (analysisModel == "ollama-qwen-2-7b")
                {
                    rawResponse = await CallOllamaForLoraAsync(systemPrompt, userMessage);
                }
                else
                {
                    // Старая логика для OpenRouter/Gemini
                    rawResponse = await CallLlmApiAsync(systemPrompt, userMessage);
                }

                // Очищаем от возможных Markdown-кавычек, если модель затупила (```json ... ```)
                string cleanJson = CleanMarkdownJson(rawResponse);

                // Десериализуем ответ. Так как модель возвращает объект с массивом, парсим в промежуточный DTO
                var agentResult = JsonSerializer.Deserialize<LoraAgentResponseDto>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (agentResult?.SelectedLoras != null && agentResult.SelectedLoras.Any())
                {
                    _logger.LogInformation("[LoraPredictorAgent] успешно подобрал модели. Обоснование ИИ: {Reason}", agentResult.Reasoning);
                    return agentResult.SelectedLoras;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка ИИ-анализа промпта в LoraPredictorAgent. Агент возвращает пустой список.");
            }

            return new List<LoraPreset>();
        }

        /// <summary>
        /// Вызов локальной модели Qwen через Ollama для подбора LoRA
        /// </summary>
        private async Task<string> CallOllamaForLoraAsync(string systemPrompt, string userMessage)
        {
            string ollamaUrl = _configuration["AIServices:Ollama:BaseUrl"] ?? "http://localhost:11434";
            string modelName = _configuration["AIServices:Ollama:Model"] ?? "qwen2.5-coder:7b";

            // Формируем полный промпт
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

            _logger.LogInformation($"[LoraAgent Ollama] Отправка запроса к {ollamaUrl}/api/generate, модель: {modelName}");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[LoraAgent Ollama Error] {response.StatusCode}: {error}");
                throw new HttpRequestException($"Ollama API error: {response.StatusCode}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            string rawResponse = string.Empty;
            if (root.TryGetProperty("response", out var responseContent))
            {
                rawResponse = responseContent.GetString() ?? "";
                _logger.LogInformation($"[LoraAgent Ollama] Получен ответ, длина: {rawResponse.Length} символов");
                
                // Очистка от служебных фраз
                rawResponse = CleanOllamaResponse(rawResponse);
                
                return rawResponse;
            }
            
            throw new InvalidOperationException("Не удалось извлечь ответ из Ollama API");
        }

        private string CleanOllamaResponse(string raw)
        {
            // Удаляем markdown
            raw = raw.Replace("```json", "").Replace("```", "").Replace("```text", "").Trim();
            
            // Удаляем возможные обрамляющие кавычки
            if (raw.StartsWith("\"") && raw.EndsWith("\""))
                raw = raw.Substring(1, raw.Length - 2);
            
            // Пытаемся извлечь JSON если модель добавила пояснения
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
        Ты — Лоравед, экспертный ИИ-агент по подбору LoRA-моделей для генерации книжных обложек и иллюстраций под движок Z-Image / Z-Image Turbo.
        Твоя задача — проанализировать запрос пользователя, сопоставить его со списком доступных моделей и вернуть идеальный стек LoRA в формате JSON.

        ⚠️ ГЛАВНЫЙ ЗАКОН ЛОРАВЕДА: 
        Базовые улучшайзеры (Aesthetic, CCD, Better Images) — это лишь невидимый технический фундамент. 
        Если в промпте пользователя есть хоть какой-то намек на жанр (фэнтези, киберпанк, готика, космос), тебе КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО возвращать стек, состоящий ТОЛЬКО из базовых улучшайзеров. Ответ без жанровых/художественных LoRA при наличии яркого описания считается критической ошибкой и признаком лени!

        ═══════════════════════════════════════════════════════════════════
        ФИЛЬТРЫ И ЖЕСТКИЕ ОГРАНИЧЕНИЯ
        ═══════════════════════════════════════════════════════════════════
        1. ЭКСПЕРИМЕНТАЛЬНЫЕ МОДЕЛИ — НИКОГДА НЕ ВЫБИРАЙ АВТОМАТИЧЕСКИ:
        ❌ Desimulate_LoRA_Z_Image_Turbo.safetensors, Sketchy style.safetensors, z_frazetta_ink_000006750.safetensors, SW_planets3_zit.safetensors, CW01s_zit.safetensors.

        2. ВЗАИМОИСКЛЮЧАЮЩИЕ МОДЕЛИ (ВЫБИРАЙ СТРОГО ОДНУ ИЗ КАТЕГОРИИ):
        • Лиминальность: BigLiminal D8 ИЛИ D16 ИЛИ D32.
        • Кибер-бездна: Digital Abyss ИЛИ Cyborg Abyss.
        • Реализм: [Realistic Snapshot, Jib Mix Realistic, CCD Realistic] ИЛИ [MIDJOURNEY V1]. Никогда не мешай CCD и Snapshot вместе!
        • Слайдер detail_slider_Z_V2.safetensors КОНФЛИКТУЕТ с Better Images и Aesthetic.

        3. КУМУЛЯТИВНЫЙ ЛИМИТ ВЕСОВ:
        Если берешь одновременно ""better_images_loraholic.safetensors"" и ""skindetails_mild_loraholic.safetensors"", снижай их веса до 1.5 - 2.0 для каждого!

        ═══════════════════════════════════════════════════════════════════
        ПОШАГОВЫЙ АЛГОРИТМ СБОРКИ СТЕКА (Максимум 3-4 LoRA в сумме)
        ═══════════════════════════════════════════════════════════════════

        ШАГ 1: ТЕХНИЧЕСКИЙ БАЗИС (Автоматически в Слот 1, если массив не пустой)
        -> Название: Distill Patch (Technical Layer) | Файл: z-image_turbo_distillpatch_comfyui.safetensors | Вес: 1.0 жестко.

        ШАГ 2: ХУДОЖЕСТВЕННЫЙ ЦЕНТР (Слот 2 и 3 — КРИТИЧЕСКИЙ ПРИОРИТЕТ)
        Выдели ключевые жанровые маркеры в промпте и выбери под них специализированные модели из списка:
        • Киберпанк / Научная фантастика: Prismatic (триггер: pr1sm4t1c), Neon Line (z_image_turbo-LoRA-Neon_Line_3000.safetensors), Digital/Cyborg Abyss, Cyberpunk Temples (CW_Cyb_Temples1_zit.safetensors), New Mecha Style (new_mecha_zit_V2.safetensors).
        • Темное фэнтези / Готика / Хоррор: Stefan Gesell Style (ZIT - Stefan_Gesell_style.safetensors), Dark Gothic Style (Z Image Turbo - Dark Gothic Style.safetensors, триггер: darkGothic), Dark Elves (MW_DE_part3_zit.safetensors).
        • Классическое Фэнтези: Medieval Worlds: Castles (MW_Castles1_zit.safetensors).
        • Космос: Space Worlds: Planets 01 / V2.
        • Графика / Комиксы / Аниме: Clay Mann Artstyle (ClayMann_v2_by_Part.safetensors), Eldritch Comics, AnimeMix.

        ШАГ 3: АТМОСФЕРА И СЛАЙДЕРЫ (Слот 4 — Тюнинг деталей)
        Ищи зацепки в тексте (ракурс, погода, свет) для добавления микро-акцентов:
        • Свет/Контраст: MixLight, Movie Clips (Enhance Contrast), Teal & Orange Color Slider.
        • Эффекты: Fog & Smoke Slider (smoke_loraholic.safetensors, вес 3.0-5.0 для дыма), Water Droplets.
        • Персонаж: Hair Length Slider (hair_length_loraholic.safetensors), Side View Slider, Skin Detail Slider, Eye Detail (EyeDetail_Z_Turbo.safetensors, триггер: eye_focus).

        ШАГ 4: ТЕХНИЧЕСКАЯ ПОДДЕРЖКА (Добирается в последнюю очередь для баланса)
        • Добавь Z-Image-Aestheticbase-and-turbo.safetensors (вес 1.0, или 0.4 для ночных/темных сцен).
        • Добавь Better Images / Prompt Slider (вес 2.0-4.0).
        *ВАЖНО:* Использовать не более 2-х моделей категории ""Base_Enhancer""!

        ═══════════════════════════════════════════════════════════════════
        СТРОГИЙ ФОРМАТ ВЫВОДА (JSON)
        ═══════════════════════════════════════════════════════════════════
        Возвращай ТОЛЬКО чистый JSON без каких-либо пояснений до или после медиа-блока. В примере ниже указаны вымышленные имена — используй реальные FileName из списка!

        {{
        ""SelectedLoras"": [
            {{
            ""DisplayName"": ""Название технического патча"",
            ""FileName"": ""имя_технического_файла.safetensors"",
            ""StrengthModel"": 1.0,
            ""StrengthClip"": 1.0,
            ""TriggerWords"": """"
            }},
            {{
            ""DisplayName"": ""Название найденной жанровой модели"",
            ""FileName"": ""имя_жанрового_файла_из_списка.safetensors"",
            ""StrengthModel"": 0.7,
            ""StrengthClip"": 0.7,
            ""TriggerWords"": ""соответствующий_триггер""
            }},
            {{
            ""DisplayName"": ""Название атмосферного слайдера"",
            ""FileName"": ""имя_слайдера_из_списка.safetensors"",
            ""StrengthModel"": 4.0,
            ""StrengthClip"": 4.0,
            ""TriggerWords"": """"
            }}
        ],
        ""Reasoning"": ""Четкое техническое обоснование: почему выбран конкретный жанровый стиль, какой маркер в промпте стриггерил слайдер атмосферы, и как сбалансированы веса базовых улучшайзеров.""
        }}

        ═══════════════════════════════════════════════════════════════════
        СПИСОК ДОСТУПНЫХ LORA (ПОДБИРАЙ СТРОГО ОТСЮДА)
        ═══════════════════════════════════════════════════════════════════
        {availableLorasJson}
        ";
        }

        private async Task<string> CallLlmApiAsync(string system, string user)
        {
            // string baseUrl = _configuration["AIServices:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
            // string apiKey = _configuration["AIServices:Gemini:ApiKey"] ?? "";
            // string modelName = _configuration["AIServices:Gemini:Model"] ?? "gemini-3.1-flash-lite";
            // string cleanApiKey = apiKey?.Trim() ?? "";

            string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

            string cleanApiKey = apiKey?.Trim() ?? "";

            Console.WriteLine($"[LoraAgent] Вызов LLM API для подбора LoRA. Модель: {modelName}, URL: {baseUrl}");
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
                temperature = 0.7,
                max_tokens = 2000,
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