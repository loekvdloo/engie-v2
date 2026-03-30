
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.NackHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NackController : ControllerBase
{
    private static readonly HashSet<string> AllowedResponses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACK",
        "NACK"
    };

    // Tracks sent responses for idempotent independent delivery checks (5D).
    private static readonly ConcurrentDictionary<string, DateTime> SentResponseRegistry = new();

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
        if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
            return BadRequest(new { step = "5A", error = "MessageId is verplicht" });

        var messageId = request.MessageId.Trim();
        var response = (request.Response ?? string.Empty).Trim().ToUpperInvariant();

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", response);

        _logger.LogInformation("[{MessageId}] === COLUMN 5: N-ACK HANDLER (Steps 5A-5D) ===", messageId);

        try
        {
            // Step 5A: Send ACK/NACK - valideer type.
            if (!AllowedResponses.Contains(response))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 5A: Ongeldig responstype: {Response}", messageId, request.Response);
                return BadRequest(new { step = "5A", error = $"Response moet ACK of NACK zijn (nu: {request.Response})" });
            }

            _logger.LogInformation("[{MessageId}] ✓ Step 5A: Verstuur {Response}-bericht", messageId, response);

            // Step 5B: Configured response - check routingkanaal.
            var route = response == "NACK" ? "NegativeFlow" : "PositiveFlow";
            if (string.IsNullOrWhiteSpace(route))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 5B: Geen route geconfigureerd voor {Response}", messageId, response);
                return StatusCode(500, new { step = "5B", error = "Geen verzendroute geconfigureerd" });
            }

            _logger.LogInformation("[{MessageId}] ✓ Step 5B: Geconfigureerde respons via {Route}", messageId, route);

            // Step 5C: Log send time - leg echte verzendtijd vast.
            var sentAt = DateTime.UtcNow;
            _logger.LogInformation("[{MessageId}] ✓ Step 5C: Verzending gelogd op {SentAt:O}", messageId, sentAt);

            // Step 5D: Independent delivery - idempotent en onafhankelijk.
            var deliveryKey = $"{messageId}:{response}";
            if (!SentResponseRegistry.TryAdd(deliveryKey, sentAt))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 5D: Dubbele verzending geblokkeerd voor key {DeliveryKey}", messageId, deliveryKey);
                return StatusCode(409, new
                {
                    step = "5D",
                    status = "DuplicateBlocked",
                    error = "Response is al verstuurd voor dit bericht"
                });
            }

            _logger.LogInformation("[{MessageId}] ✓ Step 5D: Zelfstandige verzending geregistreerd", messageId);

            return Ok(new
            {
                messageId,
                response,
                stepsCompleted = 4,
                route,
                sentAt,
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
