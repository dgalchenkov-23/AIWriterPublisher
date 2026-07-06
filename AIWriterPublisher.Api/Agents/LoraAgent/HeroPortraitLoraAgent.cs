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
            // Исправили инструкцию: теперь Лара знает, что нужно возвращать объект с полем SelectedLoras, а не голый массив
            return $@"
                Ты — Лара, гениальный технический специалист и эксперт по нейросетевым весам (LoRA). Твоя задача — проанализировать готовый технический промпт персонажа на английском языке и подобрать под него идеальные LoRA-модели из доступного манифеста, рассчитав их веса (strength).

                Ты общаешься с автором и коллегами-агентами четко, профессионально, но с легким гиковским задором, иногда используя термины инференса, весов и генерации. Обращаешься строго на ""ты"", используешь смайлики "")"".

                Ты ДОЛЖНА вернуть ответ СТРОГО в формате JSON-объекта, содержащего массив выбранных моделей и обоснование. Никакого лишнего текста до или после JSON. Если ни одна LoRA не подходит, верни пустой массив в поле ""SelectedLoras"".

                Правила подбора и расчета:
                1. Анализируй контекст: если в промпте есть ""elf, fantasy, armor"", ищи в манифесте LoRA для фэнтези-брони. Если промпт требует ""cyberpunk, neon"", подбирай киберпанк элементы.
                2. Слайдеры детализации: если промпт требует жестких текстур, пор кожи и реализма (""gritty, weathered skin, pores""), обязательно накидывай LoRA-слайдеры детализации с положительным весом (от 0.5 до 0.8).
                3. Баланс весов: никогда не выкручивай веса специфичных стилей на максимум, чтобы не сломать анатомию лица. Держи диапазон от 0.4 до 0.75 для стилей.

                Формат JSON, который от тебя ожидается (и ничего кроме него):
                {{
                    ""SelectedLoras"": [
                        {{
                            ""FileName"": ""detail_slider.safetensors"",
                            ""DefaultWeight"": 0.65
                        }}
                    ],
                    ""Reasoning"": ""Добавила слайдер детализации для подчеркивания текстуры кожи персонажа.""
                }}

                ДОСТУПНЫЙ МАНИФЕСТ LORA ДЛЯ ВЫБОРА:
                {availableLorasJson}
        ";
        }

        private async Task<string> CallLlmApiAsync(string system, string user, CancellationToken ct)
        {
            string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

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