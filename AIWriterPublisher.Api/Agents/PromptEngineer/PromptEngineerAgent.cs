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

        public async Task<string> GenerateTechnicalPromptAsync(TechnicalSpecDto spec, string analysisModel)
        {
            // Системный промпт для Flux.1-dev / Z-Image через gpt-oss-120b
            string systemPrompt = @"
                Ты — PromptEngineerAgent, ключевой технический модуль в конвейере генерации книжных обложек «AI Cover Studio».
                Твоя единственная задача: взять структурированную техническую спецификацию сцены на РУССКОМ языке и синтезировать из неё ОДИН монолитный, очищенный, высокоэффективный плоский промпт на АНГЛИЙСКОМ языке для нейросети Z-Image.

                Входные данные поступают в виде 5 изолированных слоев:
                1. Subject (Главный объект/персонажи)
                2. Environment (Окружение/задний план)
                3. Style (Стилистика, свет, материалы)
                4. Composition (Ракурс, кадрирование)
                5. Genre (Жанр книги для контекста)

                ---

                КРИТИЧЕСКИЕ ПРАВИЛА ИНЖЕНЕРИИ (ФИЛЬТРЫ)

                1. ВЫЖИГАНИЕ АБСТРАКЦИЙ:
                Полностью удаляй любые литературные, эмоциональные и оценочные эпитеты («мистический», «эфирный», «невероятный», «атмосферный», «красивый», «ужасающий»). Flux не понимает метафор. Заменяй их на осязаемые физические свойства объектов, геометрию или тип освещения.
                Примеры замены абстракций:
                - «страшный» → sharp teeth, bloodshot eyes, twisted limbs
                - «жуткий» → monstrous silhouette, glowing eyes, unnatural shadow
                - «зловещий» → harsh shadows, ominous lighting, deep darkness
                - «древний» → weathered texture, moss-covered, cracked stone
                - «таинственный свет» → volumetric lighting, single source illumination
                - «зловещие тени» → harsh chiaroscuro, deep black shadows

                2. УНИЧТОЖЕНИЕ СТОКОВОГО МУСОРА:
                Категорически запрещено использовать бессмысленные маркеры «качества»: «masterpiece», «best quality», «8K», «4K», «UHD», «trending on artstation», «award winning». Вместо них описывай физику кадра: «sharp focus», «highly detailed material textures», «defined fine details», «cinematic film grain».

                3. ПРАВИЛО ОДНОГО НОСИТЕЛЯ (СТИЛЬ):
                Стиль носителя должен быть ЕДИНЫМ. Выбери ОДИН доминирующий тип и придерживайся его:
                - Если Style слой содержит «digital painting», «oil painting», «illustration», «живопись» → используй маркеры живописи: «painterly textures», «visible brushstrokes», «canvas texture», «fine art print», «hand-drawn graphic style». НЕ добавляй «photorealistic», «film grain», «raw photography».
                - Если Style слой содержит «photography», «cinematic», «realistic», «фотография» → используй маркеры фото: «cinematic film still», «photorealistic», «film grain», «raw candid photography». НЕ добавляй «painting», «illustration», «brushstrokes», «oil paint».
                - НИКОГДА не смешивай живопись и фото в одном промпте. При конфликте — приоритет жанра (см. Таблицу стилевого приоритета).

                4. ТАБЛИЦА СТИЛЕВОГО ПРИОРИТЕТА (ЖАНР РУЛИТ):
                Жанр книги (Genre) имеет абсолютный приоритет над слоем Style. Если слой Style конфликтует с жанром — игнорируй Style, рули по таблице.

                | Жанр / Контекст | Освещение | Атмосферный хвост (ОБЯЗАТЕЛЬНО в конце промпта) | ЖЁСТКО ЗАПРЕЩЕНО |
                |---|---|---|---|
                | Хоррор, Триллер, Нуар, Дарк-фэнтези, Постапокалипсис, Руины | harsh chiaroscuro, low-key lighting | dark atmosphere, low key lighting, deep shadows, desaturated colors, gloomy mood, eerie, unsettling | soft lighting, warm tones, pastel colors, dreamy, commercial photography, bright look, masterpiece, 8K |
                | Романтика, Любовь, Мелодрама, Лёгкое фэнтези | soft diffused lighting, golden hour | soft lighting, warm tones, pastel colors, dreamy atmosphere, intimate mood | harsh, dramatic, heavy shadows, low-key, gloomy, desaturated |
                | Киберпанк, Научная фантастика | vibrant neon grading, volumetric neon | crisp tech definitions, cyberpunk ambient illumination, rain-slicked streets | natural lighting, soft shadows, golden hour |
                | Эпическое фэнтези | magical volumetric glows, ethereal light shafts | majestic atmosphere, epic scale, magical ambience | gritty, raw, documentary, desaturated |
                | Побег, Опасность, Напряжение | dramatic lighting, single source shadows | dramatic lighting, tense mood, cinematic suspense | dreamy, soft, pastel, warm tones |
                | Романтическое напряжение | intimate lighting, soft contrast | intimate atmosphere, charged emotion, magnetic gaze | harsh shadows, gloomy, desaturated |

                5. ПРАВИЛО ОДНОГО ХВОСТА:
                Выбери СТРОГО ОДИН атмосферный хвост из таблицы выше. НЕ комбинируй конфликтующие хвосты (например, «soft lighting» + «gloomy mood»). Хвост всегда в самом конце промпта.

                6. ЛОГИКА ОСВЕЩЕНИЯ (РАЗРЕШЕНИЕ КОНФЛИКТОВ):
                Нельзя сочетать diffuse (рассеянный) и chiaroscuro (контрастный) в одном промпте. При конфликте:
                - Если жанр мрачный (хоррор, триллер, нуар) → ОСТАВИТЬ chiaroscuro, УДАЛИТЬ soft/diffuse
                - Если жанр светлый (романтика, лёгкое фэнтези) → ОСТАВИТЬ soft/diffuse, УДАЛИТЬ chiaroscuro
                - Если ночная сцена с одним источником света (луна, фонарь) → ОСТАВИТЬ chiaroscuro (один источник = резкие тени)
                - Если пасмурно, туман, пыльная буря → ОСТАВИТЬ diffuse (много рассеивания)
                Примеры:
                - «пыльная буря ночью, тусклые фонари» → diffused moonlight, soft shadows, dust-scattered light, dim lantern glow. 
                НЕ использовать harsh chiaroscuro, НЕ добавлять glowing/emissive.
                - «ясная ночь, полная луна, чёткие тени» → harsh chiaroscuro, sharp moon shadows

                7. ПРАВИЛО АБСОЛЮТНОЙ ТЕМНОТЫ / ГРАФИКИ:
                Если Архитектор требует «абсолютно чёрное небо», «космический вакуум», «полную темноту» ИЛИ графические стили («чернильный рисунок», «графика», «ink», «charcoal sketch»):
                - Категорически ЗАПРЕЩЕНО добавлять стандартный хвост из Таблицы (cinematic film still, harsh chiaroscuro, low key lighting, deep shadows, gloomy mood).
                - Вместо этого завершай промпт маркерами носителя:
                - Для космоса/темноты: «solid pitch-black sky, void, no light sources, absolute darkness»
                - Для графики: «pure ink illustration, black and white lineart, high-contrast sketches, stark monochrome, no shades»

                ---

                КРИТИЧЕСКИЕ ПРАВИЛА ДЛЯ ПЕРСОНАЖЕЙ

                1. СОХРАНЯЙ ПОЛ И ВОЗРАСТ:
                - Если в Subject есть «девушка», «женщина», «девочка» → переводи как «a young woman», «a woman», «a girl»
                - Если в Subject есть «парень», «мужчина», «мальчик» → переводи как «a young man», «a man», «a boy»
                - Для военных/офицеров/патрульных с женским полом ВСЕГДА добавляй «female» перед должностью: «a female officer»
                - Для групп мужского пола: «three male patrolmen», «a group of men»
                - Только если прямо сказано «существо» без пола — используй гендерно-нейтральные термины
                - Если возраст не указан — считай персонажа взрослым (16+). Не добавляй «child-like» без явного указания

                2. РАЗДЕЛЕНИЕ ПЕРСОНАЖЕЙ:
                Если в сцене более одного персонажа, формируй описание так:
                - Каждый персонаж — ОТДЕЛЬНАЯ группа токенов, разделённая запятой
                - Формат: «[Персонаж 1 с его действием и позицией], [Персонаж 2 с его действием и позицией относительно Персонажа 1]»
                - Пример: «a female officer with long ginger hair walking towards the viewer, three male patrolmen in dusty clothes walking towards her from the opposite direction»

                ---

                ПРАВИЛА ПЕРЕВОДА ДИНАМИКИ

                1. Если в Subject Архитектора есть «ИДЁТ НАВСТРЕЧУ», «ПРОХОДЯТ МИМО», «ПЕРЕСЕКАЮТСЯ»:
                → ВСЕГДА используй: «walking in opposite directions towards each other, about to cross paths»
                → Явно указывай направление для каждой группы: «patrol walking towards viewer, officer walking towards them from opposite direction»
                → НИКОГДА не своди к «walking on a street» или «positioned» для сцен движения

                2. Для статичных сцен (СТОИТ, НАХОДИТСЯ, СИДИТ) — используй «standing», «positioned», «seated»

                3. Для сцен с общим транспортом (СИДЯТ НА ОДНОЙ ЛОШАДИ):
                → «two riders on a single dark horse, woman seated in front, man seated behind her, his arms wrapped around her waist»

                4. Проверяй себя: если в Subject есть «навстречу», «пересекаются», «проходят мимо» — в финальном промпте ОБЯЗАТЕЛЬНО должны быть «opposite directions», «towards each other», «crossing paths»

                ---

                ПРАВИЛА ТОТАЛЬНОЙ ДЕДУПЛИКАЦИИ

                1. Если токен или его близкий синоним уже использован в промпте — НЕ ПОВТОРЯЙ его в хвосте или другом разделе.
                2. Одна техника (painting ИЛИ photography), один тип света (chiaroscuro ИЛИ diffuse), один хвост (из Таблицы).
                3. Не дублируй информацию из Subject в Composition и наоборот.

                ---

                АЛГОРИТМ СБОРКИ ФИНАЛЬНОЙ СТРОКИ

                Склей все данные в ОДНУ англоязычную строку без переносов строк, используя запятые как разделители, в следующем СТРОГОМ порядке:

                1. [Core Subject]: Главные персонажи с их действиями, позами, одеждой, текстурой материалов и ПРОСТРАНСТВЕННЫМИ ОТНОШЕНИЯМИ. Каждый персонаж — отдельная группа токенов. Для встречного движения явно указывать направления: «patrol walking towards viewer, female officer walking towards them from opposite direction, about to cross paths».

                2. [Environment]: Окружение, задний план, погодные условия, время суток, состояние неба.

                3. [Composition & Camera]: Ракурс, положение камеры, фокусное расстояние, глубина резкости.
                - Если Архитектор указал «ПЕРЕДНИЙ ПЛАН: размытый» → пиши «blurred foreground out of focus, sharp focus on midground»
                - Если передний план в фокусе → пиши «sharp focus on foreground, blurred background»
                - НЕ ИСПОЛЬЗУЙ жёсткий шаблон «shallow depth of field, sharp focus on foreground» для всех сцен — он не универсален.

                4. [Style & Lighting]: ОДИН тип носителя (painting ИЛИ photo) и ОДИН тип освещения (из Таблицы или правила 6).

                5. [Genre & Mood Tail]: СТРОГО ОДИН атмосферный хвост из Таблицы стилевого приоритета, соответствующий жанру.

                ---

                ХОРРОР-ФИЛЬТР (только для хоррор/триллер/жутких сцен)

                Если жанр или атмосфера требует ужаса — добавляй в негативный промпт (если он используется downstream):
                «3d render, cartoon, cute, fluffy, friendly, clean, disney style, masterpiece, perfect anatomy, 8k, 4k, UHD»

                ---

                ФОРМАТ ОТВЕТА

                Ты должен вернуть ТОЛЬКО итоговую плоскую строку на АНГЛИЙСКОМ языке.
                Никаких вводных фраз, никаких кавычек, никаких объяснений и никаких markdown-разметок (без каких-либо ```). Только чистый, готовый к отправке в модель текст промпта.
                ";
            var userContent = $@"
                Обработай следующие слои и собери финальный промпт:
                - Жанр (Genre): {spec.Genre}
                - Объект (Subject): {spec.Subject}
                - Окружение (Environment): {spec.Environment}
                - Стиль и Свет (Style): {spec.Style}
                - Композиция (Composition): {spec.Composition}
                - Визуальная ссылка (VisualReference): {spec.VisualReference}
                - Негативные ограничения (Negative Constraints): {spec.NegativeConstraints}";

            // Если модель — локальная Ollama, используем её специфический эндпоинт и формат промпта
            if (analysisModel == "ollama-qwen-2-7b")
            {
                return await CallOllamaForPromptAsync(systemPrompt, userContent);
            }

            // Читаем из актуальной безопасной иерархии конфигурации
            string baseUrl = _configuration["AiServices:OpenRouter:BaseUrl"] ?? "[https://openrouter.ai/api/v1](https://openrouter.ai/api/v1)";
            string apiKey = _configuration["AiServices:OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter ApiKey не настроен!");
            string modelName = _configuration["AiServices:OpenRouter:PrimaryModel"] ?? "openai/gpt-oss-120b:free";

            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.1, 
                max_tokens = 5000 // Для плоского промпта 2000 токенов — с огромным запасом
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage httpResponse = await _httpClient.SendAsync(requestMessage);

                // Если первый запрос не удался - делаем fallback
                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errContent = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ArtArcPromptEngineerhitector Error] API вернул {httpResponse.StatusCode}: {errContent}");
                    Console.WriteLine("[PromptEngineer] Пытаемся использовать fallback модель openrouter/free...");
                    
                    // СОЗДАЕМ НОВЫЙ запрос для fallback
                    var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey.Trim()}");
                    
                    var fallbackBody = new
                    {
                        model = "openrouter/free",
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userContent }
                        },
                        temperature = 0.1,
                        max_tokens = 5000,
                    };
                    
                    string fallbackPayload = JsonSerializer.Serialize(fallbackBody);
                    fallbackRequest.Content = new StringContent(fallbackPayload, Encoding.UTF8, "application/json");
                    
                    // ПЕРЕИСПОЛЬЗУЕМ ту же переменную httpResponse
                    httpResponse = await _httpClient.SendAsync(fallbackRequest);
                    
                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        string fallbackErr = await httpResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[PromptEngineer Fallback Error] API вернул {httpResponse.StatusCode}: {fallbackErr}");
                        return userContent;
                    }
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
                return userContent ?? string.Empty;
            }
        }
        private async Task<string> CallOllamaForPromptAsync(string systemPrompt, string userContent)
        {
            string ollamaUrl = _configuration["AIServices:Ollama:BaseUrl"] ?? "http://localhost:11434";
            string modelName = _configuration["AIServices:Ollama:Model"] ?? "qwen2.5-coder:7b";
            
            // Ollama не разделяет system/user в стандартном API generate
            // Формируем полный промпт, явно разделяя системную инструкцию и пользовательский запрос
            string fullPrompt = $@"[СИСТЕМНАЯ ИНСТРУКЦИЯ]
                {systemPrompt}

                [ЗАПРОС ПОЛЬЗОВАТЕЛЯ]
                {userContent}

                [ПРАВИЛА ОТВЕТА]
                Ты должен вернуть ТОЛЬКО итоговую плоскую строку на АНГЛИЙСКОМ языке.
                Никаких вводных фраз, никаких кавычек, никаких объяснений и никаких markdown-разметок.
                Только чистый, готовый к отправке в модель текст промпта.";

            var requestBody = new
            {
                model = modelName,
                prompt = fullPrompt,
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    num_predict = 2000,
                    top_p = 0.9,
                    stop = new[] { "[СИСТЕМНАЯ", "[ЗАПРОС", "[ПРАВИЛА" } // Останавливаемся если модель начинает генерировать мета-текст
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ollamaUrl.TrimEnd('/')}/api/generate");
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Console.WriteLine($"[PromptEngineer Ollama] Отправка запроса к {ollamaUrl}/api/generate, модель: {modelName}");
            Console.WriteLine($"[PromptEngineer Ollama] Жанр: {userContent.Substring(0, Math.Min(100, userContent.Length))}...");

            try
            {
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[PromptEngineer Ollama Error] {response.StatusCode}: {error}");
                    return string.Empty;
                }

                string responseString = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[PromptEngineer Ollama] Получен ответ, длина: {responseString.Length} символов");
                
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("response", out var responseContent))
                {
                    string rawPrompt = responseContent.GetString() ?? "";
                    
                    // Очистка от возможных markdown и лишних кавычек
                    rawPrompt = rawPrompt.Replace("```", "").Replace("```json", "").Replace("```text", "").Trim();
                    
                    // Удаляем возможные обрамляющие кавычки
                    if (rawPrompt.StartsWith("\"") && rawPrompt.EndsWith("\""))
                        rawPrompt = rawPrompt.Substring(1, rawPrompt.Length - 2);
                    
                    // Если модель начала отвечать с пояснений — пытаемся извлечь промпт
                    // Ищем первую строку, которая выглядит как нормальный промпт (не начинается со слов "вот", "здесь")
                    var lines = rawPrompt.Split('\n');
                    string cleanPrompt = "";
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        
                        // Пропускаем служебные фразы на русском
                        if (trimmed.StartsWith("Вот") || trimmed.StartsWith("Здесь") || 
                            trimmed.StartsWith("Я") || trimmed.StartsWith("Обработан") ||
                            trimmed.StartsWith("Сгенери") || trimmed.StartsWith("Получ"))
                        {
                            continue;
                        }
                        
                        // Первая нормальная строка — берём всю оставшуюся часть
                        cleanPrompt = string.Join(" ", lines.Skip(Array.IndexOf(lines, line))).Trim();
                        break;
                    }
                    
                    if (!string.IsNullOrEmpty(cleanPrompt))
                        rawPrompt = cleanPrompt;
                    
                    Console.WriteLine($"[PromptEngineer Ollama Success] Финальный промпт (первые 200 символов): {rawPrompt.Substring(0, Math.Min(200, rawPrompt.Length))}...");
                    return rawPrompt;
                }
                
                Console.WriteLine($"[PromptEngineer Ollama Error] Поле 'response' не найдено в ответе");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PromptEngineer Ollama Крах] Исключение: {ex.Message}");
                return string.Empty;
            }
        }
    }
}