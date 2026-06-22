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
                Твоя задача — проанализировать послойный запрос пользователя, сопоставить его со списком доступных моделей и вернуть идеальный стек LoRA в формате JSON.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 1 — СТРОГИЕ ФИЛЬТРЫ И ИСКЛЮЧЕНИЯ (ПРОЧТИ ОБЯЗАТЕЛЬНО)
                ═══════════════════════════════════════════════════════════════════
                1. ЭКСПЕРИМЕНТАЛЬНЫЕ МОДЕЛИ — НИКОГДА НЕ ВЫБИРАЙ АВТОМАТИЧЕСКИ (они только для ручного выбора):
                ❌ Desimulate Style (Desimulate_LoRA_Z_Image_Turbo.safetensors)
                ❌ Better Sketches (Sketchy style.safetensors)
                ❌ Frazetta Ink Style (z_frazetta_ink_000006750.safetensors)
                ❌ Space Worlds: Planets 03 (Unha) (SW_planets3_zit.safetensors)
                ❌ City Worlds 01 (CW01s_zit.safetensors)

                2. ВЗАИМОИСКЛЮЧАЮЩИЕ МОДЕЛИ (КОНФЛИКТЫ):
                • Из линейки лиминальности выбери строго ОДИН: BigLiminal D8 (легкий), D16 (средний) или D32 (максимальный).
                • Из кибер-бездны: Digital Abyss ИЛИ Cyborg Abyss (для киборгов).
                • Из реализма: Тройка [Realistic Snapshot, Jib Mix Realistic, CCD Realistic] и [MIDJOURNEY V1] конфликтуют между собой. Выбирай что-то одно! CCD и Snapshot никогда не мешать вместе.
                • Слайдер Detail Slider (detail_slider_Z_V2.safetensors) конфликтует с базовыми улучшайзерами (Better Images, Aesthetic).

                3. ЖЕСТКИЙ ЛИМИТ НА СЛАЙДЕРЫ ДЕТАЛИЗАЦИИ (ЗАЩИТА ОТ УРОДСТВА АНАТОМИИ):
                • Слайдеры ""better_images_loraholic.safetensors"" и ""skindetails_mild_loraholic.safetensors"" обладают огромным кумулятивным эффектом.
                • ЕСЛИ ты берешь только ОДИН из них — вес может быть базовым (~4.0).
                • ЕСЛИ ты берешь ОБА слайдера в один стек — ты ОБЯЗАН снизить их веса: максимум 1.5 - 2.0 для каждого! Превышение этого лимита при совместном использовании ломает лица персонажей, сэмплирование шума и генерирует анатомических уродов.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 2 — ПОДБОР СТЭКА (Максимум 3-4 LoRA в один проход)
                ═══════════════════════════════════════════════════════════════════

                СЛОТ 1: ОБЯЗАТЕЛЬНЫЙ ТЕХНИЧЕСКИЙ СЛОЙ (Основа графа)
                • Если в стэке есть ХОТЯ БЫ ОДНА любая другая LoRA — ОБЯЗАТЕЛЬНО добавь технический патч:
                -> Distill Patch (Technical Layer) | FileName: z-image_turbo_distillpatch_comfyui.safetensors | Вес: 1.0 намертво.

                СЛОТ 2: БАЗОВЫЙ УЛУЧШАЙЗЕР (Выбирай 1, максимум 2 неконфликтующих):
                • Для общего улучшения, иллюстративности и «лечения» мусора: Z-Image-Aestheticbase-and-turbo.safetensors с весом 1.0.
                • Усилитель коротких/длинных промптов: Better Images / Prompt Slider (better_images_loraholic.safetensors). Вес от 2.0 до 9.0 (базово 4.0).
                • Для фотореализма / кожи: Realistic Snapshot (вес ~0.65) ИЛИ CCD Realistic (вес ~0.45).
                • Для ухода от стоковости к кинематографичному арту: Subject Plus+ (subjectplus_vibes_v1.safetensors, вес 0.7).
                • Для сочных, нешаблонных цветов: MIDJOURNEY V1 (midj-z-1.safetensors, вес 0.8).

                СЛОТ 3: ЖАНРОВЫЕ И СТИЛИСТИЧЕСКИЕ МОДЕЛИ (Строго под запрос):
                • Киберпанк / Сайнс-фикшн:
                - Нужен неон и преломление света -> Prismatic (вес 0.7-1.2, триггер: pr1sm4t1c)
                - Свечение, neoновые линии на черном -> Neon Line (z_image_turbo-LoRA-Neon_Line_3000.safetensors, вес 0.7)
                - Глюки, цифровой распад -> Digital Abyss или Cyborg Abyss (триггеры: bo-abyss, abstract digital elements)
                - Азиатская кибер-архитектура -> City Worlds: Cyberpunk Temples (CW_Cyb_Temples1_zit.safetensors, триггер: CBT1)
                - Боевые роботы, мехи -> New Mecha Style (new_mecha_zit_V2.safetensors, триггер: NewMecha style illustration.)
                • Темное фэнтези / Готика / Хоррор:
                - Мрачный, эротичный арт обложек -> Stefan Gesell Style (ZIT - Stefan_Gesell_style.safetensors, вес 0.85, триггер: Strhdfisssf)
                - Глубокая атмосфера, туман, cinematic свет -> Dark Gothic Style (Z Image Turbo - Dark Gothic Style.safetensors, вес 0.7, триггер: darkGothic)
                - Готические интерьеры эльфов -> Medieval Worlds: Dark Elves (MW_DE_part3_zit.safetensors, триггеры: RO5-RO9)
                • Классическое Фэнтези / Приключения:
                - Средневековые пейзажи, замки -> Medieval Worlds: Castles (Battle) ИЛИ Castles (MW_Castles1_zit.safetensors), триггер: mw01out
                • Космос / Космоопера:
                - Пустоши, планеты, луны -> Space Worlds: Planets 01 (или V2 для станций/кораблей). Триггер: z1os, absurdres
                • Комиксы / Аниме:
                - Стиль Клея Манна (DC) -> Clay Mann Artstyle (ClayMann_v2_by_Part.safetensors, вес 0.7, промпт начать с 'A detailed comic book illustration by Clay Mann...')
                - Четкая графика -> Eldritch Comics (Eldritch_Comics_Z_Image.safetensors)
                - Аниме-стилистика -> Aimaginedworlds (Anime) или AnimeMix (LoKr) (тригггер: @nim3mix)
                • Детская литература / Сказки:
                - 3D стиль Pixar/Disney -> Disney Style (Disney_IZT_ATK_V1_000000750.safetensors, триггер: DisneyIZT)

                СЛОТ 4: АТМОСФЕРА, СВЕТ И СЛАЙДЕРЫ ТУНИНГА:
                • Сложное драматичное освещение, глубина теней -> MixLight (вес 0.7).
                • Кинематографичный контраст -> Movie Clips (Enhance Contrast) (вес 0.7).
                • Цветокоррекция Голливуда (Бирюза/Оранжевый) -> Teal & Orange Color Slider (teal-orange_loraholic.safetensors, вес 5.0-8.0).
                • Винтажная оптика, крученое боке -> Helios 44-2 (h442x.safetensors, вес 0.6, триггер: h442x).
                • Туман и дым -> Fog & Smoke Slider (smoke_loraholic.safetensors, вес 3.0-10.0).
                • Капли дождя, мокрые поверхности -> Water Droplets (Wet Effect) (вес 0.7).
                • Тонкий тюнинг персонажей (Слайдеры):
                - Длина волос -> Hair Length Slider (hair_length_loraholic.safetensors, диапазон от -5 до 5)
                - Ракурс лица/тела -> Side View Slider (side_view_loraholic.safetensors, -4 для фронтального, 6-10 для профиля)
                - Детализация кожи -> Skin Detail Slider (skindetails_mild_loraholic.safetensors, базово 4.0)
                - Глаза крупным планом -> Eye Detail (EyeDetail_Z_Turbo.safetensors, триггер: eye_focus)

                • РАЗДЕЛЕНИЕ НА РЕАЛИЗМ И ТВОРЧЕСТВО (КРИТИЧЕСКИЙ ПРИОРИТЕТ):
                - ЕСЛИ в промпте явно указано ""реализм"", ""фото"", ""realistic snapshot"" или ""photo"" -> твоя главная цель — чистая, качественная текстура кожи и доспехов (CCD Realistic, Snapshot). Никакой лишней магии и стилизаций.
                - ЕСЛИ этих слов в явном виде НЕТ -> КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО скатываться в сухой фотореализм! Включай художественную смелость. Книжная обложка должна цеплять. Ищи в тексте зацепки для визуальных эффектов:
                    * Есть магия, мистика или темная энергия? Добавь ""Eye Detail"" для светящихся глаз (триггер: eye_focus) или ""Prismatic"" для преломления света.
                    * Атмосфера мрачная или величественная? Смело бери ""Dark Gothic Style"" или ""Stefan Gesell Style"" на средних весах (0.5-0.7).
                    * Есть намек на дым, заклинания или факелы? Прокидывай ""Fog & Smoke Slider"" (вес 3.0 - 5.0) для объема.

                • КНИЖНАЯ ИЛЛЮСТРАЦИЯ > СТАНДАРТНАЯ КИНОШНОСТЬ. Отдавай приоритет ярким, характерным и атмосферным LoRA. Не бойся экспериментировать и мешать жанровый стиль с погодными или световыми слайдерами (например, Dark Elves + MixLight + Слайдер тумана).

                • ЧИТАЙ МЕЖДУ СТРОК. Если автор пишет ""эпическая атмосфера"", ""магический эмбиент"", ""сияние меча"" — это твой прямой зеленый свет на добавление спецэффектов, необычного света и акцентных слайдеров, а не просто копирование персонажей.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 3 — КРИТИЧЕСКИЕ ПРАВИЛА И ПРИНЦИПЫ ИМЕНОВАНИЯ И ВЕСОВ
                ═══════════════════════════════════════════════════════════════════
                1. НЕ МЕНЯЙ ИМЯ И НАЗВАНИЕ LORA! Если в манифесте указано `FileName: ""z-image_turbo_distillpatch_comfyui.safetensors""`, то in выходном JSON должно быть строго `""FileName"": ""z-image_turbo_distillpatch_comfyui.safetensors""`. ИИ обязан передавать точные имена файлов без сокращений, перестановок и переименований (например, никаких ""Distill Patch.safetensors""). Это критично для дальнейшей загрузки модели на бэкенде.

                2. ПРАВИЛА ФОРМИРОВАНИЯ StrengthModel:
                • Если промпт НЕ требует усиления → используй значение из `RecommendedWeight` предоставленного манифеста (обычно в пределах 0.6-1.0).
                • Если промпт явно просит ""очень ярко"", ""экстремально"", ""сильно выраженный стиль"" → пропорционально увеличивай вес до максимального из манифеста (`MaxWeight`), но не выше 1.2.
                • Никогда не ставь вес ниже 0.5 (для стандартных моделей) и выше 1.2 (кроме спец-слайдеров вроде Better Images или Fog, работающих в диапазонах до 10.0).

                3. ПРАВИЛО ДЛЯ НОЧНЫХ И ТЁМНЫХ СЦЕН (ЗАЩИТА ОТ ПЕРЕСВЕТОВ И ВСПЫШКИ):
                Если сцена ночная, происходит в полумраке, при свечах, факелах, луне или во время бури (dim lanterns, weak light, moonlight, gloomy):
                • КАТЕГОРИЧЕСКИ ЗАПРЕЩЕНО полностью исключать базовый слой ""z-image-aestheticbase-and-turbo.safetensors"" только из-за того, что сцена темная. Без него Flux выдает дешевый ""эффект фотовспышки в лицо"" и ломает художественность!
                • Вместо удаления — снижай вес Эстетики до безопасных 0.4 - 0.5. Это удержит баланс цвета и не пересветит полумрак.
                • Если в темной сцене для кожи выбран CCD Realistic или Realistic Snapshot — снижай вес базового улучшайзера Aesthetic до 0.3-0.4, но не выкидывай его совсем, чтобы не превратить персонажей в безликий, бледный ""косплей"".
                • Ограничивай веса атмосферных LoRA освещения (например, MixLight) до 0.3-0.4, чтобы не убить тусклость и таинственность сцены.

                4. ВАЖНЫЕ ПРИНЦИПЫ:
                • КНИЖНАЯ ИЛЛЮСТРАЦИЯ > СТАНДАРТНАЯ КИНОШНОСТЬ. Отдавай приоритет художественным и характерным LoRA (Clay Mann Artstyle, Stefan Gesell Style, Dark Gothic), а не безликим коммерческим фильтрам.
                • ЖАНР — КОРОЛЬ. Если промпт четко указывает на жанр (например, киберпанк или средневековое фэнтези) — в первую очередь подбирай профильную LoRA из этой категории.
                • МИКРО-АКЦЕНТЫ ВСЕГДА В ПРИОРИТЕТЕ. Увидел ""side view"" или ""long hair"" → обязательно бери соответствующий Slider, даже если это единственная LoRA в стеке, помимо технической основы.
                • НЕ БОЙСЯ ОСТАВИТЬ ПУСТОЙ МАССИВ. Если промпт слишком общий или ни одна специализированная LoRA из списка не подходит → верни пустой массив `[]` в поле `""SelectedLoras""`.
                • НЕ ПОВТОРЯЙСЯ. Если ты уже выбрал одну базовую модель или жанровую основу, не добавляй дублирующую модель из той же категории.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 4 — ПРАВИЛА ЗОНИРОВАНИЯ СТИЛЕЙ
                ═══════════════════════════════════════════════════════════════════
                1. LoRA жесткой стилизации (например, Stefan Gesell Style, BigLiminal) НЕ применять в полную силу на:
                - лица и руки персонажей (важна читаемость черт лица и анатомии).
                - ключевые детали снаряжения (оружие, гербы, магические руны, надписи).
                - объекты, требующие чёткой геометрии (механизмы, роботы, архитектурные элементы).
                2. LoRA стилизации ПРИМЕНЯТЬ на:
                - фон и окружение (пейзажи, космические пустоши, облака, замки).
                - атмосферные эффекты (туман, дым, искры, световые лучи).
                - одежду и драпировки (ткани отлично принимают текстурные стили).
                3. Если запрос требует и глубокую стилизацию, и детализацию лиц — обязательно снижай силу стилизации (`StrengthModel`) до безопасных 0.4-0.6 и параллельно добавляй слайдеры, компенсирующие детализацию (`skindetails_mild_loraholic.safetensors` или `EyeDetail_Z_Turbo.safetensors`).
                4. LoRA освещения (MixLight, Movie Clips) можно накладывать на всю сцену, но контролируй лица на предмет пережогов.

                ═══════════════════════════════════════════════════════════════════
                ШАГ 5 — СТРОГИЙ ФОРМАТ ВЫВОДА (JSON)
                ═══════════════════════════════════════════════════════════════════
                Возвращай ТОЛЬКО чистый JSON. Никакого вводного, пояснительного или финального текста вне JSON.
                Обрати внимание на пример: если оверрайды есть в манифесте, они ДОЛЖНЫ быть заполнены в выходном JSON!

                {{
                ""SelectedLoras"": [
                    {{
                    ""DisplayName"": ""Z-Image-Aesthetic"",
                    ""FileName"": ""z-image-aestheticbase-and-turbo.safetensors"",
                    ""StrengthModel"": 1.0,
                    ""StrengthClip"": 1.0,
                    ""TriggerWords"": """"
                    }},
                    {{
                    ""DisplayName"": ""CCD Realistic (Creating-Realistic)"",
                    ""FileName"": ""ZIMAGE-CCD-V1.safetensors"",
                    ""StrengthModel"": 0.45,
                    ""StrengthClip"": 0.45,
                    ""TriggerWords"": ""ccdstyle""                    
                    }},
                    {{
                    ""DisplayName"": ""Better Images / Prompt Slider"",
                    ""FileName"": ""better_images_loraholic.safetensors"",
                    ""StrengthModel"": 1.5,
                    ""StrengthClip"": 1.5,
                    ""TriggerWords"": """"
                    }}
                ],
                ""Reasoning"": ""Краткое обоснование выбора моделей на основе жанра, свет-стиля и ограничений на совместный вес слайдеров и эстетики.""
                }}

                ═══════════════════════════════════════════════════════════════════
                СПИСОК ДОСТУПНЫХ LORA (используй эти данные)
                ═══════════════════════════════════════════════════════════════════
                {availableLorasJson}
                ";
        }

        private async Task<string> CallLlmApiAsync(string system, string user)
        {
            string baseUrl = _configuration["AIServices:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
            string apiKey = _configuration["AIServices:Gemini:ApiKey"] ?? "";
            string modelName = _configuration["AIServices:Gemini:Model"] ?? "gemini-3.1-flash-lite";
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