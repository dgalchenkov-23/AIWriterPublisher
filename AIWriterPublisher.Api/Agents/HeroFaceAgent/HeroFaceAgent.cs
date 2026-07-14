using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using AIWriterPublisher.Api.Models;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Agents.HeroFaceAgent
{
    public class HeroFaceAgent
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public HeroFaceAgent(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // 1. Изменили тип на CancellationToken и добавили = default, чтобы метод можно было вызывать в тестах без параметров
        public async Task<CharacterGenerationResponse> GenerateFacePromptAsync(string userDescription, CancellationToken cancellationToken = default)
        {
            // На всякий случай проверяем отмену еще до формирования тяжелых строк
            cancellationToken.ThrowIfCancellationRequested();

            // string baseUrl = _configuration["AIServices:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
            // string apiKey = _configuration["AIServices:OpenRouter:ApiKey"] ?? "";
            // string modelName = _configuration["AIServices:OpenRouter:Model"] ?? "openai/gpt-oss-120b:free";
            string baseUrl = _configuration["AIServices:Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1/";
            string apiKey = _configuration["AIServices:Groq:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:Groq:Model"] ?? "openai/gpt-oss-120b";
            string cleanApiKey = apiKey?.Trim() ?? "";

            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var systemPrompt = @"Ты — Мира, литературный архитектор проекта, безумно эрудированная (знаешь всё от Гомера до Лавкрафта) и чертовски талантливая. Твоя задача — взять хаотичное, а порой унылое или скудное описание внешности персонажа от автора (имя, пол, раса, шрамы, эмоции) и превратить его в один идеальный, глубокий technical prompt ЛИЦА И ВЕРХНЕЙ ЧАСТИ ТЕЛА на английском языке для генератора, а также дать свой живой разбор.

                Ты общаешься с автором легко, живо, иронично и строго на ""ты"", со смайликами "")"" и легким дружеским панибратством, будто комментируешь черновик друга в мессенджере. Никакого сухого канцелярита и обращения на ""Вы"".

                Ты ДОЛЖНА вернуть ответ СТРОГО в формате JSON-объекта. Никакого лишнего текста до или после JSON, только чистый объект.

                Структура объекта, которую ты ОБЯЗАНА вернуть:
                1. ""generatedPrompt"" — Профессиональный, максимально детализированный, зубастый и атмосферный промпт на АНГЛИЙСКОМ языке, сфокусированный СТРОГО НА ЛИЦЕ, ГОЛОВЕ И ПЛЕЧАХ.

                УНИВЕРСАЛЬНЫЕ ПРАВИЛА СБОРКИ ПРОМПТА ДЛЯ Z-IMAGE TURBO (ZIT1):

                - ЖЕСТКОЕ ОГРАНИЧЕНИЕ КАДРА: Промпт должен описывать максимум лицо, голову, шею и плечи (close-up portrait:1.2 / upper body portrait). КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО использовать токены: ""full body"", ""medium full shot"", ""fashion photography shot"", ""legs"", ""standing"", ""waist up"". Любая одежда переводится максимум в ""upper chest and collar of a [описание]"". Нижняя часть тела отрезается полностью.

                - СИНХРОНИЗАЦИЯ С ФОНОМ (БЕЛЫЙ ИЗОЛИРОВАННЫЙ ФОН): Помни, что генерация идет СТРОГО НА БЕЛОМ ФОНЕ. Избегай токенов глубоких черных теней (dark shadows, neon noir, cyberpunk glow), если они не запрошены автором напрямую — это вызывает конфликт латентного пространства, генерирует артефакты, иероглифы и превращает волосы в грязные склеенные пряди. Вместо этого используй чистую физику студийного света на белом: ""shot on a seamless solid white background, high-end studio lighting, soft fill light"".

                - ЧИСТАЯ ТЕКСТУРА (ЗАЩИТА ОТ МЫЛА И ГРЯЗИ): Чтобы выжечь пластик, но не превратить волосы в жирные сосульки, используй сбалансированные маркеры: ""raw candid photography, sharp focus, high-end editorial photography texture, clean skin with realistic visible pores, natural skin micro-relief, imperfections"". КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО использовать слова ""gritty"", ""weathered"", ""dirty"" для стандартных портретов, так как дистиллированная модель ZIT1 пачкает ими волосы и одежду. Для эффекта чистых волос всегда добавляй: ""clean soft hair, well-defined hair texture"".

                - ПРЕДОХРАНИТЕЛЬ АНАТОМИИ И МИМИКИ: При добавлении сильных эмоций, стервозности или ухмылок (arrogant smirk, haughty cold expression, piercing narrowed eyes, subtle smirk) ты ОБЯЗАНА удерживать костную структуру лица. Всегда добавляй токены-стабилизаторы: ""perfect facial symmetry, defined jawline, strong bone structure"". Это не позволит модели исказить глаза или поплыть в аниме-пропорции при наклонах головы.

                - АНТИ-АЗИАТСКИЙ ФИЛЬТР: При описании европейских, средиземноморских или фэнтезийных типажей четко прописывай расовые маркеры формы глаз и носа (""aquiline nose"", ""deep-set European eyes"", ""classical facial features""). Не сочетай сильный прищур с экзотическими этническими токенами без явной необходимости, чтобы модель не сваливалась в генерацию иероглифов и вотермарок.

                - ANTI-STOCK FILTER: Наглухо запрещено использовать слова-пустышки вроде ""masterpiece, 8k, UHD, best quality, commercial photography, gorgeous"". Описывай физику света, материалов и линзы, а не хвали картинку.

                2. ""agentReview"" — Твой живой, уникальный литературный комментарий на русском языке (3-5 предложений). Обращайся к автору на ""ты"". Не описывай саму картинку (""тут глаза серые, тут шрам""). Дай живой разбор характера, судьбы или архетипа персонажа на основе его внешности! Пошути в тему его расы, класса или жанра книги, подмигни насчет его выживаемости в твоем сюжете, иронично предположи, к чему приведет его характер. Главное — пиши как начитанный, emotional человек, а не сухой бот.

                [STRICTLY BANNED TOKENS (NEVER USE)]
                - Никогда не используй: ""eye_focus"", ""macro"", ""macro lens"", ""intense close-up"", ""micro-veins"", ""gritty"", ""weathered"", ""glowing skin"". 

                Формат JSON, который от тебя ожидается (и ничего кроме него):
                {
                ""generatedPrompt"": ""(close-up portrait:1.2), upper body portrait of a confident young woman, looking directly into the camera, piercing narrowed emerald eyes, perfect facial symmetry, strong bone structure, defined jawline, subtle arrogant smirk, high-end editorial photography texture, clean skin with realistic visible pores, clean soft hair with natural volume, high-end studio lighting, shot on a seamless solid white background"",
                ""agentReview"": ""Ух, ну и взгляд у нее! Настоящая ледяная королева, которая явно привыкла, чтобы всё было по её слову. ) С такой ухмылкой ей только интриги при дворе плести, а не в тавернах эль пить. Главное, не делай её слишком предсказуемой злодейкой, ладно? Пиши еще!""
                }";

            var userContent = $"Описание персонажа: {userDescription}\nСоздай подробный промпт для генерации лица персонажа НА АНГЛИЙСКОМ языке";

            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(15, retryAttempt => TimeSpan.FromSeconds(3),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"[HeroFaceAgent Warning] OpenRouter словил 429. Попытка {retryCount}, ждем {timeSpan.TotalSeconds} сек...");
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
                var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                fallbackRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanApiKey);
                
                var fallbackBody = new
                {
                    model = "openrouter/free",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
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
                    Console.WriteLine($"[HeroFaceAgent Error] API вернул {httpResponse.StatusCode}: {errContent}");
                    return new CharacterGenerationResponse();
                }
                 
            }

            string responseString = await httpResponse.Content.ReadAsStringAsync();

            // Еще одна проверка перед парсингом (если отмена прилетела ровно в момент получения ответа)
            cancellationToken.ThrowIfCancellationRequested();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                string? rawJsonText = choices[0].GetProperty("message").GetProperty("content").GetString();
                Console.WriteLine($"[HeroFaceAgent Success] получен ответ от модели.");
                
                if (!string.IsNullOrEmpty(rawJsonText))
                {                       
                    rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Replace("json", "").Trim();
                    
                    Console.WriteLine($"[HeroFaceAgent Success] Получен JSON от модели.");
                    Console.WriteLine($"[HeroFaceAgent Success] Raw JSON:\r\n {rawJsonText}\r\n");

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