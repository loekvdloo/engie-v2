
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace Engie.Mca.MessageProcessor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessorController : ControllerBase
{
    private readonly ILogger<ProcessorController> _logger;

    public ProcessorController(ILogger<ProcessorController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Message Processor Phase 2: Steps 2A-2E
    /// Classification, priority, queueing
    /// </summary>
    [HttpPost("phase2")]
    public async Task<IActionResult> ProcessPhase2([FromBody] ProcessorRequest request)
    {
        var messageId = request.MessageId;
        _logger.LogInformation("[{MessageId}] === COLUMN 2: MESSAGE PROCESSOR PHASE 2 (Steps 2A-2E) ===", messageId);

        try
        {
            // Step 2A: Classify message type
            _logger.LogInformation("[{MessageId}] ✓ Step 2A: Classificeer berichttype: {MessageType}", messageId, request.MessageType);
            await Task.Delay(10);

            // Step 2B: Determine priority
            _logger.LogInformation("[{MessageId}] ✓ Step 2B: Bepaal prioriteit: Normaal", messageId);
            await Task.Delay(10);

            // Step 2C: Place in queue
            _logger.LogInformation("[{MessageId}] ✓ Step 2C: Plaats in wachtrij: Queue-OK", messageId);
            await Task.Delay(10);

            // Step 2D: No exceptions parked
            _logger.LogInformation("[{MessageId}] ✓ Step 2D: Uitzonderingen geen gepark", messageId);
            await Task.Delay(10);

            // Step 2E: No resending required
            _logger.LogInformation("[{MessageId}] ✓ Step 2E: Geen herversending vereist", messageId);
            await Task.Delay(10);

            return Ok(new
            {
                messageId,
                phase = 2,
                stepsCompleted = 5,
                status = "Phase2Complete",
                nextService = "MessageValidator"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Message processor phase 2 failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Message Processor Phase 4: Steps 4A-4E
    /// ACK/NACK generation, error codes, validation results
    /// </summary>
    [HttpPost("phase4")]
    public async Task<IActionResult> ProcessPhase4([FromBody] ProcessorRequest request)
    {
        var messageId = request.MessageId;
        _logger.LogInformation("[{MessageId}] === COLUMN 2+4: MESSAGE PROCESSOR PHASE 4 (Steps 4A-4E) ===", messageId);

        try
        {
            // Step 4A: Generate ACK/NACK
            var responseType = request.HasErrors ? "NACK" : "ACK";
            _logger.LogInformation("[{MessageId}] ✓ Step 4A: Genereer {ResponseType}-bericht", messageId, responseType);
            await Task.Delay(10);

            // Step 4B: Document errors (if any)
            if (request.HasErrors)
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4B: Voeg foutcodes toe aan NACK", messageId);
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4B: Geen foutcodes toe te voegen", messageId);
            }
            await Task.Delay(10);

            // Step 4C: Add error codes
            _logger.LogInformation("[{MessageId}] ✓ Step 4C: Voeg foutcodes toe", messageId);
            await Task.Delay(10);

            // Step 4D: Register validation result
            _logger.LogInformation("[{MessageId}] ✓ Step 4D: Registreer validatieresultaat", messageId);
            await Task.Delay(10);

            // Step 4E: Configure NACK sending
            _logger.LogInformation("[{MessageId}] ✓ Step 4E: Configureer {ResponseType}-verzending", messageId, responseType);
            await Task.Delay(10);

            return Ok(new
            {
                messageId,
                phase = 4,
                stepsCompleted = 5,
                response = responseType,
                status = "Phase4Complete",
                nextService = "NackHandler"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Message processor phase 4 failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "MessageProcessor", status = "healthy" });
    }
}

public class ProcessorRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public bool HasErrors { get; set; }
    public string? ErrorCode { get; set; }
}
