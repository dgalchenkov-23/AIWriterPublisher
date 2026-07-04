using Microsoft.AspNetCore.Mvc;
using AIWriterPublisher.Api.Agents.ArtDirector;
using AIWriterPublisher.Api.Agents.PromptEngineer;
using AIWriterPublisher.Api.Agents.ArtArchitector;
using AIWriterPublisher.Api.Agents.VisionEditorAgent;
using AIWriterPublisher.Api.Services;
using AIWriterPublisher.Api.Models;
using AIWriterPublisher.Api.Models.DTO; 
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace AIWriterPublisher.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoverGeneratorController : ControllerBase
    {
        private readonly ArtDirectorAgent _artDirectorAgent;
        private readonly ArtArchitectorAgent _artArchitector;
        private readonly PromptEngineerAgent _promptEngineerAgent;
        private readonly VisionEditorAgent _visionAgent;
        private readonly IImageGenerator _imageGenerator;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CoverGeneratorController> _logger;

        public CoverGeneratorController(
            ArtDirectorAgent artDirectorAgent,
            ArtArchitectorAgent artArchitector,
            PromptEngineerAgent promptEngineerAgent,
            VisionEditorAgent visionAgent,
            IImageGenerator imageGenerator,
            HttpClient httpClient,
            ILogger<CoverGeneratorController> logger,
            IConfiguration configuration)
        {
            _artDirectorAgent = artDirectorAgent;
            _artArchitector = artArchitector;
            _promptEngineerAgent = promptEngineerAgent;
            _visionAgent = visionAgent;
            _imageGenerator = imageGenerator;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("brainstorm")]
        public async Task<IActionResult> Brainstorm([FromBody] BookDescriptionRequest request)
        {
            try
            {
                List<ArtConcept> bookConepts = new List<ArtConcept>();

                bookConepts = await _artDirectorAgent.BrainstormConceptsAsync(request.Genre, request.Description);
                return Ok(bookConepts);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// ЭНДПОИНТ ДЛЯ РЕЖИМА «ТВОРЕЦ» (Direct Prompt - С чистого листа)
        /// </summary>
        [HttpPost("direct-generate")]
        public async Task<IActionResult> DirectGenerate([FromBody] DirectGenerationRequest request)
        {
            try
            {
                EngineeringSpecDto engineeringSpec = new EngineeringSpecDto();
                Console.WriteLine($"[Direct Engine] Запуск генерации. Режим: {request.Mode}");
                
                TechnicalSpecDto finalSpec = request.TechnicalSpec ?? new TechnicalSpecDto();
                Console.WriteLine($"[Direct Engine] Полученные слои ТЗ: Subject='{finalSpec.Subject}', Environment='{finalSpec.Environment}', StyleAndLight='{finalSpec.Style}', Composition='{finalSpec.Composition}'");
                
                if (request.Mode == "raw-parse")
                {
                    Console.WriteLine($"[Direct Engine] Режим «Магический поток» активирован. Запускаем парсер...");
                    if (string.IsNullOrWhiteSpace(request.RawPrompt))
                        return BadRequest("Промпт для распознавания не может быть пустым.");

                    finalSpec = await _artArchitector.AnalyzeUserInputAsync(request.RawPrompt, request.AnalysisModel);
                    engineeringSpec = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec, request.AnalysisModel);
                }
                else 
                {
                    Console.WriteLine("[Direct Engine] Режим «Строгие слои» активирован. Используем предоставленную структуру ТZ.");
                    engineeringSpec = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec, request.AnalysisModel);
                }

                Console.WriteLine($"[Direct Engine] Итоговый плоский промпт для модели: {engineeringSpec.PositivePrompt}");

                string base64Image = string.Empty;
                string width = (request.RenderSettings?.Dimensions?.Width ?? 1024).ToString();
                string height = (request.RenderSettings?.Dimensions?.Height ?? 1024).ToString();

                string targetAspectRatio = "2:3";
                if (width == "1024" && height == "1024") targetAspectRatio = "1:1";
                else if (int.Parse(width) > int.Parse(height)) targetAspectRatio = "3:2";

                if (request.RenderSettings?.Provider?.ToLower() == "huggingface")
                {
                    Console.WriteLine("[Direct Engine] Выбран Hugging Face (Flux.1-dev). Вызываем выделенный метод...");
                    if (_imageGenerator is RealImageGenerator realGenerator)
                    {
                        base64Image = await realGenerator.GenerateFluxViaPollinationsAsync(engineeringSpec.PositivePrompt, width, height, finalSpec);
                    }
                    else
                    {
                        base64Image = await _imageGenerator.GenerateImageAsync(engineeringSpec, finalSpec, request.AnalysisModel, targetAspectRatio);
                    }
                }
                else
                {
                    Console.WriteLine("[Direct Engine] Выбран стандартный движок генерации проекта (ComfyUI).");
                    if (_imageGenerator is ComfyUiImageGenerator comfyGenerator)
                    {
                        base64Image = await comfyGenerator.GenerateImageAsync(engineeringSpec, finalSpec, request.AnalysisModel, targetAspectRatio);
                    }
                }

                if (!base64Image.StartsWith("data:") && !base64Image.StartsWith("http"))
                {
                    base64Image = $"data:image/png;base64,{base64Image}";
                }

                var response = new DirectGenerationResponse
                {
                    ImageBase64 = base64Image,
                    ParsedSpec = request.Mode == "raw-parse" ? finalSpec : null
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Direct Error] Крах эндпоинта: {ex.Message}");
                return StatusCode(500, new { error = $"Ошибка Direct-генерации: {ex.Message}" });
            }
        }

        /// <summary>
        /// РЕЖИМ Z-IMAGE: Эндпоинт точечного редактирования обложки по референсу (Img2Img)
        /// Принимает базовое изображение в Base64, силу изменений (denoise) и текстовое описание правок.
        /// </summary>
        [HttpPost("edit-reference")]
        public async Task<IActionResult> EditWithReference([FromBody] DirectGenerationRequest request)
        {
            _logger.LogInformation("[Z-Image Engine] Получен запрос на Vision-редактирование.");

            if (string.IsNullOrWhiteSpace(request.BaseImageBase64))
            {
                return BadRequest(new { error = "Для редактирования необходимо передать изображение (BaseImageBase64)." });
            }

            try
            {
                // 1. ПОДГОТОВКА ФАЙЛА (Манифест v0.4: работаем через локальные пути)
                string fileName = $"ref_{Guid.NewGuid():N}.png";
                string localPath = Path.Combine(AppContext.BaseDirectory, "covers", fileName);
                
                // Сохраняем Base64 во временный файл для анализа агентом
                var base64Data = request.BaseImageBase64.Contains(",") ? request.BaseImageBase64.Split(',')[1] : request.BaseImageBase64;
                await System.IO.File.WriteAllBytesAsync(localPath, Convert.FromBase64String(base64Data));

                ComfyUiImg2ImgSpecDto editSpec;

                // 2. ВЫБОР ПУТИ АНАЛИЗА
                if (request.Mode == "raw-parse" && !string.IsNullOrWhiteSpace(request.RawPrompt))
                {
                    // РЕЖИМ VISION: Nex-N2-Pro "смотрит" на картинку и "читает" Магический поток
                    _logger.LogInformation("[Z-Image] Запуск Vision-анализа (Nex-N2-Pro)...");
                    editSpec = await _visionAgent.AnalyzeEditRequestAsync(localPath, request.RawPrompt);
                }
                else
                {
                    // РЕЖИМ СТРОГИХ СЛОЕВ: Используем PromptEngineer для сборки текста
                    _logger.LogInformation("[Z-Image] Сборка промпта из технических слоев...");
                    TechnicalSpecDto spec = request.TechnicalSpec ?? new TechnicalSpecDto();
                    EngineeringSpecDto engineeringSpec = await _promptEngineerAgent.GenerateTechnicalPromptAsync(spec, request.AnalysisModel);

                    editSpec = new ComfyUiImg2ImgSpecDto 
                    { 
                        PromptOverride = engineeringSpec.PositivePrompt,
                        DenoisingStrength = request.DenoisingStrength ?? 0.45,
                        ReferenceImagePath = localPath
                    };
                }

                // 3. ОТПРАВКА В COMFYUI
                _logger.LogWarning("[Z-Image Debug] Текущий тип генератора: {Type}", _imageGenerator.GetType().Name);
                if (_imageGenerator is ComfyUiImageGenerator comfyGenerator)
                {
                    _logger.LogInformation("[Z-Image] Инжекция в ComfyUI. Промпт: {P}", editSpec.PromptOverride);
                    
                    // Вызываем метод, принимающий полный DTO спецификации
                    string resultBase64 = "0";//await comfyGenerator.EditImageWithFireRedAsync(editSpec);

                    return Ok(new DirectGenerationResponse 
                    { 
                        ImageBase64 = resultBase64,
                        // Возвращаем то, что надумал Vision-агент для синхронизации фронта
                        ParsedSpec = request.Mode == "raw-parse" ? new TechnicalSpecDto { Subject = editSpec.PromptOverride } : null
                    });
                }

                return StatusCode(501, new { error = "Движок ComfyUI не настроен." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Z-Image Error] Крах эндпоинта правок");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        /// <summary>
        /// Вспомогательный метод-парсер. Обращается к Llama-3 на Hugging Face, 
        /// чтобы разложить текст по полочкам ТЗ без лимитов и ограничений.
        /// </summary>
        private async Task<TechnicalSpecDto> ParseRawPromptWithAiAsync(string rawPrompt)
        {
            string systemInstruction = 
                "You are a strict technical analyzer for AI Cover Studio. Your job is to parse the user's creative description " +
                "and strictly distribute it into JSON keys: subject, environment, style_and_light, composition.\n" +
                "CRITICAL RULES:\n" +
                "1. DO NOT FANTASIZE. Do not add any new elements, colors, or characters not mentioned in the text.\n" +
                "2. Translate all text to clean technical English.\n" +
                "3. Remove qualitative words like 'epic', 'stunning', 'incredible'. Keep physical descriptions only.\n" +
                "4. Output ONLY clean valid JSON object. No markdown, no ```json tags, no explanation.";

            try 
            {
                // Вытягиваем тот же самый токен, который мы юзаем для Flux!
                string hfToken = _configuration["HuggingFace:Token"] ?? "";
                if (string.IsNullOrEmpty(hfToken))
                {
                    Console.WriteLine("[HF Llama Error] Токен HuggingFace:Token не найден. Срабатывает фоллбэк.");
                    return new TechnicalSpecDto { Subject = rawPrompt };
                }

                // Официальный инференс-путь к стабильной Llama-3-8B-Instruct
                string modelUrl = "[https://api-inference.huggingface.co/models/meta-llama/Meta-Llama-3-8B-Instruct](https://api-inference.huggingface.co/models/meta-llama/Meta-Llama-3-8B-Instruct)";

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, modelUrl);
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hfToken);

                // Формируем стандартный Chat Completions payload, который понимает Llama-3-Instruct
                var requestBody = new
                {
                    inputs = $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{systemInstruction}<|eot_id|><|start_header_id|>user<|end_header_id|>\n\n{rawPrompt}<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n",
                    parameters = new
                    {
                        max_new_tokens = 500,
                        temperature = 0.1, // Выкручиваем креатив в ноль, нам нужна точность
                        return_full_text = false
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                requestMessage.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                Console.WriteLine("[HF Llama] Отправка Магического потока на растерзание Llama 3...");
                var httpResponse = await _httpClient.SendAsync(requestMessage);
                
                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errContent = await httpResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[HF Llama Error] API вернул ошибку: {httpResponse.StatusCode}. Детали: {errContent}");
                    return new TechnicalSpecDto { Subject = rawPrompt };
                }

                string responseString = await httpResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[HF Llama Raw] Ответ от модели: {responseString}");

                // Hugging Face для текстовых моделей возвращает массив объектов [{ "generated_text": "..." }]
                using var doc = JsonDocument.Parse(responseString);
                string? rawJsonText = null;

                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    rawJsonText = doc.RootElement[0].GetProperty("generated_text").GetString();
                }

                if (!string.IsNullOrEmpty(rawJsonText))
                {
                    // Чистим от возможных остатков markdown-разметки, если Llama заупрямилась
                    rawJsonText = rawJsonText.Replace("```json", "").Replace("```", "").Trim();
                    
                    Console.WriteLine($"[HF Llama Clean JSON] Парсим структуру: {rawJsonText}");

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsedSpec = JsonSerializer.Deserialize<TechnicalSpecDto>(rawJsonText, options);
                    
                    return parsedSpec ?? new TechnicalSpecDto { Subject = rawPrompt };
                }

                return new TechnicalSpecDto { Subject = rawPrompt };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HF Llama Крах] Исключение: {ex.Message}. Уходим в фоллбэк.");
                return new TechnicalSpecDto { Subject = rawPrompt };
            }
        }
    }
}