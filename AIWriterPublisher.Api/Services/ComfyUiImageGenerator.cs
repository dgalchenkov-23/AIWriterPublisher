using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AIWriterPublisher.Api.Models.DTO; 
using AIWriterPublisher.Api.Services; 
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIWriterPublisher.Api.Services;

public sealed class ComfyUiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ComfyUiImageGenerator> _logger;
    private const string ComfyUrl = "http://127.0.0.1:8188"; // Стандартный порт ComfyUI
    private readonly LoraOrchestrationService _loraOrchestrator;
    private readonly ZImageGraphOrchestrator _zImageOrchestrator;

    public ComfyUiImageGenerator(HttpClient httpClient, ILogger<ComfyUiImageGenerator> logger, LoraOrchestrationService loraOrchestrator, ZImageGraphOrchestrator zImageOrchestrator)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 минут на генерацию
        _logger = logger;
        _loraOrchestrator = loraOrchestrator;
        _zImageOrchestrator = zImageOrchestrator;
    }

    /// <summary>
    /// Результат генерации: base64 для фронта + имя файла для внутреннего использования
    /// </summary>
    public class GenerationResult
    {
        public string Base64 { get; set; }
        public string FileName { get; set; }
        public string SubFolder { get; set; }
    }


    public async Task<string> GenerateImageAsync(EngineeringSpecDto engineeringSpec, TechnicalSpecDto artArchitectorSpec, string analysisModel, string aspectRatio = "2:3")
    {
        Console.WriteLine($"[ComfyUI] Запущен процесс генерации с моделью: {analysisModel}");
        string width = "1024";
        string height = "1024";

        if (aspectRatio == "2:3")
        {
            width = "832";
            height = "1248";
        }
        else if (aspectRatio == "3:2")
        {
            width = "1248";
            height = "832";
        }
        else if (aspectRatio == "16:9")
        {
            width = "1344";
            height = "768";
        }

        return await GenerateEventHorizonAsync(engineeringSpec.PositivePrompt, engineeringSpec.NegativePrompt, artArchitectorSpec, analysisModel);
    }

    public class ComfyUiGraphOrchestrator
    {
        public static string InjectLoraChainIntoGraph(string jsonGraph, List<LoraConfig> lorasToInject, string manifestJsonContent, ZImageGraphOrchestrator orchestrator)
        {
            var graph = JObject.Parse(jsonGraph);
           
            
            // 1. Получили сырой ответ от Лораведа и десериализовали в List<LoraConfig> aiLoras
            var optimizationResult = orchestrator.OptimizeGraph(lorasToInject);

            // 2. Мапим настройки KSampler (Нода "6")
            var kSamplerInputs = graph["6"]["inputs"];
            kSamplerInputs["steps"] = optimizationResult.SamplerSettings.Steps;
            kSamplerInputs["cfg"] = optimizationResult.SamplerSettings.Cfg;
            kSamplerInputs["sampler_name"] = optimizationResult.SamplerSettings.SamplerName;
            kSamplerInputs["scheduler"] = optimizationResult.SamplerSettings.Scheduler;

            // 3. Инжекция цепочки в Power Lora Loader (Нода "8")
            if (graph["8"] != null && graph["8"]["inputs"] != null)
            {
                var loraLoaderInputs = (JObject)graph["8"]["inputs"];
                
                // Чистим старые слоты lora_
                var keysToRemove = loraLoaderInputs.Properties()
                    .Select(p => p.Name)
                    .Where(name => name.StartsWith("lora_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    loraLoaderInputs.Remove(key);
                }

                // Наполняем новыми
                if (optimizationResult.PatchedLoras != null && optimizationResult.PatchedLoras.Count > 0)
                {
                    for (int i = 0; i < optimizationResult.PatchedLoras.Count; i++)
                    {
                        var lora = optimizationResult.PatchedLoras[i];
                        int slotIndex = i + 1;

                        var loraObject = new JObject
                        {
                            ["on"] = true,
                            ["lora"] = lora.FileName,
                            ["strength"] = lora.StrengthModel
                        };

                        loraLoaderInputs[$"lora_{slotIndex}"] = loraObject;
                    }
                }
            }
            
            return graph.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    /// <summary>
    /// Загружает изображение на сервер ComfyUI (эндпоинт /upload/image)
    /// </summary>
    /// <param name="imageBase64">Строка изображения в формате Base64 (с префиксом data:image/... или без)</param>
    /// <returns>Имя файла, сохраненное в папке input внутри ComfyUI</returns>
    private async Task<string> UploadImageToComfyUiAsync(string imageBase64)
    {
        _logger.LogInformation("Подготовка к отправке референса в ComfyUI...");

        // Очищаем префикс Base64, если он прилетел с фронта
        if (imageBase64.Contains(","))
        {
            imageBase64 = imageBase64.Split(',')[1];
        }

        byte[] imageBytes = Convert.FromBase64String(imageBase64);
        string tempFileName = $"ref_{Guid.NewGuid():N}.png";

        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        
        // ComfyUI жестко требует поле "image" в multipart форме
        content.Add(imageContent, "image", tempFileName);
        // Флаг перезаписи, если файл с таким именем существует
        content.Add(new StringContent("true"), "overwrite");

        var response = await _httpClient.PostAsync($"{ComfyUrl}/upload/image", content);
        response.EnsureSuccessStatusCode();

        string jsonResult = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResult);
        
        if (doc.RootElement.TryGetProperty("name", out var nameProp))
        {
            string serverFileName = nameProp.GetString() ?? tempFileName;
            _logger.LogInformation("Референс успешно загружен в ComfyUI под именем: {Name}", serverFileName);
            return serverFileName;
        }

        throw new InvalidOperationException("ComfyUI успешно принял файл, но не вернул его внутреннее имя.");
    }

    /// <summary>
    /// Копирует файл из output в input для использования в качестве маски
    /// </summary>
    private async Task<string> CopyMaskToInputAsync(string maskFileName, string subFolder = null)
    {
        // Скачиваем файл из output
        string viewUrl = $"{ComfyUrl}/view?filename={Uri.EscapeDataString(maskFileName)}&type=output";
        if (!string.IsNullOrEmpty(subFolder))
        {
            viewUrl += $"&subfolder={Uri.EscapeDataString(subFolder)}";
        }
        
        byte[] maskBytes = await _httpClient.GetByteArrayAsync(viewUrl);
        
        // Загружаем в input
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(maskBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "image", maskFileName);
        content.Add(new StringContent("true"), "overwrite");
        
        var response = await _httpClient.PostAsync($"{ComfyUrl}/upload/image", content);
        response.EnsureSuccessStatusCode();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var uploadResult = JObject.Parse(jsonResponse);
        var uploadedName = uploadResult["name"]?.ToString();
        
        _logger.LogInformation("Маска скопирована из output в input: {FileName}", uploadedName);
        return uploadedName;
    }

    /// <summary>
    /// РЕЖИМ Z-IMAGE: Обработка изображения на основе референса и маски (Img2Img / Inpainting)
    /// </summary>
    public async Task<String> ProcessImageByReferenceAsync(ComfyUiImg2ImgSpecDto spec)
    {
        _logger.LogInformation("Запуск режима Z-Image для точечной коррекции...");

        try
        {
            // 1. Проверяем доступность ComfyUI
            await CheckComfyUiHealthAsync();

            // 2. Подготовка изображений
            string referenceFileName = await UploadImageToComfyUiAndGetNameAsync(spec.ReferenceImagePath);
            string maskFileName = string.Empty;

            // 3. Определяем, нужна ли авто-маска
            bool needsAutoMask = spec.EnableInpainting && 
                                string.IsNullOrEmpty(spec.MaskImagePath) && 
                                !string.IsNullOrEmpty(spec.MaskDescription);

            // 4. Если нужна авто-маска — сначала генерируем её через Граф A
            if (needsAutoMask)
            {
                _logger.LogInformation("Генерация маски через GroundingDINO: {Description}", spec.MaskDescription);
                var maskResult = await GenerateMaskAsync(spec.ReferenceImagePath, spec.MaskDescription);
                maskFileName = maskResult.FileName;  // ← берём имя файла для внутреннего использования
            }
            else if (spec.EnableInpainting && !string.IsNullOrEmpty(spec.MaskImagePath))
            {
                maskFileName = await UploadImageToComfyUiAndGetNameAsync(spec.MaskImagePath);
            }

            // 5. Загружаем шаблон workflow для Img2Img (Граф B)
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "EventHorizon_Img2Img_B.json");
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа Img2Img не найден: {workflowPath}");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath);
            var graph = JObject.Parse(workflowJson);

            // 6. ИНЖЕКЦИЯ ПАРАМЕТРОВ
            // Нода 60: LoadImage
            if (graph["60"]?["inputs"] is JObject loadInputs)
                loadInputs["image"] = referenceFileName;

            // Нода 75: LoadImageMask
            if (graph["75"]?["inputs"] is JObject maskInputs)
            {
                maskInputs["image"] = maskFileName;
                maskInputs["channel"] = "red";
            }

            // Нода 5: CLIPTextEncode (промпт)
            if (graph["5"]?["inputs"] is JObject posInputs)
                posInputs["text"] = spec.PromptOverride;

            // Нода 4: KSampler
            if (graph["4"]?["inputs"] is JObject samplerInputs)
            {
                samplerInputs["denoise"] = spec.DenoisingStrength;
                samplerInputs["steps"] = 10;
                samplerInputs["seed"] = new Random().Next();
            }
            // 7. Отправка запроса
            var requestBody = new { prompt = graph };
            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{ComfyUrl}/api/prompt", content);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(jsonResponse);
            var promptId = result["prompt_id"]?.ToString();

            // 8. Ждём результат и возвращаем base64 для фронта
            var generationResult = await PollAndGetResultAsync(promptId);
            return generationResult.Base64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка в пайплайне Z-Image");
            throw;
        }
    }
    /// <summary>
    /// Загружает изображение в ComfyUI и возвращает ТОЛЬКО имя файла
    /// </summary>
    private async Task<string> UploadImageToComfyUiAndGetNameAsync(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        byte[] fileBytes;
        
        if (File.Exists(input))
        {
            fileBytes = await File.ReadAllBytesAsync(input);
        }
        else if (input.StartsWith("data:image"))
        {
            var base64Part = input.Substring(input.IndexOf(',') + 1);
            fileBytes = Convert.FromBase64String(base64Part);
        }
        else
        {
            fileBytes = Convert.FromBase64String(input);
        }
        
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "image", "image.png");
        
        var response = await _httpClient.PostAsync($"{ComfyUrl}/upload/image", content);
        response.EnsureSuccessStatusCode();
        
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var uploadResult = JObject.Parse(jsonResponse);
        var fileName = uploadResult["name"]?.ToString();
        
        return fileName;
    }

            
    /// <summary>
    /// Генерация маски (возвращает результат с base64 для фронта и именем файла)
    /// </summary>
    private async Task<GenerationResult> GenerateMaskAsync(string referenceImagePath, string maskDescription)
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            string referenceFileName = await UploadImageToComfyUiAndGetNameAsync(referenceImagePath);
            
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "EventHorizon_Img2Img_A.json");
            string workflowJson = await File.ReadAllTextAsync(workflowPath);
            var graph = JObject.Parse(workflowJson);
            
            if (graph["60"]?["inputs"] is JObject loadInputs)
                loadInputs["image"] = referenceFileName;
            
            if (graph["79"]?["inputs"] is JObject segInputs)
                segInputs["prompt"] = maskDescription;
            
            var requestBody = new { prompt = graph };
            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{ComfyUrl}/api/prompt", content);
            response.EnsureSuccessStatusCode();
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(jsonResponse);
            var promptId = result["prompt_id"]?.ToString();
            
            // Получаем результат (маска в output)
            var maskResult = await PollAndGetResultAsync(promptId);
            
            // ✅ КОПИРУЕМ МАСКУ ИЗ OUTPUT В INPUT
            string copiedFileName = await CopyMaskToInputAsync(maskResult.FileName, maskResult.SubFolder);
            
            // Возвращаем результат с новым именем файла (который теперь в input)
            return new GenerationResult
            {
                Base64 = maskResult.Base64,
                FileName = copiedFileName,  // ← теперь это файл в папке input
                SubFolder = maskResult.SubFolder
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации маски");
            throw;
        }
    }

    /// <summary>
    /// Вспомогательный метод: проверяет, является ли путь строкой Base64 или путем к файлу, и загружает в ComfyUI
    /// </summary>
    private async Task<string> PrepareAndUploadImageAsync(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Если это путь к локальному файлу в папке /covers (согласно Манифесту 0.4)
        if (File.Exists(input))
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(input);
            string base64 = Convert.ToBase64String(fileBytes);
            return await UploadImageToComfyUiAsync(base64);
        }
        
        // Если это уже Base64 строка
        return await UploadImageToComfyUiAsync(input);
    }

    public async Task<string> GenerateEventHorizonAsync(string positivePrompt, string negativePrompt, TechnicalSpecDto artArchitectorSpec, string analysisModel)
    {
        try
        {
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "ZIT_Heretic.json");
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа ComfyUI не найден: {workflowPath}");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath);
            Console.WriteLine($"[ComfyUI] Шаблон графа Juggernaut XL загружен. Подготовка к генерации... готовит промпт модель: {analysisModel}");
            // 1. Запрашиваем у ИИ-оркестратора Лоры на основе входящего промпта
            // Передаем "All" или дефолтный жанр, так как ИИ-агент внутри сам разберется по контексту текста
            var selectedLoras = await _loraOrchestrator.PrepareLorasForGenerationAsync("All", artArchitectorSpec, analysisModel);

            /// 2. ДОПИСЫВАЕМ ТРИГГЕРНЫЕ СЛОВА В ПРОМПТ
            // Собираем все непустые триггеры от выбранных Лор
            var triggers = selectedLoras
                .Where(l => !string.IsNullOrEmpty(l.TriggerWords))
                .Select(l => l.TriggerWords);

            if (triggers.Any())
            {
                string combinedTriggers = string.Join(", ", triggers);
                // Мягко добавляем триггеры в начало промпта для максимального веса во Flux
                positivePrompt = $"{combinedTriggers}, {positivePrompt}";
                
                Console.WriteLine($"[ComfyUI] Промпт обогащен триггерами: {positivePrompt}");
            }

            /// 3. Мапим в LoraConfig для врезки файлов в ноды
            var lorasToInject = selectedLoras.Select(l => new LoraConfig
            {
                DisplayName = l.DisplayName,
                FileName = l.FileName,
                StrengthModel = l.StrengthModel,
                StrengthClip = l.StrengthClip
            }).ToList();

            // 4. ПОДСТРАХОВКА: Считываем оригинальный манифест с диска
            // Берём путь к lora_manifest.json (предполагаем, что он лежит рядом с билдом или в корне проекта)
            string manifestPath = Path.Combine(AppContext.BaseDirectory, "lora_manifest.json");
            string rawManifestJson = File.Exists(manifestPath) 
                ? await File.ReadAllTextAsync(manifestPath) 
                : string.Empty;

            if (string.IsNullOrEmpty(rawManifestJson))
            {
                Console.WriteLine("[ComfyUI] ПРЕДУПРЕЖДЕНИЕ: Файл lora_manifest.json не найден по пути: " + manifestPath + ". Автоподтягивание оверрайдов из кода работать не будет.");
            }

            // Передаем граф, подготовленный стек Лор и сырой манифест
            string loraGraph = ComfyUiGraphOrchestrator.InjectLoraChainIntoGraph(workflowJson, lorasToInject, rawManifestJson, _zImageOrchestrator);

            var graph = JsonNode.Parse(loraGraph)?.AsObject();
            //var graph = JsonNode.Parse(workflowJson)?.AsObject();

            if (graph == null) throw new InvalidOperationException("Не удалось распарсить JSON.");

            // Нода 5: CLIPTextEncode (Positive Prompt)
            string positiveNodeId = "5";
            Console.WriteLine($"[ComfyUI] Подставляем положительный промпт в ноду {positiveNodeId}: {positivePrompt}");
            if (graph.TryGetPropertyValue(positiveNodeId, out var posNode) && posNode?["inputs"] is JsonObject posInputs)
            {
                // У CLIPTextEncode поле называется "text", а не "positive"
                posInputs["text"] = positivePrompt;
            }

            // Нода 4: CLIPTextEncode (Negative Prompt)
            string negativeNodeId = "4";
            Console.WriteLine($"[ComfyUI] Подставляем отрицательный промпт в ноду {negativeNodeId}: {negativePrompt}");
            if (graph.TryGetPropertyValue(negativeNodeId, out var negNode) && negNode?["inputs"] is JsonObject negInputs)
            {
                // У CLIPTextEncode поле называется "text", а не "negative"
                negInputs["text"] = negativePrompt;
            }
            Console.WriteLine($"Полный граф: \r\n {graph.ToString()}");
            await CheckComfyUiHealthAsync();

            var requestBody = new JsonObject { ["prompt"] = graph };
            var response = await _httpClient.PostAsJsonAsync($"{ComfyUrl}/prompt", requestBody);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var queueResult = System.Text.Json.JsonSerializer.Deserialize<ComfyQueueResponse>(jsonResponse, options);

            Console.WriteLine($"[ComfyUI] получили результат очереди: {queueResult.PromptId}");
            
            var generationResult = await PollAndGetResultAsync(queueResult!.PromptId);

            return generationResult.Base64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при работе с локальным инференсом ComfyUI");
            throw;
        }
    }

    private async Task CheckComfyUiHealthAsync()
    {
        try
        {
            var pingResponse = await _httpClient.GetAsync($"{ComfyUrl}/");
            if (!pingResponse.IsSuccessStatusCode)
            {
                throw new Exception($"ComfyUI недоступен. Код: {pingResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUI не отвечает по адресу {Url}", ComfyUrl);
            throw new Exception("ComfyUI не отвечает. Запусти run_nvidia_gpu.bat", ex);
        }
    }



    public async Task<string> ExecuteCharacterInferenceAsync(
        string finalPrompt, List<LoraPreset> loras, string? faceReferenceUrl, 
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // 1. Динамически выбираем шаблон графа в зависимости от наличия FaceSwap
            bool isFaceSwapRequired = !string.IsNullOrEmpty(faceReferenceUrl);
            string workflowFileName = isFaceSwapRequired ? "ZIT_Hero_FaceSwap.json" : "ZIT_Heretic_HeroFace.json";
            string workflowPath = Path.Combine(AppContext.BaseDirectory, workflowFileName);

            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа ComfyUI не найден: {workflowPath}");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath, ct);
            _logger.LogInformation("[ComfyUI] Загружен граф {Workflow}. FaceSwap: {IsFaceSwap}", workflowFileName, isFaceSwapRequired);

            // 2. Мапим во внутреннюю структуру для инжектора Лора-нод
            var lorasToInject = loras.Select(l => new LoraConfig
            {
                DisplayName = l.DisplayName,
                FileName = l.FileName,
                StrengthModel = l.DefaultWeight, // Используем вес, рассчитанный Ларой
                StrengthClip = l.DefaultWeight
            }).ToList();

            // Подгружаем манифест для проверки оверрайдов
            string manifestPath = Path.Combine(AppContext.BaseDirectory, "lora_portrait_manifest.json");
            string rawManifestJson = File.Exists(manifestPath) 
                ? await File.ReadAllTextAsync(manifestPath, ct) 
                : string.Empty;
            string loraGraph = string.Empty;

            if( !isFaceSwapRequired )
            {
                _logger.LogInformation("[ComfyUI] FaceSwap не требуется. Используем стандартный граф без LoRA.");
                loraGraph = workflowJson; // Если FaceSwap не нужен, используем граф без LoRA
            }
            else
            {
                _logger.LogInformation("[ComfyUI] FaceSwap требуется. Инжектим цепочку LoRA в граф.");
                loraGraph = ComfyUiGraphOrchestrator.InjectLoraChainIntoGraph(workflowJson, lorasToInject, rawManifestJson, _zImageOrchestrator);
            }
            // Врезаем цепочку Лорок в граф

            var graph = JsonNode.Parse(loraGraph)?.AsObject();
            if (graph == null) throw new InvalidOperationException("Не удалось распарсить итоговый JSON графа.");

            // 3. Генерируем случайный seed для KSampler (Нода "6") для разнообразия генераций
            long newSeed = Random.Shared.NextInt64(1, 999999999999999);
            graph["6"]["inputs"]["seed"] = newSeed;
            // 4. Подставляем промпты в стандартные ноды
            // Нода 5: Positive Prompt
            string positiveNodeId = "5";
            if (graph.TryGetPropertyValue(positiveNodeId, out var posNode) && posNode?["inputs"] is JsonObject posInputs)
            {
                posInputs["text"] = finalPrompt;
            }

            // Нода 5: Negative Prompt (для Flux можно оставлять пустым или зашивать дефолт)
            string negativeNodeId = "4";
            if (graph.TryGetPropertyValue(negativeNodeId, out var negNode) && negNode?["inputs"] is JsonObject negInputs)
            {
                negInputs["text"] = "bad anatomy, blurry, low quality, deformed, extra limbs";
            }

            // 6. Если нужен FaceSwap — настраиваем ноду ReActor / IP-Adapter
            if (isFaceSwapRequired)
            {
                // Предположим, нода FaceSwap в твоем графе ZIT_Hero_FaceSwap.json имеет ID "10"
                string faceSwapNodeId = "10"; 
                _logger.LogInformation("[ComfyUI] Инжектим ссылку на лицо в ноду {NodeId}: {Url}", faceSwapNodeId, faceReferenceUrl);
                
                if (graph.TryGetPropertyValue(faceSwapNodeId, out var fsNode) && fsNode?["inputs"] is JsonObject fsInputs)
                {
                    // Поле может называться "image" или "image_path" в зависимости от кастомной ноды загрузки картинок по URL
                    fsInputs["image_url"] = faceReferenceUrl; 
                }
                else
                {
                    _logger.LogWarning("[ComfyUI] Предупреждение: Нода FaceSwap с ID {NodeId} не найдена в графе!", faceSwapNodeId);
                }
            }

            ct.ThrowIfCancellationRequested();

            // 7. Проверяем здоровье ComfyUI и отправляем запрос
            await CheckComfyUiHealthAsync();

            Console.WriteLine($"[ComfyUI] Полный граф для инференса персонажа: {graph.ToString()}");

            var requestBody = new JsonObject { ["prompt"] = graph };
            
            // Используем встроенный токен отмены для HttpClient запроса
            var response = await _httpClient.PostAsJsonAsync($"{ComfyUrl}/prompt", requestBody, ct);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync(ct);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var queueResult = System.Text.Json.JsonSerializer.Deserialize<ComfyQueueResponse>(jsonResponse, options);

            if (queueResult == null || string.IsNullOrEmpty(queueResult.PromptId))
            {
                throw new InvalidOperationException("ComfyUI не вернул PromptId очереди.");
            }

            _logger.LogInformation("[ComfyUI] Запрос успешно поставлен в очередь. PromptId: {PromptId}", queueResult.PromptId);
            
            // 8. Уходим в поллинг ожидания генерации файла (передаем токен отмены внутрь)
            var generationResult = await PollAndGetResultAsync(queueResult.PromptId, ct);

            return generationResult.Base64;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[ComfyUI] Генерация была отменена пользователем.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка инференса персонажа в ExecuteCharacterInferenceAsync");
            throw;
        }
    }


    

    // Дополнительный метод для скачивания изображения в байты (если нужно сохранить локально)
    private async Task<byte[]> DownloadImageAsync(string fileName, string subFolder = null)
    {
        string viewUrl = $"{ComfyUrl}/view?filename={Uri.EscapeDataString(fileName)}&type=output";
        if (!string.IsNullOrEmpty(subFolder))
        {
            viewUrl += $"&subfolder={Uri.EscapeDataString(subFolder)}";
        }
        
        return await _httpClient.GetByteArrayAsync(viewUrl);
    }

    /// <summary>
    /// Ожидает завершения генерации и возвращает результат (base64 для фронта + имя файла)
    /// </summary>
    private async Task<GenerationResult> PollAndGetResultAsync(string promptId, CancellationToken cancellationToken = default)
    {
        string? fileName = null;
        string? subFolder = null;
        bool isCompleted = false;
        
        // 1. Пробуем WebSocket для мгновенного уведомления
        try
        {
            using var ws = new ClientWebSocket();
            var wsUrl = $"{ComfyUrl.Replace("http", "ws")}/ws?clientId={Guid.NewGuid():N}";
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
            
            // Ждём уведомления о завершении (максимум 30 секунд)
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var buffer = new byte[4096];
            
            while (!isCompleted && !cts.Token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                
                // Ищем событие завершения для нашего prompt_id
                if (message.Contains($"\"prompt_id\": \"{promptId}\"") && 
                    (message.Contains("\"type\": \"executing\"") || 
                    message.Contains("\"type\": \"executed\"")))
                {
                    _logger.LogInformation("[ComfyUI] WebSocket получил уведомление о завершении генерации");
                    isCompleted = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ComfyUI] WebSocket не удалось использовать, переключаемся на polling");
            // WebSocket не сработал — продолжаем с polling
        }
        
        // 2. Если WebSocket не сработал или не дождался — используем polling (как раньше)
        if (!isCompleted)
        {
            _logger.LogInformation("[ComfyUI] Начинаем polling для проверки завершения генерации");
            
            int maxAttempts = 120;
            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(5000);
                
                var historyResponse = await _httpClient.GetAsync($"{ComfyUrl}/history/{promptId}");
                if (!historyResponse.IsSuccessStatusCode) continue;
                
                string historyJson = await historyResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(historyJson);
                
                if (doc.RootElement.TryGetProperty(promptId, out var taskDetails))
                {
                    if (taskDetails.TryGetProperty("outputs", out var outputs))
                    {
                        foreach (var nodeOutput in outputs.EnumerateObject())
                        {
                            if (nodeOutput.Value.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                            {
                                var firstImage = images[0];
                                fileName = firstImage.GetProperty("filename").GetString();
                                if (firstImage.TryGetProperty("subfolder", out var subProp))
                                    subFolder = subProp.GetString();
                                isCompleted = true;
                                break;
                            }
                        }
                    }
                    break;
                }
            }
        }
        else
        {
            // Если WebSocket сработал — нужно скачать результат
            // Получаем список файлов из output
            string historyJson = await _httpClient.GetStringAsync($"{ComfyUrl}/history/{promptId}");
            using var doc = JsonDocument.Parse(historyJson);
            
            if (doc.RootElement.TryGetProperty(promptId, out var taskDetails))
            {
                if (taskDetails.TryGetProperty("outputs", out var outputs))
                {
                    foreach (var nodeOutput in outputs.EnumerateObject())
                    {
                        if (nodeOutput.Value.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                        {
                            var firstImage = images[0];
                            fileName = firstImage.GetProperty("filename").GetString();
                            if (firstImage.TryGetProperty("subfolder", out var subProp))
                                subFolder = subProp.GetString();
                            break;
                        }
                    }
                }
            }
        }
        
        // 3. Если результат не получен — таймаут
        if (!isCompleted || string.IsNullOrEmpty(fileName))
            throw new TimeoutException("Превышено время ожидания генерации в ComfyUI.");
        
        // 4. Скачиваем изображение
        string viewUrl = $"{ComfyUrl}/view?filename={Uri.EscapeDataString(fileName)}&type=output";
        if (!string.IsNullOrEmpty(subFolder))
            viewUrl += $"&subfolder={Uri.EscapeDataString(subFolder)}";
        
        byte[] imageBytes = await _httpClient.GetByteArrayAsync(viewUrl);
        string base64 = $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
        
        // Сохраняем локально
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "covers");
        if (!Directory.Exists(outputDirectory)) 
            Directory.CreateDirectory(outputDirectory);
        
        string localPath = Path.Combine(outputDirectory, $"{promptId}.png");
        await File.WriteAllBytesAsync(localPath, imageBytes);
        
        return new GenerationResult
        {
            Base64 = base64,
            FileName = fileName,
            SubFolder = subFolder
        };
    }
}

