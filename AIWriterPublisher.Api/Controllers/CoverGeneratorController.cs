using Microsoft.AspNetCore.Mvc;
using AIWriterPublisher.Api.Agents.ArtDirector;
using AIWriterPublisher.Api.Agents.PromptEngineer;
using AIWriterPublisher.Api.Agents.ArtArchitector;
using AIWriterPublisher.Api.Services;
using AIWriterPublisher.Api.Models;
using AIWriterPublisher.Api.Models.DTO; // Наш новый неймспейс с DTO
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

        // [HttpPost("generate")]
        // public async Task<IActionResult> GenerateCover([FromBody] CoverGenerationRequest request)
        // {
        //     if (string.IsNullOrWhiteSpace(request.VisualElements))
        //         return BadRequest("Визуальные элементы концепта не могут быть пустыми.");

        //     try
        //     {
        //         // ПОЧИНЕНО: Генерируем технический промпт через агента на основе VisualElements из Request
        //         //string finalPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(request.VisualElements, request.Genre);

        //         string imageUrl = string.Empty;
        //         if (request.Engine?.ToLower() == "pollinations")
        //         {
        //             string encodedPrompt = Uri.EscapeDataString(finalPrompt);
        //             string polyUrl = $"https://image.pollinations.ai/p/{encodedPrompt}?width=512&height=768&nologo=true&seed={Random.Shared.Next(1, 999999)}";
        //             byte[] imageBytes = await _httpClient.GetByteArrayAsync(polyUrl);
        //             imageUrl = $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
        //         }
        //         else
        //         {
        //             // Вызываем генератор (по умолчанию передаст дефолтный aspectRatio "2:3")
        //             imageUrl = await _imageGenerator.GenerateImageAsync(finalPrompt);
        //         }

        //         return Ok(new CoverGenerationResponse { TechnicalPrompt = finalPrompt, ImageUrl = imageUrl });
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, $"Ошибка при генерации обложки: {ex.Message}");
        //     }
        // }

        /// <summary>
        /// ЭНДПОИНТ ДЛЯ РЕЖИМА «ТВОРЕЦ» (Direct Prompt)
        /// Обрабатывает ручной ввод по слоям или парсит «Магический поток» через LLM,
        /// после чего склеивает монолитное ТЗ и рендерит графику.
        /// </summary>
        [HttpPost("direct-generate")]
        public async Task<IActionResult> DirectGenerate([FromBody] DirectGenerationRequest request)
        {
            Console.WriteLine($"[Direct Engine] Запуск генерации. Режим: {request.Mode}");
            try
            {
                string flatFluxPrompt;
                Console.WriteLine($"[Direct Engine] Запуск генерации. Режим: {request.Mode}");
                
                TechnicalSpecDto finalSpec = request.TechnicalSpec ?? new TechnicalSpecDto();
                Console.WriteLine($"[Direct Engine] Полученные слои ТЗ: Subject='{finalSpec.Subject}', Environment='{finalSpec.Environment}', StyleAndLight='{finalSpec.Style}', Composition='{finalSpec.Composition}'");
                // РЕЖИМ 1: Магический поток (Требуется разбор строки на JSON-структуру)
                if (request.Mode == "raw-parse")
                {
                    Console.WriteLine($"[Direct Engine] Режим «Магический поток» активирован. Запускаем парсер...");
                    if (string.IsNullOrWhiteSpace(request.RawPrompt))
                        return BadRequest("Промпт для распознавания не может быть пустым.");

                    Console.WriteLine("[Direct Engine] Запуск ИИ-парсера для Магического потока...");

                    finalSpec = await _artArchitector.AnalyzeUserInputAsync(request.RawPrompt, request.RenderSettings?.Provider?.ToLower());
                    flatFluxPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec);
                }
                else //Иначе режим 2: Строгие слои (Пользователь уже заполнил техническую структуру, просто используем её)
                {
                    Console.WriteLine("[Direct Engine] Режим «Строгие слои» активирован. Используем предоставленную структуру ТЗ.");
                    flatFluxPrompt = await _promptEngineerAgent.GenerateTechnicalPromptAsync(finalSpec);
                }

                Console.WriteLine($"[Direct Engine] Итоговый плоский промпт для Flux: {flatFluxPrompt}");

                // ВЫБОР ДВИЖКА РЕНДЕРА (На базе RenderSettings)
                string base64Image = string.Empty;
                string width = (request.RenderSettings?.Dimensions?.Width ?? 1024).ToString();
                string height = (request.RenderSettings?.Dimensions?.Height ?? 1024).ToString();

                // Определяем соотношение сторон для интерфейса IImageGenerator
                string targetAspectRatio = "2:3";
                if (width == "1024" && height == "1024") targetAspectRatio = "1:1";
                else if (int.Parse(width) > int.Parse(height)) targetAspectRatio = "3:2";

                // Проверяем, кто у нас провайдер в настройках рендера
                if (request.RenderSettings?.Provider?.ToLower() == "huggingface")
                {
                    Console.WriteLine("[Direct Engine] Выбран Hugging Face (Flux.1-dev). Вызываем выделенный метод...");
                    
                    if (_imageGenerator is RealImageGenerator realGenerator)
                    {
                        // base64Image = await realGenerator.GenerateFluxViaHuggingFaceAsync(flatFluxPrompt, width, height);
                        base64Image = await realGenerator.GenerateFluxViaPollinationsAsync(flatFluxPrompt, width, height);
                    }
                    else
                    {
                        base64Image = await _imageGenerator.GenerateImageAsync(flatFluxPrompt, targetAspectRatio);
                    }
                }
                else
                {
                    Console.WriteLine("[Direct Engine] Выбран стандартный движок генерации проекта.");
                    ComfyUiImageGenerator comfyGenerator = _imageGenerator as ComfyUiImageGenerator;
                    if (comfyGenerator != null)
                    {
                        base64Image = await comfyGenerator.GenerateImageAsync(flatFluxPrompt, targetAspectRatio);
                    }
                }

                // Гарантируем, что строка уйдет на фронт с b64-префиксом, если сервис вернул чистую строку
                if (!base64Image.StartsWith("data:") && !base64Image.StartsWith("http"))
                {
                    base64Image = $"data:image/png;base64,{base64Image}";
                }

                // ФОРМИРУЕМ ОТВЕТ
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