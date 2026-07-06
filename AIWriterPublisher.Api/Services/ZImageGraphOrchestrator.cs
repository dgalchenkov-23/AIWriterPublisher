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
        // 1. Убираем конфликты (прямо в переданной коллекции)
        RemoveConflicts(selectedLoras);

        // 2. Нормализуем веса (прямо в переданной коллекции)
        NormalizeWeights(selectedLoras);

        // 3. Выбираем настройки на основе финального стека
        var samplerSettings = GetSamplerSettings(selectedLoras);

        // 4. Возвращаем результат (контракт НЕ МЕНЯЕТСЯ)
        return new OptimizedGraphResult
        {
            SamplerSettings = samplerSettings,
            PatchedLoras = selectedLoras
        };
    }

    // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

    private void RemoveConflicts(List<LoraConfig> loras)
    {
        bool hasAesthetic = loras.Any(l => l.FileName == AESTHETIC_BASE);
        bool hasBetterImages = loras.Any(l => l.FileName == BETTER_IMAGES);
        bool hasCcd = loras.Any(l => l.FileName == CCD_REALISTIC);
        bool hasSnapshot = loras.Any(l => l.FileName == REALISTIC_SNAPSHOT);

        // Если есть Aesthetic, убираем Better Images (конфликт)
        if (hasAesthetic && hasBetterImages)
        {
            loras.RemoveAll(l => l.FileName == BETTER_IMAGES);
        }

        // Если есть CCD и Snapshot вместе — оставляем только Snapshot
        if (hasCcd && hasSnapshot)
        {
            loras.RemoveAll(l => l.FileName == CCD_REALISTIC);
        }
    }

    private void NormalizeWeights(List<LoraConfig> loras)
    {
        foreach (var lora in loras)
        {
            float maxWeight = GetMaxWeight(lora.FileName);
            float safeWeight = GetSafeWeight(lora.FileName); // НОВОЕ!

            // Если вес превышает безопасный — обрезаем до безопасного
            if (lora.StrengthModel > safeWeight)
            {
                lora.StrengthModel = safeWeight;
                lora.StrengthClip = safeWeight;
            }

            // Если вес всё ещё превышает абсолютный потолок — обрезаем до потолка
            if (lora.StrengthModel > maxWeight)
            {
                lora.StrengthModel = maxWeight;
                lora.StrengthClip = maxWeight;
            }
        }
    }

    private float GetSafeWeight(string fileName)
    {
        // БЕЗОПАСНЫЕ РАБОЧИЕ ВЕСА (не потолок!)
        if (fileName.Contains("better_images_loraholic")) return 4.0f;  // рабочий диапазон 2-5
        if (fileName.Contains("detail_slider_Z_V2")) return 1.5f;      // рабочий диапазон 0.5-1.5
        if (fileName.Contains("skindetails_mild_loraholic")) return 3.0f; // рабочий диапазон 1-4
        if (fileName.Contains("midj-z-1")) return 0.8f;                // рабочий диапазон 0.5-0.8
        if (fileName.Contains("RealisticSnapshot")) return 0.7f;       // рабочий диапазон 0.5-0.7
        if (fileName.Contains("Z-Image-Aesthetic")) return 1.0f;       // строго 1.0
        if (fileName.Contains("distillpatch")) return 1.0f;            // строго 1.0
        return 5.0f; // дефолт для остальных
    }

    private float GetMaxWeight(string fileName)
    {
        // АБСОЛЮТНЫЙ ПОТОЛОК (за который нельзя заходить НИКОГДА)
        if (fileName.Contains("better_images_loraholic")) return 9.0f;
        if (fileName.Contains("detail_slider_Z_V2")) return 2.0f;
        if (fileName.Contains("skindetails_mild_loraholic")) return 8.0f;
        if (fileName.Contains("midj-z-1")) return 1.0f;
        if (fileName.Contains("RealisticSnapshot")) return 1.0f;
        if (fileName.Contains("Z-Image-Aesthetic")) return 1.2f;
        if (fileName.Contains("distillpatch")) return 1.0f;
        return 9.0f;
    }

    private KSamplerSettings GetSamplerSettings(List<LoraConfig> loras)
    {
        bool hasAesthetic = loras.Any(l => l.FileName == AESTHETIC_BASE);
        bool hasRealistic = loras.Any(l => l.FileName == REALISTIC_SNAPSHOT || l.FileName == CCD_REALISTIC);
        bool hasBetterImages = loras.Any(l => l.FileName == BETTER_IMAGES);

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

        // ПРАВИЛО 2: Реалистичные без Aesthetic → euler
        if (hasRealistic && !hasAesthetic)
        {
            return new KSamplerSettings
            {
                Steps = 14,
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

        // ПРАВИЛО 4: Better Images и ничего больше → euler
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