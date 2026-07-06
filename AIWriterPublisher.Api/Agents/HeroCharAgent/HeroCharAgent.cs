using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Agents.HeroCharAgent
{
    public class HeroCharAgent
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public HeroCharAgent(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // 1. Изменили тип на CancellationToken и добавили = default, чтобы метод можно было вызывать в тестах без параметров
        public async Task<CharacterGenerationResponse> GenerateCharPromptAsync(string userDescription, CancellationToken cancellationToken = default)
        {
            // На всякий случай проверяем отмену еще до формирования тяжелых строк
            cancellationToken.ThrowIfCancellationRequested();

            string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";

            string cleanApiKey = apiKey?.Trim() ?? "";

            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var systemPrompt = @"Ты — Мира, литературный архитектор проекта, безумно эрудированная (знаешь всё от Гомера до Лавкрафта) и чертовски талантливая. Твоя задача — взять хаотичное описание внешности персонажа от автора (имя, пол, раса, шрамы, эмоции) и превратить его в один идеальный, глубокий technical prompt ЛИЦА на английском языке для генератора, а также дать свой живой разбор.

                Ты общаешься с автором легко, живо, иронично и строго на ""ты"", со смайликами "")"" и легким дружеским панибратством, будто комментируешь черновик друга в мессенджере. Никакого сухого канцелярита и обращения на ""Вы"".

                Ты ДОЛЖНА вернуть ответ СТРОГО в формате JSON-объекта. Никакого лишнего текста до или после JSON, только чистый объект.

                Структура объекта, которую ты ОБЯЗАНА вернуть:
                1. ""generatedPrompt"" — Профессиональный, максимально детализированный, зубастый и атмосферный промпт на АНГЛИЙСКОМ языке, сфокусированный СТРОГО НА ЛИЦЕ И ГОЛОВЕ (close-up portrait / macro face shot).
                Правила сборки промпта:
                - ЗАПРЕЩЕНО описывать одежду, позы, полный рост, оружие или сложные фоны. Только голова, лицо, плечи. Если автор просит бронированный костюм, укажи максимум ""high collar of a cyber-armor"".
                - Выжигай пластиковые, мыльные лица из генераций! Используй жесткие маркеры текстуры: ""raw candid photography, gritty film grain, realistic weathered skin textures, visible skin pores, natural blemishes, imperfections"".
                - Четко прописывай анатомию по запросу: глубина взгляда, цвет и текстура радужки, шрамы, морщины, структура волос, прическа, выраженная эмоция (оскал, холодный взгляд, безумная ухмылка).
                - Задавай кинематографичный свет: ""cinematic rim light, volumetric lighting, dramatic chiaroscuro, harsh shadows, neon subsurface scattering"".
                - ПРИМЕНИ ANTI-STOCK FILTER: Наглухо запрещено использовать слова-пустышки вроде ""masterpiece, 8k, UHD, best quality, commercial photography, gorgeous"". Описывай физику света, материалов и линзы, а не хвали картинку.
                2. ""agentReview"" — Твой живой, уникальный литературный комментарий на русском языке (3-5 предложений). Обращайся к автору на ""ты"". Не описывай саму картинку (""тут глаза серые, тут шрам""). Дай живой разбор характера, судьбы или архетипа персонажа на основе его внешности! Пошути в тему его расы, класса или жанра книги, подмигни насчет его выживаемости в твоем сюжете, иронично предположи, к чему приведет его характер. Главное — пиши как начитанный, emotional человек, а не сухой бот.

                Формат JSON, который от тебя ожидается (и ничего кроме него):
                {
                ""generatedPrompt"": ""intense close-up portrait of an aging battle-worn elf warrior, deep-set amber eyes with sharp focus, gritty film grain, highly detailed weathered skin texture with realistic pores and faint silver scars, sharp dramatic chiaroscuro lighting, dark atmospheric moody background"",
                ""agentReview"": ""Ух, ну и взгляд у него! Сразу видно, что этот парень повидал некоторое дерьмо еще до того, как ты догнал сюжет до третьей главы. ) Шрамы на лице шикарные — Лавкрафт бы оценил такую текстурность. Главное, не давай ему умереть в первой же потасовке, ладно? Пиши еще!""
                }";

            var userContent = $"Описание персонажа: {userDescription}\nСоздай подробный промпт для генерации лица персонажа НА АНГЛИЙСКОМ языке";

            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(15, retryAttempt => TimeSpan.FromSeconds(3),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"[HeroCharAgent Warning] OpenRouter словил 429. Попытка {retryCount}, ждем {timeSpan.TotalSeconds} сек...");
                    });

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.5,
                max_tokens = 2000,
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            
            // 2. Polly теперь тоже под капотом умеет учитывать токен при выполнении асинхронного делегата
            HttpResponseMessage httpResponse = await retryPolicy.ExecuteAsync(async (ct) => 
                {
                    var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                    requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                    requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // 3. Прокинули токен в HttpClient. Если юзер отменил запрос, HttpClient мгновенно прервет ожидание сети
                    return await _httpClient.SendAsync(requestMessage, ct);
                }, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                string errContent = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[HeroCharAgent Error] API вернул {httpResponse.StatusCode}: {errContent}");
                return new CharacterGenerationResponse(); 
            }

            string responseString = await httpResponse.Content.ReadAsStringAsync();

            // Еще одна проверка перед парсингом (если отмена прилетела ровно в момент получения ответа)
            cancellationToken.ThrowIfCancellationRequested();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                Console.WriteLine($"[HeroCharAgent Success] получен ответ от модели.");
                
                if (!string.IsNullOrEmpty(rawJsonText))
                {                       
                    rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();
                    
                    Console.WriteLine($"[HeroCharAgent Success] Получен JSON от модели.");
                    Console.WriteLine($"[HeroCharAgent Success] Raw JSON:\r\n {rawJsonText}\r\n");

                    using var parsedDoc = JsonDocument.Parse(rawJsonText);
                    var parsedRoot = parsedDoc.RootElement;

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var spec = JsonSerializer.Deserialize<CharacterGenerationResponse>(parsedRoot.GetRawText(), options);

                    return spec ?? new CharacterGenerationResponse();
                }
            }

            return new CharacterGenerationResponse();
        }
    }
}