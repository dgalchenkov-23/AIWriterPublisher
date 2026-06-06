namespace AIWriterPublisher.Api.Models.DTO;

public class LoraPreset 
{
    public string DisplayName { get; set; }
    public string FileName { get; set; }
    public double DefaultWeight { get; set; }
    public double StrengthModel { get; set; } = 1.0;
    public double StrengthClip { get; set; } = 1.0;
}