namespace AIWriterPublisher.Api.Models.DTO;

    public class LoraPreset
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double DefaultWeight { get; set; } = 1.0;
        public double StrengthModel { get; set; } = 1.0;
        public double StrengthClip { get; set; } = 1.0;
        public string TriggerWords { get; set; } = string.Empty;
        
        // Новое поле для передачи переопределений параметров генерации (Steps, CFG, Sampler, Scheduler)
        public Dictionary<string, object>? Overrides { get; set; } = null;
    }

    // Ответ от ИИ-агента
    public class LoraAgentResponseDto
    {
        // Список выбранных пресетов
        public List<LoraPreset> SelectedLoras { get; set; } = new();
        
        // Краткое обоснование для логов, почему ИИ выбрал именно их
        public string Reasoning { get; set; } = string.Empty;
    }

    // Конфигурация для работы с LoRA
    public class LoraConfig
    {
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string TriggerWords { get; set; }
        public double StrengthModel { get; set; }
        public double StrengthClip { get; set; }
    }

    public class KSamplerSettings
    {
        public int Steps { get; set; }
        public float Cfg { get; set; }
        public string SamplerName { get; set; }
        public string Scheduler { get; set; }
    }