using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Newtonsoft.Json.Linq;

namespace AIWriterPublisher.Api.Services;

public sealed class ComfyUiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ComfyUiImageGenerator> _logger;
    private const string ComfyUrl = "http://127.0.0.1:8188"; // Стандартный порт ComfyUI
    private readonly LoraOrchestrationService _loraOrchestrator;

    public ComfyUiImageGenerator(HttpClient httpClient, ILogger<ComfyUiImageGenerator> logger, LoraOrchestrationService loraOrchestrator)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 минут на генерацию
        _logger = logger;
        _loraOrchestrator = loraOrchestrator;
    }

    public async Task<string> GenerateImageAsync(string technicalPrompt, TechnicalSpecDto artArchitectorSpec, string aspectRatio = "2:3")
    {
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

        return await GenerateEventHorizonAsync(technicalPrompt, "", artArchitectorSpec);
    }

    public class LoraConfig
    {
        public string LoraName { get; set; }
        public double StrengthModel { get; set; } = 1.0;
        public double StrengthClip { get; set; } = 1.0;
    }

    public class ComfyUiGraphOrchestrator
    {
        public static string InjectLoraChainIntoGraph(string jsonGraph, List<LoraConfig> lorasToInject)
        {
            var graph = JObject.Parse(jsonGraph);
            // 1. Динамически определяем базовые источники, читая их из ноды "12", пока она жива
            JArray baseModelInput;
            JArray baseClipInput;

            if (graph["12"] != null && graph["12"]["inputs"] != null)
            {
                // Берём оригинальные линки, которые были прописаны в шаблоне
                baseModelInput = (JArray)graph["12"]["inputs"]["model"];
                baseClipInput = (JArray)graph["12"]["inputs"]["clip"];
            }
            else
            {
                // Резервный фолбэк на случай, если ноду 12 уже кто-то стер, но мы знаем дефолты
                baseModelInput = new JArray { "4", 0 };
                baseClipInput = new JArray { "10", 0 };
            }

            // 2. Определяем, куда шел выход из последней дефолтной LoRA (ноды "13")
            // Нам нужно найти ноду, которая потребляла ["13", 0] и ["13", 1]
            string targetNodeIdForModel = null;
            string targetModelKey = null;
            string targetNodeIdForClip = null;
            string targetClipKey = null;

            foreach (var property in graph.Properties())
            {
                var node = property.Value as JObject;
                if (node == null || node["inputs"] == null) continue;

                foreach (var inputProp in ((JObject)node["inputs"]).Properties())
                {
                    if (inputProp.Value.Type == JTokenType.Array)
                    {
                        var link = (JArray)inputProp.Value;
                        if (link.Count == 2)
                        {
                            if (link[0].ToString() == "13" && link[1].Value<int>() == 0)
                            {
                                targetNodeIdForModel = property.Name;
                                targetModelKey = inputProp.Name;
                            }
                            if (link[0].ToString() == "13" && link[1].Value<int>() == 1)
                            {
                                targetNodeIdForClip = property.Name;
                                targetClipKey = inputProp.Name;
                            }
                        }
                    }
                }
            }

            // Если не нашли куда шла 13-я нода, значит граф изменен, бросаем исключение
            if (targetNodeIdForModel == null || targetNodeIdForClip == null)
            {
                throw new Exception("Не удалось найти конечные узлы-потребители для дефолтной LoRA цепочки (нода 13).");
            }

            // 3. Удаляем старые жестко зашитые ноды 12 и 13, чтобы они не мусорили
            graph.Remove("12");
            graph.Remove("13");

            // Если пользователь не выбрал ни одной LoRA, соединяем напрямую базы с таргетами
            if (lorasToInject == null || lorasToInject.Count == 0)
            {
                graph[targetNodeIdForModel]["inputs"][targetModelKey] = baseModelInput;
                graph[targetNodeIdForClip]["inputs"][targetClipKey] = baseClipInput;
                return graph.ToString();
            }

            // 4. Вычисляем стартовый ID для новых нод
            int currentMaxId = graph.Properties()
                .Select(p => int.TryParse(p.Name, out int id) ? id : 0)
                .Max();

            int nextNodeId = currentMaxId + 1;

            // Переменные для отслеживания текущего "хвоста" линковки
            JArray currentModelLink = baseModelInput;
            JArray currentClipLink = baseClipInput;

            // 5. Динамически строим цепочку LoRA
            for (int i = 0; i < lorasToInject.Count; i++)
            {
                var lora = lorasToInject[i];
                string newLoraNodeId = nextNodeId.ToString();
                nextNodeId++;

                // Создаем JSON-структуру для ноды LoraLoader
                var loraNode = new JObject
                {
                    ["inputs"] = new JObject
                    {
                        ["lora_name"] = lora.LoraName,
                        ["strength_model"] = lora.StrengthModel,
                        ["strength_clip"] = lora.StrengthClip,
                        ["model"] = currentModelLink, // Подключаем к предыдущему звену
                        ["clip"] = currentClipLink    // Подключаем к предыдущему звену
                    },
                    ["class_type"] = "LoraLoader",
                    ["_meta"] = new JObject
                    {
                        ["title"] = $"Динамическая LoRA {i + 1}: {lora.LoraName}"
                    }
                };

                // Добавляем ноду в граф
                graph[newLoraNodeId] = loraNode;

                // Теперь это звено становится "источником" для следующего шага
                currentModelLink = new JArray { newLoraNodeId, 0 };
                currentClipLink = new JArray { newLoraNodeId, 1 };
            }

            // 6. Замыкаем цепочку: подключаем выходы последней созданной LoRA к таргетам
            graph[targetNodeIdForModel]["inputs"][targetModelKey] = currentModelLink;
            graph[targetNodeIdForClip]["inputs"][targetClipKey] = currentClipLink;

            return graph.ToString();
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
    /// РЕЖИМ Z-IMAGE: Генерация на основе референса (Img2Img / Inpainting)
    /// </summary>
    /// <param name="positivePrompt">Уточняющий промпт для изменений</param>
    /// <param name="baseImageBase64">Исходная обложка в формате Base64</param>
    /// <param name="denoisingStrength">Сила изменений (0.0 - копия, 1.0 - полная перерисовка). Из Манифеста 0.3 дефолт: 0.35</param>
    public async Task<string> EditImageWithReferenceAsync(string positivePrompt, string baseImageBase64, double denoisingStrength = 0.35)
    {
        try
        {
            // Проверяем доступность ComfyUI
            await CheckComfyUiHealthAsync();

            // 1. Первым делом загружаем картинку-донор в ComfyUI
            string uploadedFileName = await UploadImageToComfyUiAsync(baseImageBase64);

            // 2. Загружаем шаблон workflow для редактирования
            // Для Img2Img нужен граф, содержащий ноду LoadImage и KSampler со входом latent от VAE Encode!
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "EventHorizon_Img2Img.json");
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа для Img2Img не найден по пути: {workflowPath}. Выгрузи API JSON с нодой LoadImage!");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath);
            var graph = JsonNode.Parse(workflowJson)?.AsObject();
            if (graph == null) throw new InvalidOperationException("Не удалось распарсить JSON графа Img2Img.");

            // 3. Инжектируем имя файла в ноду LoadImage (Допустим, её ID в графе = "10")
            string loadImageNodeId = "10";
            if (graph.TryGetPropertyValue(loadImageNodeId, out var loadNode) && loadNode?["inputs"] is JsonObject loadInputs)
            {
                loadInputs["image"] = uploadedFileName;
                _logger.LogInformation("Имя файла {File} успешно внедрено в ноду LoadImage ({Id})", uploadedFileName, loadImageNodeId);
            }
            else
            {
                _logger.LogWarning("Нода {Id} (LoadImage) не найдена в графе! Проверь EventHorizon_Img2Img.json", loadImageNodeId);
            }

            // 4. Инжектируем силу изменения (denoising_strength) в KSampler (Допустим, его ID = "3")
            string kSamplerNodeId = "3";
            if (graph.TryGetPropertyValue(kSamplerNodeId, out var samplerNode) && samplerNode?["inputs"] is JsonObject samplerInputs)
            {
                samplerInputs["denoise"] = denoisingStrength;
                _logger.LogInformation("Сила денойза {Denoise} успешно внедрена в KSampler ({Id})", denoisingStrength, kSamplerNodeId);
            }

            // 5. Инжектируем новый позитивный промпт (Допустим, CLIPTextEncode имеет ID = "6")
            string positiveNodeId = "6"; 
            if (graph.TryGetPropertyValue(positiveNodeId, out var posNode) && posNode?["inputs"] is JsonObject posInputs)
            {
                posInputs["text"] = positivePrompt;
                _logger.LogInformation("Промпт точечной редактуры внедрен в ноду {Id}", positiveNodeId);
            }

            // 6. Отправляем в очередь выполнения
            var requestBody = new JsonObject { ["prompt"] = graph };
            var response = await _httpClient.PostAsJsonAsync($"{ComfyUrl}/prompt", requestBody);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();
            var queueResult = JsonSerializer.Deserialize<ComfyQueueResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (queueResult == null || string.IsNullOrEmpty(queueResult.PromptId))
                throw new Exception("ComfyUI не вернул PromptId задачи.");

            // 7. Поллинг и скачивание (используем твою отлаженную логику)
            return await PollAndDownloadResultAsync(queueResult.PromptId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка в режиме редактирования Z-Image (Img2Img)");
            throw;
        }
    }

    public async Task<string> GenerateEventHorizonAsync(string positivePrompt, string negativePrompt, TechnicalSpecDto artArchitectorSpec)
    {
        try
        {
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "EventHorizon.json");
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа ComfyUI не найден: {workflowPath}");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath);

            // 1. Запрашиваем у ИИ-оркестратора Лоры на основе входящего промпта
            // Передаем "All" или дефолтный жанр, так как ИИ-агент внутри сам разберется по контексту текста
            var selectedLoras = await _loraOrchestrator.PrepareLorasForGenerationAsync("All", artArchitectorSpec);

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

            // 3. Мапим в LoraConfig для врезки файлов в ноды
            var lorasToInject = selectedLoras.Select(l => new LoraConfig
            {
                LoraName = l.FileName,
                StrengthModel = l.StrengthModel,
                StrengthClip = l.StrengthClip
            }).ToList();

            // 4. Вот теперь передаем в оркестратора граф и Лоры для динамической врезки цепочки
            string loraGraph = ComfyUiGraphOrchestrator.InjectLoraChainIntoGraph(workflowJson, lorasToInject);

            var graph = JsonNode.Parse(loraGraph)?.AsObject();

            if (graph == null) throw new InvalidOperationException("Не удалось распарсить JSON.");

            string positiveNodeId = "6"; 
            if (graph.TryGetPropertyValue(positiveNodeId, out var posNode) && posNode?["inputs"] is JsonObject posInputs)
            {
                posInputs["text"] = positivePrompt;
            }

            string negativeNodeId = "7"; 
            if (graph.TryGetPropertyValue(negativeNodeId, out var negNode) && negNode?["inputs"] is JsonObject negInputs)
            {
                negInputs["text"] = negativePrompt;
            }

            await CheckComfyUiHealthAsync();

            var requestBody = new JsonObject { ["prompt"] = graph };
            var response = await _httpClient.PostAsJsonAsync($"{ComfyUrl}/prompt", requestBody);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();
            var queueResult = JsonSerializer.Deserialize<ComfyQueueResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return await PollAndDownloadResultAsync(queueResult!.PromptId);
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

    private async Task<string> PollAndDownloadResultAsync(string promptId)
    {
        string? fileName = null;
        string? subFolder = null;
        bool isCompleted = false;
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
                _logger.LogInformation("Генерация в ComfyUI завершена! Извлекаем файлы...");
                
                if (taskDetails.TryGetProperty("outputs", out var outputs))
                {
                    foreach (var nodeOutput in outputs.EnumerateObject())
                    {
                        if (nodeOutput.Value.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                        {
                            var firstImage = images[0];
                            fileName = firstImage.GetProperty("filename").GetString();
                            if (firstImage.TryGetProperty("subfolder", out var subProp))
                            {
                                subFolder = subProp.GetString();
                            }
                            isCompleted = true;
                            break;
                        }
                    }
                }
                break;
            }
        }

        if (!isCompleted || string.IsNullOrEmpty(fileName))
        {
            throw new TimeoutException("Превышено время ожидания генерации локальной модели в ComfyUI.");
        }

        string viewUrl = $"{ComfyUrl}/view?filename={Uri.EscapeDataString(fileName)}&type=output";
        if (!string.IsNullOrEmpty(subFolder))
        {
            viewUrl += $"&subfolder={Uri.EscapeDataString(subFolder)}";
        }

        byte[] imageBytes = await _httpClient.GetByteArrayAsync(viewUrl);
        
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "covers");
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        string localPath = Path.Combine(outputDirectory, $"{promptId}.png");
        await File.WriteAllBytesAsync(localPath, imageBytes);
        _logger.LogInformation("Файл сохранен локально: {Path}", localPath);

        return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
    }
}

// Вспомогательный DTO класс для десериализации ответа очереди
public class ComfyQueueResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("prompt_id")]
    public string PromptId { get; set; } = string.Empty;
}