using System;
using System.Collections.Generic;
using System.Linq;
using AIWriterPublisher.Api.Models.DTO; 

public class ZImageGraphOrchestrator
{
    private const string DISTILL_PATCH = "z-image_turbo_distillpatch_comfyui.safetensors";
    private const string AESTHETIC_BASE = "Z-Image-Aesthetic_v1.safetensors";
    private const string REALISTIC_SNAPSHOT = "RealisticSnapshot-Zimage-Turbov5.safetensors";
    private const string CCD_REALISTIC = "ZIMAGE-CCD-V1.safetensors";
    private const string BETTER_IMAGES = "better_images_loraholic.safetensors";
    private const string SKIN_DETAILS = "skindetails_mild_loraholic.safetensors";

    public OptimizedGraphResult OptimizeGraph(List<LoraConfig> selectedLoras)
    {
        // Делаем глубокую/поверхностную копию, чтобы не срать в исходную коллекцию бизнес-логики
        var lorasPipeline = selectedLoras.Select(l => new LoraConfig 
        { 
            DisplayName = l.DisplayName,
            FileName = l.FileName,
            StrengthModel = l.StrengthModel,
            StrengthClip = l.StrengthClip,
            TriggerWords = l.TriggerWords
        }).ToList();

        // 1. Убираем конфликты
        RemoveConflicts(lorasPipeline);

        // 2. Нормализуем веса
        NormalizeWeights(lorasPipeline);

        // 3. Теперь правила сэмплера отработают корректно на чистом стеке
        var samplerSettings = GetSamplerSettings(lorasPipeline);
    Console.WriteLine($"lorasPipeline: {string.Join(", ", lorasPipeline.Select(l => $"{l.FileName} (weight: {l.StrengthModel})"))}");
        return new OptimizedGraphResult
        {
            SamplerSettings = samplerSettings,
            PatchedLoras = lorasPipeline
        };
    }

    private void RemoveConflicts(List<LoraConfig> loras)
    {
        bool hasAesthetic = loras.Any(l => l.FileName.Contains("Z-Image-Aesthetic"));
        bool hasBetterImages = loras.Any(l => l.FileName.Contains("better_images_loraholic"));
        bool hasCcd = loras.Any(l => l.FileName.Contains("ZIMAGE-CCD"));
        bool hasSnapshot = loras.Any(l => l.FileName.Contains("RealisticSnapshot"));

        if (hasAesthetic && hasBetterImages)
        {
            loras.RemoveAll(l => l.FileName.Contains("better_images_loraholic"));
        }

        if (hasCcd && hasSnapshot)
        {
            loras.RemoveAll(l => l.FileName.Contains("ZIMAGE-CCD"));
        }
    }

    private void NormalizeWeights(List<LoraConfig> loras)
    {
        foreach (var lora in loras)
        {
            float? maxWeight = GetMaxWeight(lora.FileName);
            float? safeWeight = GetSafeWeight(lora.FileName);

            // Если модель неизвестна бэкенду — НЕ ТРОГАЕМ её вес, доверяем агенту!
            // Но если лимиты определены — жестко их соблюдаем.
            if (safeWeight.HasValue && lora.StrengthModel > safeWeight.Value)
            {
                lora.StrengthModel = safeWeight.Value;
                lora.StrengthClip = safeWeight.Value;
            }

            if (maxWeight.HasValue && lora.StrengthModel > maxWeight.Value)
            {
                lora.StrengthModel = maxWeight.Value;
                lora.StrengthClip = maxWeight.Value;
            }
        }
    }

    private float? GetSafeWeight(string fileName)
    {
        if (fileName.Contains("better_images_loraholic")) return 4.0f;  
        if (fileName.Contains("detail_slider_Z_V2")) return 1.5f;      
        if (fileName.Contains("skindetails_mild_loraholic")) return 3.0f; 
        if (fileName.Contains("midj-z-1")) return 0.8f;                
        if (fileName.Contains("RealisticSnapshot")) return 0.7f;       
        if (fileName.Contains("Z-Image-Aesthetic")) return 1.0f;       
        if (fileName.Contains("distillpatch")) return 1.0f;   
        
        return null; // Для новых LoRA возвращаем null, чтобы использовать вес от ИИ-агента
    }

    private float? GetMaxWeight(string fileName)
    {
        if (fileName.Contains("better_images_loraholic")) return 9.0f;
        if (fileName.Contains("detail_slider_Z_V2")) return 2.0f;
        if (fileName.Contains("skindetails_mild_loraholic")) return 8.0f;
        if (fileName.Contains("midj-z-1")) return 1.0f;
        if (fileName.Contains("RealisticSnapshot")) return 1.0f;
        if (fileName.Contains("Z-Image-Aesthetic")) return 1.2f;
        if (fileName.Contains("distillpatch")) return 1.0f;
        
        return null; 
    }

    private KSamplerSettings GetSamplerSettings(List<LoraConfig> loras)
    {
        bool hasAesthetic = loras.Any(l => l.FileName.Contains("Z-Image-Aesthetic"));
        bool hasRealistic = loras.Any(l => l.FileName.Contains("RealisticSnapshot") || l.FileName.Contains("ZIMAGE-CCD") || l.FileName.Contains("Jibs_Realistic"));
        bool hasBetterImages = loras.Any(l => l.FileName.Contains("better_images_loraholic"));

        // ПРАВИЛО 1: Aesthetic без реалистичных → heunpp2
        if (hasAesthetic && !hasRealistic)
        {
            return new KSamplerSettings
            {
                Steps = 20,
                Cfg = 1.5f,
                SamplerName = "heunpp2",
                Scheduler = "linear_quadratic"
            };
        }

        // ПРАВИЛО 2: Реалистичные без Aesthetic → euler (Наш текущий случай)
        if (hasRealistic && !hasAesthetic)
        {
            return new KSamplerSettings
            {
                Steps = 18, // Поднял с 14 до стабильных 18 под наш сетап
                Cfg = 1.0f,
                SamplerName = "euler",
                Scheduler = "simple"
            };
        }

        // ПРАВИЛО 3: И Aesthetic, и реалистичные → компромисс
        if (hasAesthetic && hasRealistic)
        {
            return new KSamplerSettings
            {
                Steps = 16,
                Cfg = 1.2f,
                SamplerName = "euler",
                Scheduler = "simple"
            };
        }

        // ПРАВИЛО 4: Better Images и ничего больше
        if (hasBetterImages)
        {
            return new KSamplerSettings
            {
                Steps = 14,
                Cfg = 1.0f,
                SamplerName = "euler",
                Scheduler = "simple"
            };
        }

        // ПРАВИЛО 5: Дефолт для ZIT
        return new KSamplerSettings
        {
            Steps = 14,
            Cfg = 1.0f,
            SamplerName = "euler",
            Scheduler = "simple"
        };
    }
}