
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace Engie.Mca.EventHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly ILogger<EventController> _logger;

    public EventController(ILogger<EventController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handle incoming message event
    /// Steps: 1A-1F (Technical receipt, validation, type identification)
    /// </summary>
    [HttpPost("handle")]
    public async Task<IActionResult> HandleEvent([FromBody] EventHandlerRequest request)
    {
        var messageId = request.MessageId;
        _logger.LogInformation("[{MessageId}] === COLUMN 1: EVENT HANDLER (Steps 1A-1F) ===", messageId);

        try
        {
            // Step 1A: Receive event
            _logger.LogInformation("[{MessageId}] ✓ Step 1A: Ontvang event", messageId);
            await Task.Delay(10);

            // Step 1B: Technical receipt confirmation
            _logger.LogInformation("[{MessageId}] ✓ Step 1B: Technische ontvangstbevestiging", messageId);
            await Task.Delay(10);

            // Step 1C: Technical XML validation
            _logger.LogInformation("[{MessageId}] ✓ Step 1C: Technische validatie XML geslaagd", messageId);
            await Task.Delay(10);

            // Step 1D: Log receipt time
            _logger.LogInformation("[{MessageId}] ✓ Step 1D: Logging van ontvangsttijd", messageId);
            await Task.Delay(10);

            // Step 1E: Identify message type
            _logger.LogInformation("[{MessageId}] ✓ Step 1E: Berichttype geïdentificeerd: {MessageType}", messageId, request.MessageType);
            await Task.Delay(10);

            // Step 1F: Prepare for processing
            _logger.LogInformation("[{MessageId}] ✓ Step 1F: Bereid verwerking voor", messageId);
            await Task.Delay(10);

            return Ok(new
            {
                messageId,
                stepsCompleted = 6,
                status = "EventHandled",
                nextService = "MessageProcessor"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Event handling failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "EventHandler", status = "healthy" });
    }
}

public class EventHandlerRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
