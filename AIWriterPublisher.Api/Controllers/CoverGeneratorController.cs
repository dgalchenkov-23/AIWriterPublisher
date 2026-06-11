using Microsoft.AspNetCore.Mvc;
using AIWriterPublisher.Api.Agents.ArtDirector;
using AIWriterPublisher.Api.Agents.PromptEngineer;
using AIWriterPublisher.Api.Agents.ArtArchitector;
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
        private readonly IImageGenerator _imageGenerator;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public CoverGeneratorController(
            ArtDirectorAgent artDirectorAgent,
            ArtArchitectorAgent artArchitector,
            PromptEngineerAgent promptEngineerAgent,
            IImageGenerator imageGenerator,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _artDirectorAgent = artDirectorAgent;
            _artArchitector = artArchitector;
            _promptEngineerAgent = promptEngineerAgent;
            _imageGenerator = imageGenerator;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        [HttpPost("brainstorm")]
        public async Task<IActionResult> Brainstorm([FromBody] BookDescriptionRequest request)
        {
            try
            {
                string jsonResult = await _artDirectorAgent.BrainstormConceptsAsync(request.Genre, request.Description);
                return Content(jsonResult, "application/json");
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
                string flatFluxPrompt;
                Console.WriteLine($"[Direct Engine] Запуск генерации. Режим: {request.Mode}");
                
                TechnicalSpecDto finalSpec = request.TechnicalSpec ?? new TechnicalSpecDto();
                Console.WriteLine($"[Direct Engine] Полученные слои ТЗ: Subject='{finalSpec.Subject}', Environment='{finalSpec.Environment}', StyleAndLight='{finalSpec.Style}', Composition='{finalSpec.Composition}'");
                
                if (request.Mode == "raw-parse")
                {
                    Console.WriteLine($"[Direct Engine] Режим «Магический поток» активирован. Запускаем парсер...");
                    if (string.IsNullOrWhiteSpace(request.RawPrompt))
                        return BadRequest("Промпт для распознавания не может быть пустым.");

                    finalSpec = await _artArchitector.AnalyzeUserInputAsync(request.RawPrompt, request.RenderSettings?.Provider?.ToLower());
                    flatFluxPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec);
                }
                else 
                {
                    Console.WriteLine("[Direct Engine] Режим «Строгие слои» активирован. Используем предоставленную структуру ТЗ.");
                    flatFluxPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec);
                }

                Console.WriteLine($"[Direct Engine] Итоговый плоский промпт для Flux: {flatFluxPrompt}");

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
                        base64Image = await realGenerator.GenerateFluxViaPollinationsAsync(flatFluxPrompt, width, height, finalSpec);
                    }
                    else
                    {
                        base64Image = await _imageGenerator.GenerateImageAsync(flatFluxPrompt, finalSpec, targetAspectRatio);
                    }
                }
                else
                {
                    Console.WriteLine("[Direct Engine] Выбран стандартный движок генерации проекта (ComfyUI).");
                    if (_imageGenerator is ComfyUiImageGenerator comfyGenerator)
                    {
                        base64Image = await comfyGenerator.GenerateImageAsync(flatFluxPrompt, finalSpec, targetAspectRatio);
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
            Console.WriteLine("[Z-Image Engine] Получен запрос на редактирование по референсу.");

            if (string.IsNullOrWhiteSpace(request.BaseImageBase64))
            {
                return BadRequest(new { error = "Для редактирования необходимо передать базовое изображение в формате Base64 (BaseImageBase64)." });
            }

            try
            {
                // 1. Формируем промпт изменений (извлекаем из слоев или сырого текста)
                string modificationPrompt = string.Empty;

                if (request.Mode == "raw-parse" && !string.IsNullOrWhiteSpace(request.RawPrompt))
                {
                    // Если Катя ввела "Убери кота и сделай небо багровым" в свободном стиле
                    var parsedSpec = await _artArchitector.AnalyzeUserInputAsync(request.RawPrompt, "comfyui");
                    modificationPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(parsedSpec);
                }
                else
                {
                    // Если правки переданы четко структурировано в TechnicalSpec
                    TechnicalSpecDto spec = request.TechnicalSpec ?? new TechnicalSpecDto();
                    modificationPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(spec);
                }

                if (string.IsNullOrWhiteSpace(modificationPrompt))
                {
                    return BadRequest(new { error = "Промпт изменений (описание того, что перерисовать) не может быть пустым." });
                }

                Console.WriteLine($"[Z-Image Engine] Итоговый промпт правок: {modificationPrompt}");

                // 2. Достаем денойз из настроек (дефолт 0.35 из Манифеста 0.3)
                double denoise = request.DenoisingStrength ?? 0.35;
                Console.WriteLine($"[Z-Image Engine] Сила денойза: {denoise}");

                // 3. Вызываем локальный ComfyUI генератор через приведение типов
                if (_imageGenerator is ComfyUiImageGenerator comfyGenerator)
                {
                    Console.WriteLine("[Z-Image Engine] Передаем управление в ComfyUiImageGenerator...");
                    string resultBase64 = await comfyGenerator.EditImageWithReferenceAsync(modificationPrompt, request.BaseImageBase64, denoise);

                    // Гарантируем префикс для корректного отображения на HTML5 Canvas
                    if (!resultBase64.StartsWith("data:") && !resultBase64.StartsWith("http"))
                    {
                        resultBase64 = $"data:image/png;base64,{resultBase64}";
                    }

                    return Ok(new DirectGenerationResponse 
                    { 
                        ImageBase64 = resultBase64,
                        ParsedSpec = request.Mode == "raw-parse" ? request.TechnicalSpec : null
                    });
                }
                else
                {
                    return StatusCode(501, new { error = "Режим Z-Image (Img2Img) поддерживается только локальным движком ComfyUI. Проверьте конфигурацию DI." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Z-Image Error] Крах эндпоинта правок: {ex.Message}");
                return StatusCode(500, new { error = $"Ошибка при редактировании по референсу: {ex.Message}" });
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