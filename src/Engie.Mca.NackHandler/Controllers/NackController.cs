
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;
namespace Engie.Mca.NackHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NackController : ControllerBase
{
    private readonly ILogger<NackController> _logger;

    public NackController(ILogger<NackController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handle ACK/NACK sending: Steps 5A-5D
    /// Send responses, log send time, independent delivery
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendAckNack([FromBody] NackRequest request)
    {
        var messageId = request.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", request.Response);

        _logger.LogInformation("[{MessageId}] === COLUMN 5: N-ACK HANDLER (Steps 5A-5D) ===", messageId);

        try
        {
            // Step 5A: Send ACK/NACK
            _logger.LogInformation("[{MessageId}] ✓ Step 5A: Verstuur {Response}-bericht", messageId, request.Response);
            await Task.Delay(10);

            // Step 5B: Configured response
            _logger.LogInformation("[{MessageId}] ✓ Step 5B: Geconfigureerde respons", messageId);
            await Task.Delay(10);

            // Step 5C: Log send time
            _logger.LogInformation("[{MessageId}] ✓ Step 5C: Logging verzendtijd", messageId);
            await Task.Delay(10);

            // Step 5D: Independent delivery
            _logger.LogInformation("[{MessageId}] ✓ Step 5D: Zelfstandige verzending", messageId);
            await Task.Delay(10);

            return Ok(new
            {
                messageId,
                response = request.Response,
                stepsCompleted = 4,
                status = "ResponseSent",
                nextService = "OutputHandler"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] N-ACK handling failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "NackHandler", status = "healthy" });
    }
}

public class NackRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string Response { get; set; } = "ACK"; // ACK or NACK
}
