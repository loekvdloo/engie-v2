
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;
namespace Engie.Mca.OutputHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutputController : ControllerBase
{
    private readonly ILogger<OutputController> _logger;

    public OutputController(ILogger<OutputController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Output Handler: Steps 6A-6B
    /// Forward to raw-layer and register delivery status
    /// </summary>
    [HttpPost("finalize")]
    public async Task<IActionResult> FinalizeOutput([FromBody] OutputRequest request)
    {
        var messageId = request.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", request.Status);

        _logger.LogInformation("[{MessageId}] === COLUMN 6: OUTPUT HANDLER (Steps 6A-6B) ===", messageId);

        try
        {
            // Step 6A: Forward to raw-layer
            _logger.LogInformation("[{MessageId}] ✓ Step 6A: Doorzetten naar raw-layer", messageId);
            await Task.Delay(10);

            // Step 6B: Register delivery status
            _logger.LogInformation("[{MessageId}] ✓ Step 6B: Registreer afleverstatus: {Status}", messageId, request.Status);
            await Task.Delay(10);

            return Ok(new
            {
                messageId,
                status = request.Status,
                stepsCompleted = 2,
                finalStatus = "Delivered",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Output handling failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "OutputHandler", status = "healthy" });
    }
}

public class OutputRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string Status { get; set; } = "Delivered";
}
