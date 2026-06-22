using System;
using System.Collections.Generic;
using System.Linq;

public class LoraConfig
{
    public string DisplayName { get; set; }
    public string FileName { get; set; }
    public float StrengthModel { get; set; }
    public float StrengthClip { get; set; }
}

public class KSamplerSettings
{
    public int Steps { get; set; }
    public float Cfg { get; set; }
    public string SamplerName { get; set; }
    public string Scheduler { get; set; }
}

public class ZImageGraphOrchestrator
{
    // Константы известных нам "капризных" моделей
    private const string DISTILL_PATCH = "z-image_turbo_distillpatch_comfyui.safetensors";
    private const string CCD_REALISTIC = "ZIMAGE-CCD-V1.safetensors";
    private const string REALISTIC_SNAPSHOT = "RealisticSnapshot.safetensors"; // условное имя из манифеста
    private const string AESTHETIC_BASE = "z-image-aestheticbase-and-turbo.safetensors";
    private const string BETTER_IMAGES = "better_images_loraholic.safetensors";
    private const string SKIN_DETAILS = "skindetails_mild_loraholic.safetensors";

    public KSamplerSettings OptimizeGraph(List<LoraConfig> selectedLoras)
    {
        // Дефолтные безопасные настройки для Z-Image Turbo
        var settings = new KSamplerSettings
        {
            Steps = 14,
            Cfg = 1.0f,
            SamplerName = "euler",
            Scheduler = "beta"
        };

        if (selectedLoras == null || !selectedLoras.Any())
            return settings;

        // -----------------------------------------------------------------
        // ШАГ 1: ОПРЕДЕЛЕНИЕ ТЕХНИЧЕСКОЙ ДОМИНАНТЫ И НАСТРОЙКА СЕМПЛЕРА
        // -----------------------------------------------------------------
        
        bool hasCcd = selectedLoras.Any(l => l.FileName.Equals(CCD_REALISTIC, StringComparison.OrdinalIgnoreCase));
        bool hasSnapshot = selectedLoras.Any(l => l.FileName.Equals(REALISTIC_SNAPSHOT, StringComparison.OrdinalIgnoreCase));
        bool hasDistill = selectedLoras.Any(l => l.FileName.Equals(DISTILL_PATCH, StringComparison.OrdinalIgnoreCase));
        bool hasAesthetic = selectedLoras.Any(l => l.FileName.Equals(AESTHETIC_BASE, StringComparison.OrdinalIgnoreCase));

        // Жесткое правило: Если есть CCD или Snapshot (Реализм) -> Только Euler! 
        // Даже если ИИ закинул туда Эстетику, реализм приоритетнее по сэмплеру.
        if (hasCcd || hasSnapshot)
        {
            settings.Steps = 12; // Быстрый, точный проход для кожи
            settings.Cfg = 1.0f;
            settings.SamplerName = "euler";
            settings.Scheduler = "beta";
        }
        // Если реализма нет, но включена Эстетика без Дистиллятора -> включаем "Журнальный режим"
        else if (hasAesthetic && !hasDistill)
        {
            settings.Steps = 20; // Heun требует больше шагов для схождения
            settings.Cfg = 1.5f;
            settings.SamplerName = "heunpp2";
            settings.Scheduler = "linear_quadratic";
        }
        // Если вшит Дистиллятор патч -> Heun запрещен математически, откатываемся на Turbo-настройки
        else if (hasDistill)
        {
            settings.Steps = 14;
            settings.Cfg = 1.0f;
            settings.SamplerName = "euler";
            settings.Scheduler = "normal";
        }

        // -----------------------------------------------------------------
        // ШАГ 2: КОРРЕКЦИЯ ВЕСОВ (SAFE SCALING) ПОД ВЫБРАННЫЙ РЕЖИМ
        // -----------------------------------------------------------------
        
        // Находим агрессивные слайдеры в стеке, если они есть
        var betterImagesLora = selectedLoras.FirstOrDefault(l => l.FileName.Equals(BETTER_IMAGES, StringComparison.OrdinalIgnoreCase));
        var skinDetailsLora = selectedLoras.FirstOrDefault(l => l.FileName.Equals(SKIN_DETAILS, StringComparison.OrdinalIgnoreCase));

        // Ситуация А: Тяжелый сэмплер (Heunpp2 / 20 шагов)
        if (settings.SamplerName == "heunpp2")
        {
            // На 20 шагах слайдеры становятся токсичными. Принудительно душим их веса в 2 раза.
            if (betterImagesLora != null && betterImagesLora.StrengthModel > 1.5f)
            {
                betterImagesLora.StrengthModel = 1.2f;
                betterImagesLora.StrengthClip = 1.2f;
            }
            if (skinDetailsLora != null && skinDetailsLora.StrengthModel > 1.5f)
            {
                skinDetailsLora.StrengthModel = 1.0f;
                skinDetailsLora.StrengthClip = 1.0f;
            }
        }
        // Ситуация Б: Легкий сэмплер, но ИИ влепил ОБА слайдера одновременно
        else if (betterImagesLora != null && skinDetailsLora != null)
        {
            // Защита от кумулятивного взрыва анатомии. Срезаем до безопасного лимита.
            if (betterImagesLora.StrengthModel > 2.0f) 
                betterImagesLora.StrengthModel = 2.0f;
                betterImagesLora.StrengthClip = 2.0f;

            if (skinDetailsLora.StrengthModel > 2.0f) 
                skinDetailsLora.StrengthModel = 1.5f;
                skinDetailsLora.StrengthClip = 1.5f;
        }

        return settings;
    }
}