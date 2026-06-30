using System;
using System.Collections.Generic;
using System.Linq;
using AIWriterPublisher.Api.Models.DTO; 

public class ZImageGraphOrchestrator
{
    // Константы известных нам "капризных" моделей
    private const string DISTILL_PATCH = "z-image_turbo_distillpatch_comfyui.safetensors";
    private const string CCD_REALISTIC = "ZIMAGE-CCD-V1.safetensors";
    private const string REALISTIC_SNAPSHOT = "RealisticSnapshot.safetensors"; // условное имя из манифеста
    private const string AESTHETIC_BASE = "Z-Image-Aesthetic_v1.safetensors";
    private const string BETTER_IMAGES = "better_images_loraholic.safetensors";
    private const string SKIN_DETAILS = "skindetails_mild_loraholic.safetensors";

    public OptimizedGraphResult OptimizeGraph(List<LoraConfig> selectedLoras)
    {
    // 1. Инициализируем дефолтный KSampler для Z-Image Turbo
    var samplerSettings = new KSamplerSettings
    {
        Steps = 14,
        Cfg = 1.0f,
        SamplerName = "euler",
        Scheduler = "beta"
    };

    // Если ИИ вернул пустой список, отдаем дефолты и пустой стек
    if (selectedLoras == null || !selectedLoras.Any())
    {
        return new OptimizedGraphResult 
        { 
            SamplerSettings = samplerSettings, 
            PatchedLoras = new List<LoraConfig>() 
        };
    }

    // 2. Определяем техническую доминанту
    bool hasCcd = selectedLoras.Any(l => l.FileName.Equals(CCD_REALISTIC, StringComparison.OrdinalIgnoreCase));
    bool hasSnapshot = selectedLoras.Any(l => l.FileName.Equals(REALISTIC_SNAPSHOT, StringComparison.OrdinalIgnoreCase));
    bool hasDistill = selectedLoras.Any(l => l.FileName.Equals(DISTILL_PATCH, StringComparison.OrdinalIgnoreCase));
    bool hasAesthetic = selectedLoras.Any(l => l.FileName.Equals(AESTHETIC_BASE, StringComparison.OrdinalIgnoreCase));

    // Выставляем KSampler на основе доминанты
    if (hasCcd || hasSnapshot)
    {
        samplerSettings.Steps = 12;
        samplerSettings.Cfg = 1.0f;
        samplerSettings.SamplerName = "euler";
        samplerSettings.Scheduler = "beta";
    }
    else if (hasAesthetic && !hasDistill)
    {
        samplerSettings.Steps = 20;
        samplerSettings.Cfg = 1.5f;
        samplerSettings.SamplerName = "heunpp2";
        samplerSettings.Scheduler = "linear_quadratic";
    }
    else if (hasDistill)
    {
        samplerSettings.Steps = 14;
        samplerSettings.Cfg = 1.0f;
        samplerSettings.SamplerName = "euler";
        samplerSettings.Scheduler = "normal";
    }

    // 3. Корректируем веса (Safe Scaling) ПРЯМО в переданной коллекции
    var betterImagesLora = selectedLoras.FirstOrDefault(l => l.FileName.Equals(BETTER_IMAGES, StringComparison.OrdinalIgnoreCase));
    var skinDetailsLora = selectedLoras.FirstOrDefault(l => l.FileName.Equals(SKIN_DETAILS, StringComparison.OrdinalIgnoreCase));

    if (samplerSettings.SamplerName == "heunpp2")
    {
        // На 20 шагах Heun гасим агрессию слайдеров, чтобы не поплыла анатомия
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
    else if (betterImagesLora != null && skinDetailsLora != null)
    {
        // На быстрых сэмплерах защищаем от кумулятивного взрыва шума при совместном использовании
        if (betterImagesLora.StrengthModel > 2.0f) 
        {
            betterImagesLora.StrengthModel = 2.0f;
            betterImagesLora.StrengthClip = 2.0f;
        }
        if (skinDetailsLora.StrengthModel > 2.0f) 
        {
            skinDetailsLora.StrengthModel = 1.5f;
            skinDetailsLora.StrengthClip = 1.5f;
        }
    }

    // Возвращаем полный пропатченный пакет
    return new OptimizedGraphResult
    {
        SamplerSettings = samplerSettings,
        PatchedLoras = selectedLoras // Веса внутри элементов уже изменены ссылочно
    };
    }
}