using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using AIWriterPublisher.Api.Models.DTO;
using AIWriterPublisher.Api.Services;
using AIWriterPublisher.Api.Agents.HeroFaceAgent;
using AIWriterPublisher.Api.Agents.HeroCharAgent;
using AIWriterPublisher.Api.Agents.LoraAgent;
using AIWriterPublisher.Api.Models;

namespace AIWriterPublisher.Api.Services
{
    public class HeroCharacterOrchestrator
    {
        private readonly HeroFaceAgent _faceAgent;
        private readonly HeroCharAgent _charAgent;
        private readonly HeroPortraitLoraAgent _loraAgent;
        private readonly ComfyUiImageGenerator _comfyGenerator; 
        private readonly List<LoraDefinition> _allPortraitLoras;

        public HeroCharacterOrchestrator(
            HeroFaceAgent faceAgent,
            HeroCharAgent charAgent,
            HeroPortraitLoraAgent loraAgent,
            ComfyUiImageGenerator comfyGenerator)
        {
            _faceAgent = faceAgent;
            _charAgent = charAgent;
            _loraAgent = loraAgent;
            _comfyGenerator = comfyGenerator;            

            string path = Path.Combine(AppContext.BaseDirectory, "lora_portrait_manifest.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<LoraRootManifest>(json);
                _allPortraitLoras = root?.AvailableLoras ?? new List<LoraDefinition>();
            }
            else
            {
                _allPortraitLoras = new List<LoraDefinition>(); 
            }
        }

        // Внутренний метод оркестратора, который фильтрует, запрашивает нейронку Лары и вешает триггеры
        public async Task<List<LoraPreset>> PredictPortraitLorasAsync(
            string technicalPrompt, 
            bool isFullBody, 
            CancellationToken cancellationToken = default,
            string analysisModel = "gpt-oss:120b")
        {
            // 1. Фильтруем Лоры на уровне C#, чтобы не забивать контекст ИИ
            var filteredLoras = _allPortraitLoras.Where(l => 
                l.Category == "Enhancer" || 
                (isFullBody && (l.Category == "Clothing" || l.Category == "Environment" || l.Category == "Pose")) ||
                (!isFullBody && (l.Category == "Portrait" || l.Category == "FaceDetail"))
            ).ToList();

            // 2. Мапим во встроенные облегченные DTO для ИИ
            var lorasForAgent = filteredLoras.Select(l => new LoraPreset
            {
                DisplayName = l.DisplayName,
                FileName = l.FileName,
                DefaultWeight = l.DefaultWeight,
                Description = l.Description 
            }).ToList();

            if (!lorasForAgent.Any())
            {
                return new List<LoraPreset>();
            }

            // 3. Запрашиваем у агента Лары выбор моделей и расчет весов под наш промпт
            // Передаем правильные аргументы, которые ожидает _loraAgent
            List<LoraPreset> selectedByAi = await _loraAgent.PredictPortraitLorasAsync(technicalPrompt, lorasForAgent, cancellationToken);

            // 4. Обогащаем выбор ИИ триггер-вордами из нашего мастер-манифеста
            foreach (var selected in selectedByAi)
            {
                var original = _allPortraitLoras.FirstOrDefault(x => x.FileName == selected.FileName);
                if (original != null && !string.IsNullOrEmpty(original.TriggerWords))
                {
                    selected.TriggerWords = original.TriggerWords; 
                }
            }

            return selectedByAi;
        }

        public async Task<CharacterGenerationResponse> ProcessGenerationAsync(
            CharacterGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CharacterGenerationResponse character = new CharacterGenerationResponse();
            string agentReview = string.Empty;

            // =================================================================
            // ШАГ 1: Получаем чистый английский промпт от специализированного агента
            // =================================================================
            if (!request.IsFullBody)
            {
                // Цепочка Миры (Лицо)
                character = await _faceAgent.GenerateFacePromptAsync(request.UserDescription, cancellationToken);
                agentReview = character.AgentReview;
            }
            else
            {
                // Цепочка Кары (Полный рост)
                character = await _charAgent.GenerateCharPromptAsync(request.UserDescription, cancellationToken);
                agentReview = character.AgentReview;
            }

            // =================================================================
            // ШАГ 2: Подбор LoRA и обогащение промпта триггер-вордами
            // =================================================================
            // ИСПРАВЛЕНО: Вызываем собственный метод этого же класса через `this` (или просто напрямую),
            // передавая туда промпт от ИИ, флаг полного роста и модель анализа.
            var predictedLoras = await this.PredictPortraitLorasAsync(
                character.GeneratedPrompt, 
                request.IsFullBody, 
                cancellationToken, 
                request.AnalysisModel);

            // Собираем триггеры в одну строку
            var triggerWordsBuilder = new StringBuilder();
            foreach (var lora in predictedLoras)
            {
                if (!string.IsNullOrWhiteSpace(lora.TriggerWords))
                {
                    triggerWordsBuilder.Append($"{lora.TriggerWords}, ");
                }
            }

            // Мёржим триггеры в начало промпта для максимального веса при генерации
            string finalTechnicalPrompt = character.GeneratedPrompt;
            if (triggerWordsBuilder.Length > 0)
            {
                finalTechnicalPrompt = $"{triggerWordsBuilder}{character.GeneratedPrompt}";
            }

            // =================================================================
            // ШАГ 3: Запись в граф и инференс
            // =================================================================
            string generatedImageUrl = await _comfyGenerator.ExecuteCharacterInferenceAsync(
                finalTechnicalPrompt,
                predictedLoras,
                request.FaceReferenceUrl,
                cancellationToken
            );

            // Сборка ответа по CharacterGenerationResponse
            return new CharacterGenerationResponse
            {
                GeneratedPrompt = finalTechnicalPrompt,
                AgentReview = agentReview,
                ImageUrl = generatedImageUrl
            };
        }
    }
}