using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AIWriterPublisher.Api.Models.DTO; 

namespace AIWriterPublisher.Api.Services;

public sealed class ComfyUiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ComfyUiImageGenerator> _logger;
    private const string ComfyUrl = "http://127.0.0.1:8188"; // Стандартный порт ComfyUI

    public ComfyUiImageGenerator(HttpClient httpClient, ILogger<ComfyUiImageGenerator> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 минут на генерацию
        _logger = logger;
    }

    public async Task<string> GenerateImageAsync(string technicalPrompt, string aspectRatio = "2:3")
    {
        // Маппим привычные Кате пропорции обложки в пиксели, которые понимает Flux.1-dev
        // Базируемся на золотом стандарте ~1 мегапиксель для Flux
        string width = "1024";
        string height = "1024";

        if (aspectRatio == "2:3")
        {
            width = "832";
            height = "1248"; // Идеальное вертикальное разрешение под книжную обложку
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

        // Вызываем наш прямой enterprise-пайплайн к Hugging Face
        // return await GenerateFluxViaHuggingFaceAsync(technicalPrompt, width, height);
        return await GenerateEventHorizonAsync(technicalPrompt, "");
    }

    

    public async Task<string> GenerateEventHorizonAsync(string positivePrompt, string negativePrompt)
    {
        try
        {
            // 1. Загружаем сохраненный API-граф (шаблон workflow)
            // Файл должен лежать в корне проекта или в App_Data/flux_schnell_api.json
            string workflowPath = Path.Combine(AppContext.BaseDirectory, "EventHorizon.json");
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"Шаблон графа ComfyUI не найден по пути: {workflowPath}. Выгрузи его из UI в формате API JSON!");
            }

            string workflowJson = await File.ReadAllTextAsync(workflowPath);
            var graph = JsonNode.Parse(workflowJson)?.AsObject();

            if (graph == null)
                throw new InvalidOperationException("Не удалось распарсить JSON графа ComfyUI.");

            // 2. Инжектируем промпты в правильные ноды графа.
            // ВНИМАНИЕ: Проверь ID нод в твоем сохраненном JSON! 
            // Обычно во Flux нода текста — это CLIPTextEncode. Допустим, её ID = "6"
            
            // Навешиваем позитивный промпт
            string positiveNodeId = "6"; 
            if (graph.TryGetPropertyValue(positiveNodeId, out var posNode) && posNode?["inputs"] is JsonObject posInputs)
            {
                posInputs["text"] = positivePrompt;
                _logger.LogInformation("Позитивный промпт успешно внедрен в ноду {Id}", positiveNodeId);
            }
            else
            {
                // Лог-предупреждение, если ID отличается (например, используется другой кастомный граф)
                _logger.LogWarning("Нода {Id} для позитивного текста не найдена в графе! Проверь EventHorizon.json", positiveNodeId);
            }

            // 3. Собираем финальное тело запроса. 
            // ComfyUI ждет JSON, где граф обернут в свойство "prompt"
            var requestBody = new JsonObject
            {
                ["prompt"] = graph
            };

            // Проверяем, жив ли ComfyUI
            try
            {
                var pingResponse = await _httpClient.GetAsync($"{ComfyUrl}/");
                if (!pingResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("ComfyUI недоступен. Код: {StatusCode}", pingResponse.StatusCode);
                    throw new Exception("ComfyUI не отвечает. Запусти run_nvidia_gpu.bat");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ComfyUI не отвечает по адресу {Url}", ComfyUrl);
                throw;
            }

            // 4. Пушим задачу в очередь ComfyUI (POST /prompt)
            var response = await _httpClient.PostAsJsonAsync($"{ComfyUrl}/prompt", requestBody);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();
            var queueResult = System.Text.Json.JsonSerializer.Deserialize<ComfyQueueResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Учитываем регистр для полей типа 'prompt_id' в JSON
            });
            if (queueResult == null || string.IsNullOrEmpty(queueResult.PromptId))
            {
                throw new Exception("ComfyUI не вернул PromptId задачи.");
            }

            string promptId = queueResult.PromptId;
            _logger.LogInformation("Задача успешно добавлена в очередь ComfyUI. Prompt ID: {Id}", promptId);

            // 5. Поллинг (Опрос готовности). Проверяем статус задачи через GET /history/{promptId}
            string? fileName = null;
            string? subFolder = null;
            bool isCompleted = false;
            int maxAttempts = 120; // 120 попыток по 2 секунды = 4 минуты таймаута

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2000); // Ждем 2 секунды перед проверкой

                var historyResponse = await _httpClient.GetAsync($"{ComfyUrl}/history/{promptId}");
                if (!historyResponse.IsSuccessStatusCode) continue;

                string historyJson = await historyResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(historyJson);
                
                // Если в корневом объекте истории появился наш promptId, значит генерация завершена
                if (doc.RootElement.TryGetProperty(promptId, out var taskDetails))
                {
                    _logger.LogInformation("Генерация в ComfyUI завершена! Извлекаем файлы...");
                    
                    // Пробиваемся сквозь дебри JSON ответов истории к ноде сохранения (обычно SaveImage, ID = "9")
                    // Структура: [promptId] -> outputs -> [nodeId] -> images -> [0] -> filename
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

            // 6. Скачиваем готовую картинку через эндпоинт /view
            string viewUrl = $"{ComfyUrl}/view?filename={Uri.EscapeDataString(fileName)}&type=output";
            if (!string.IsNullOrEmpty(subFolder))
            {
                viewUrl += $"&subfolder={Uri.EscapeDataString(subFolder)}";
            }

            byte[] imageBytes = await _httpClient.GetByteArrayAsync(viewUrl);
            _logger.LogInformation("Бинарник изображения успешно скачан из ComfyUI. Размер: {Size} байт", imageBytes.Length);

            // 7. Сохраняем в локальные ассеты проекта (согласно разделу 5 Манифеста 0.3)
            string outputDirectory = Path.Combine(AppContext.BaseDirectory, "covers");
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string localPath = Path.Combine(outputDirectory, $"{promptId}.png");
            await File.WriteAllBytesAsync(localPath, imageBytes);
            _logger.LogInformation("Файл сохранен локально: {Path}", localPath);

            // 8. Возвращаем стандартную Base64 строку для мгновенного отображения у Кати на фронте
            return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при работе с локальным инференсом ComfyUI");
            throw;
        }
    }
}