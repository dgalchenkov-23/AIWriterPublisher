using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Threading;
using AIWriterPublisher.Api.Services;
using AIWriterPublisher.Api.Models.DTO; // Твой правильный namespace из скрина

namespace AIWriterPublisher.Controllers
{
    [ApiController]
    [Route("api/hero-forge")]
    public class HeroForgeController : ControllerBase
    {
        private readonly HeroCharacterOrchestrator _orchestrator;

        public HeroForgeController(HeroCharacterOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        [HttpPost("generate")]
        public async Task<ActionResult<CharacterGenerationResponse>> Generate(
            [FromBody] CharacterGenerationRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserDescription))
            {
                return BadRequest("Описание персонажа (UserDescription) не может быть пустым.");
            }

            if (request.IsFullBody && string.IsNullOrWhiteSpace(request.FaceReferenceUrl))
            {
                return BadRequest("Для генерации в полный рост (IsFullBody = true) необходим FaceReferenceUrl.");
            }

            try
            {
                var response = await _orchestrator.ProcessGenerationAsync(request, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при обработке запроса агентами: {ex.Message}");
            }
        }
    }
}