
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.MessageProcessor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessorController : ControllerBase
{
    // 2C — Slagboom: max 5 berichten worden gelijktijdig verwerkt; overige wachten
    private static readonly SemaphoreSlim _gate = new SemaphoreSlim(5, 5);

    // 2D — Parkeerdrempel: telt berichten die wachten + actief verwerkt worden
    private static int _pendingCount = 0;
    private const int ParkThreshold = 15;

    private static readonly HashSet<string> KnownMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllocationSeries",
        "AllocationFactorSeries",
        "AggregatedAllocationSeries"
    };

    private readonly ILogger<ProcessorController> _logger;

    public ProcessorController(ILogger<ProcessorController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Message Processor Phase 2: Steps 2A-2E
    /// Classification, priority, queueing, parking check, resend check
    /// </summary>
    [HttpPost("phase2")]
    public async Task<IActionResult> ProcessPhase2([FromBody] ProcessorRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
            return BadRequest(new { step = "2A", error = "MessageId is verplicht" });

        var messageId = request.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var messageTypeScope = LogContext.PushProperty("MessageType", request.MessageType);

        _logger.LogInformation("[{MessageId}] === COLUMN 2: MESSAGE PROCESSOR PHASE 2 (Steps 2A-2E) ===", messageId);

        // Registreer bericht als 'in de pijp' voor de parkeerdrempel-check (2D)
        Interlocked.Increment(ref _pendingCount);
        bool gateEntered = false;

        try
        {
            // Step 2A: Classificeer berichttype
            if (string.IsNullOrWhiteSpace(request.MessageType) || !KnownMessageTypes.Contains(request.MessageType))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 2A: Onbekend berichttype: {MessageType}", messageId, request.MessageType);
                return BadRequest(new { step = "2A", error = $"Onbekend berichttype: {request.MessageType}" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 2A: Berichttype geclassificeerd: {MessageType}", messageId, request.MessageType);

            // Step 2B: Bepaal prioriteit — altijd Normaal
            const string priority = "Normaal";
            _logger.LogInformation("[{MessageId}] ✓ Step 2B: Prioriteit bepaald: {Priority}", messageId, priority);

            // Step 2C: Slagboom — wacht op een vrij verwerkingsslot (max 5 seconden)
            _logger.LogInformation("[{MessageId}] Step 2C: Wachtrij — wacht op slot (bezet: {InUse}/5, totaal in pijp: {Pending})",
                messageId, 5 - _gate.CurrentCount, _pendingCount);

            gateEntered = await _gate.WaitAsync(TimeSpan.FromSeconds(5));
            if (!gateEntered)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 2C: Slagboom timeout — alle slots bezet na 5 seconden wachten", messageId);
                return StatusCode(503, new { step = "2C", error = "Verwerkingswachtrij vol, probeer later opnieuw" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 2C: Slot verkregen — bericht staat in de wachtrij (bezet: {InUse}/5)",
                messageId, 5 - _gate.CurrentCount + 1);

            // Step 2D: Parkeercheck — te veel berichten actief in het systeem?
            if (_pendingCount > ParkThreshold)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 2D: Parkeerdrempel overschreden ({Pending}/{Threshold}) — bericht geparkeerd",
                    messageId, _pendingCount, ParkThreshold);
                return StatusCode(429, new
                {
                    step = "2D",
                    status = "Parked",
                    error = $"Systeem overbelast ({_pendingCount} berichten actief) — bericht geparkeerd voor latere verwerking"
                });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 2D: Geen parkering nodig ({Pending}/{Threshold} berichten actief)",
                messageId, _pendingCount, ParkThreshold);

            // Step 2E: Controleer op herversending via MessageId-patroon
            bool isResend = messageId.EndsWith("-retry", StringComparison.OrdinalIgnoreCase)
                         || messageId.Contains("-resend-", StringComparison.OrdinalIgnoreCase);
            if (isResend)
                _logger.LogWarning("[{MessageId}] ✓ Step 2E: Herversending gedetecteerd — wordt alsnog verwerkt", messageId);
            else
                _logger.LogInformation("[{MessageId}] ✓ Step 2E: Geen herversending vereist", messageId);

            return Ok(new
            {
                messageId,
                phase = 2,
                stepsCompleted = 5,
                priority,
                isResend,
                activeMessages = _pendingCount,
                status = "Phase2Complete",
                nextService = "MessageValidator"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Message processor phase 2 failed", messageId);
            return BadRequest(new { step = "unknown", error = ex.Message });
        }
        finally
        {
            if (gateEntered) _gate.Release();
            Interlocked.Decrement(ref _pendingCount);
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

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", request.HasErrors ? "NACK" : "ACK");
        using var errorCodesScope = LogContext.PushProperty("ErrorCodes", request.ErrorCodes == null ? string.Empty : string.Join(",", request.ErrorCodes));

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
    public List<string>? ErrorCodes { get; set; }
}
