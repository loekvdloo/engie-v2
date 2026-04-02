
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.OutputHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutputController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Delivered",
        "Failed"
    };

    // 6B — Afleverstatusregister per MessageId (in-memory, idempotent).
    private static readonly ConcurrentDictionary<string, DeliveryRecord> DeliveryRegistry = new();

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
        if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
            return BadRequest(new { step = "6A", error = "MessageId is verplicht" });

        var messageId = request.MessageId.Trim();
        var status = (request.Status ?? string.Empty).Trim();

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", status);

        _logger.LogInformation("[{MessageId}] === COLUMN 6: OUTPUT HANDLER (Steps 6A-6B) ===", messageId);

        try
        {
            // Step 6A: Doorzetten naar raw-layer — check status geldig en bericht niet al afgeleverd.
            if (!AllowedStatuses.Contains(status))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 6A: Ongeldige status: {Status}", messageId, status);
                return BadRequest(new { step = "6A", error = $"Status moet Delivered of Failed zijn (nu: {status})" });
            }

            if (DeliveryRegistry.TryGetValue(messageId, out var existing))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 6A: Bericht al afgeleverd op {DeliveredAt} met status {Status}",
                    messageId, existing.DeliveredAt, existing.Status);
                return StatusCode(409, new
                {
                    step = "6A",
                    status = "AlreadyDelivered",
                    error = "Bericht is al eerder doorgezet naar de raw-layer",
                    previousDelivery = existing
                });
            }

            _logger.LogInformation("[{MessageId}] ✓ Step 6A: Doorzetten naar raw-layer — status: {Status}", messageId, status);
            await Task.Delay(5);

            // Step 6B: Registreer afleverstatus — sla op in register met timestamp.
            var deliveredAt = DateTime.UtcNow;
            var record = new DeliveryRecord(messageId, status, deliveredAt);
            DeliveryRegistry[messageId] = record;

            _logger.LogInformation("[{MessageId}] ✓ Step 6B: Afleverstatus geregistreerd: {Status} op {DeliveredAt:O}",
                messageId, status, deliveredAt);

            // Eindresultaat — dit is het antwoord dat helemaal terug door de keten gaat naar de client
            var errorCodes   = request.ErrorCodes ?? new List<string>();
            var responseType = request.ResponseType ?? (status == "Delivered" ? "Ack" : "Nack");

            return Ok(new
            {
                // Originele envelope-velden
                id                       = request.EnvelopeId,
                type                     = request.EnvelopeType,
                createtime               = request.EnvelopeCreatetime,
                source                   = request.EnvelopeSource,
                msgsender                = request.EnvelopeMsgsender,
                msgsenderrole            = request.EnvelopeMsgsenderrole,
                msgreceiver              = request.EnvelopeMsgreceiver,
                msgreceiverrole          = request.EnvelopeMsgreceiverrole,
                msgtype                  = request.EnvelopeMsgtype,
                msgsubtype               = request.EnvelopeMsgsubtype,
                msgid                    = messageId,
                msgcorrelationid         = request.CorrelationId ?? messageId,
                msgcreationtime          = request.EnvelopeMsgcreationtime,
                msgversion               = request.EnvelopeMsgversion,
                msgpayloadid             = request.EnvelopeMsgpayloadid,
                msgcontenttype           = request.EnvelopeMsgcontenttype,
                entemsendacknowledgement = request.EnvelopeEntemsendacknowledgement,
                entemsendtooutput        = request.EnvelopeEntemsendtooutput,
                entemvalidationresult    = request.EnvelopeEntemvalidationresult,
                entemtimestamp           = request.EnvelopeEntemtimestamp,
                // Verwerkingsresultaat
                messageId,
                correlationId = request.CorrelationId ?? messageId,
                status,
                responseType,
                errorCount = errorCodes.Count,
                errorCodes,
                deliveredAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Output handling failed", messageId);
            return BadRequest(new { step = "unknown", error = ex.Message });
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
    public string? CorrelationId { get; set; }
    public string? ResponseType { get; set; }
    public List<string>? ErrorCodes { get; set; }
    // Volledige envelope-velden
    public string? EnvelopeId { get; set; }
    public string? EnvelopeType { get; set; }
    public string? EnvelopeCreatetime { get; set; }
    public string? EnvelopeSource { get; set; }
    public string? EnvelopeMsgsender { get; set; }
    public string? EnvelopeMsgsenderrole { get; set; }
    public string? EnvelopeMsgreceiver { get; set; }
    public string? EnvelopeMsgreceiverrole { get; set; }
    public string? EnvelopeMsgtype { get; set; }
    public string? EnvelopeMsgsubtype { get; set; }
    public string? EnvelopeMsgcreationtime { get; set; }
    public string? EnvelopeMsgversion { get; set; }
    public string? EnvelopeMsgpayloadid { get; set; }
    public string? EnvelopeMsgcontenttype { get; set; }
    public bool EnvelopeEntemsendacknowledgement { get; set; }
    public bool EnvelopeEntemsendtooutput { get; set; }
    public List<OutputEnvelopeValidationItem>? EnvelopeEntemvalidationresult { get; set; }
    public string? EnvelopeEntemtimestamp { get; set; }
}

public class OutputEnvelopeValidationItem
{
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public record DeliveryRecord(string MessageId, string Status, DateTime DeliveredAt);
