using Microsoft.Extensions.Logging;
using AIWriterPublisher.Api.Models.DTO;

namespace AIWriterPublisher.Api.Services;

public interface ILoraOrchestrationService
{
    Dictionary<string, LoraPreset> GetAvailablePresets();
    LoraPreset ResolveLora(string genre, string mode, string customLoraName);
}

public class LoraOrchestrationService : ILoraOrchestrationService
{
    private readonly ILogger<LoraOrchestrationService> _logger;

    // Статическая карта соответствия жанров и реальных LoRA на диске
    private static readonly Dictionary<string, LoraPreset> GenreLoraMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "fantasy", new LoraPreset { DisplayName = "Высокое Фэнтези (Детализация)", FileName = "flux_fantasy_v1.safetensors", DefaultWeight = 0.8 } },
        { "dark_fantasy", new LoraPreset { DisplayName = "Мрачная Готика", FileName = "dark_cinematic_photo.safetensors", DefaultWeight = 0.75 } },
        { "cyberpunk", new LoraPreset { DisplayName = "Киберпанк & Неон", FileName = "flux_neon_cyber.safetensors", DefaultWeight = 0.7 } },
        { "sci_fi", new LoraPreset { DisplayName = "Космическая Одиссея", FileName = "hard_scifi_spaceships.safetensors", DefaultWeight = 0.85 } },
        { "romance", new LoraPreset { DisplayName = "Акварельная Романтика", FileName = "soft_watercolor_mix.safetensors", DefaultWeight = 0.65 } }
    };

    public LoraOrchestrationService(ILogger<LoraOrchestrationService> _logger)
    {
        this._logger = _logger;
    }

    public Dictionary<string, LoraPreset> GetAvailablePresets() => GenreLoraMap;

    public LoraPreset ResolveLora(string genre, string mode, string customLoraName)
    {
        // 1. Ручной режим: если пользователь явно ткнул в пресет стиля
        if (mode == "custom" && !string.IsNullOrEmpty(customLoraName))
        {
            var preset = GenreLoraMap.Values.FirstOrDefault(p => p.DisplayName.Equals(customLoraName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                _logger.LogInformation("Пользователь выбрал ручной стиль: {Style}", preset.DisplayName);
                return preset;
            }
        }

        // 2. Умный режим (Auto): подбираем LoRA на базе метаданных книги (жанра)
        if (GenreLoraMap.TryGetValue(genre, out var autoPreset))
        {
            _logger.LogInformation("Авто-подбор LoRA для жанра [{Genre}]: {LoraFile}", genre, autoPreset.FileName);
            return autoPreset;
        }

        // Fallback: если жанр экзотический, возвращаем пустую заглушку (работаем без LoRA на чистом Flux)
        _logger.LogWarning("Логическая LoRA для жанра [{Genre}] не найдена. Рендерим на чистой базовой модели.", genre);
        return new LoraPreset { DisplayName = "Стандартный", FileName = string.Empty, DefaultWeight = 0.0 };
    }
}