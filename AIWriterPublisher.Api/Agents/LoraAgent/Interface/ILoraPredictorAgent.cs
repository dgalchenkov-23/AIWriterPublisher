using System.Collections.Generic;
using System.Threading.Tasks;
using AIWriterPublisher.Api.Models.DTO; // или где у тебя лежит LoraPreset

namespace AIWriterPublisher.Api.Agents.LoraAgent.Interface
{
    public interface ILoraPredictorAgent
    {
        Task<List<LoraPreset>> PredictLorasAsync(TechnicalSpecDto spec, List<LoraPreset> availableLoras, string analysisModel);
    }
}