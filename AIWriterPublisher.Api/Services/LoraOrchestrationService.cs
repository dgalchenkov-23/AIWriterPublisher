using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AIWriterPublisher.Api.Models;
using AIWriterPublisher.Api.Models.DTO;
using AIWriterPublisher.Api.Agents.LoraAgent;

namespace AIWriterPublisher.Api.Services;

public interface ILoraOrchestrationService
{
    Dictionary<string, LoraPreset> GetAvailablePresets();
    LoraPreset ResolveLora(string genre, string mode, string customLoraName);
}

public class LoraOrchestrationService : ILoraOrchestrationService
{
    private readonly LoraPredictorAgent _aiAgent;
    private readonly List<LoraDefinition> _allLoras;

    public LoraOrchestrationService(LoraPredictorAgent aiAgent)
    {
        _aiAgent = aiAgent;
        
        // Загружаем манифест из файла один раз при старте
        string path = Path.Combine(AppContext.BaseDirectory, "lora_manifest.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<LoraRootManifest>(json);
            _allLoras = root?.AvailableLoras ?? new List<LoraDefinition>();
        }
        else
        {
            _allLoras = new List<LoraDefinition>(); // фолбэк, если файла нет
        }
    }

    public async Task<List<LoraPreset>> PrepareLorasForGenerationAsync(string currentGenre, TechnicalSpecDto spec, string analysisModel)
    {
        // 1. Фильтруем Лоры для конкретного жанра Кати, чтобы сберечь контекст ИИ
        var filteredLorasForAi = _allLoras.Where(l => 
            l.Category == "Enhancer" || 
            l.ApplicableGenres.Contains("All") || 
            l.ApplicableGenres.Contains(currentGenre, StringComparer.OrdinalIgnoreCase)
        ).ToList();

        // 2. Мапим наши внутренние LoraDefinition в облегченные LoraPreset для ИИ-агента
        var lorasForAgent = filteredLorasForAi.Select(l => new LoraPreset
        {
            DisplayName = l.DisplayName,
            FileName = l.FileName,
            DefaultWeight = l.DefaultWeight
            // Тут можно расширить DTO агента, чтобы он видел еще и l.Description!
        }).ToList();

        // 3. Прыгаем в OpenRouter через агент
        List<LoraPreset> selectedByAi = await _aiAgent.PredictLorasAsync(spec, lorasForAgent, analysisModel);

        // 4. Обогащаем ответ ИИ триггерными словами из нашего мастер-манифеста
        foreach (var selected in selectedByAi)
        {
            var original = _allLoras.FirstOrDefault(x => x.FileName == selected.FileName);
            if (original != null && !string.IsNullOrEmpty(original.TriggerWords))
            {
                // Передаем триггерное слово дальше по конвейеру, чтобы C# вшил его в промпт
                selected.TriggerWords = original.TriggerWords; 
            }
        }

        return selectedByAi;
    }
    // --- Заглушки для старого интерфейса ILoraOrchestrationService ---
        // Если твой старый интерфейс требует эти методы, давай вернем их как фолбэки, чтобы не ломать проект:
    public Dictionary<string, LoraPreset> GetAvailablePresets() 
    {
        return _allLoras.ToDictionary(
            lora => lora.FileName,
            lora => new LoraPreset
            {
                DisplayName = lora.DisplayName,
                FileName = lora.FileName,
                DefaultWeight = lora.DefaultWeight
            }
        );
    }

    public LoraPreset ResolveLora(string genre, string prompt, string mode)
    {
        // Старый синхронный метод. Можешь просто вернуть пустой или дефолтный пресет
        return new LoraPreset();
    }
}