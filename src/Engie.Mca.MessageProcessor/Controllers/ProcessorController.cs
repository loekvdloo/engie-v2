
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System.Net.Http;
using System.Net.Http.Json;

namespace Engie.Mca.MessageProcessor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessorController : ControllerBase
{
    private static readonly string MessageValidatorBaseUrl =
        Environment.GetEnvironmentVariable("MESSAGE_VALIDATOR_BASE_URL")
        ?? "http://engie-mca-message-validator:8080";

    private static readonly string NackHandlerBaseUrl =
        Environment.GetEnvironmentVariable("NACK_HANDLER_BASE_URL")
        ?? "http://engie-mca-nack-handler:8080";

    // 2C — Slagboom: max 5 berichten worden gelijktijdig verwerkt; overige wachten
    private static readonly SemaphoreSlim _gate = new SemaphoreSlim(5, 5);

    // 2D — Parkeerdrempel: telt berichten die wachten + actief verwerkt worden
    private static int _pendingCount = 0;
    private const int ParkThreshold = 15;

    private static readonly HashSet<string> KnownMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllocationSeries",
        "AllocationFactorSeries",
        "AggregatedAllocationSeries",
        "AllocationServiceNotification"
    };

    // 4D — Registratie van validatieresultaat per bericht.
    private static readonly Dictionary<string, object> ValidationResultRegistry = new();
    private static readonly object RegistryLock = new();

    private readonly ILogger<ProcessorController> _logger;

    private readonly IHttpClientFactory _httpClientFactory;

    public ProcessorController(ILogger<ProcessorController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

            // Doorgeven aan MessageValidator (volgende in de keten)
            using var valReq = new HttpRequestMessage(HttpMethod.Post, $"{MessageValidatorBaseUrl}/api/validator/validate");
            valReq.Content = JsonContent.Create(new
            {
                MessageId     = messageId,
                CorrelationId = request.CorrelationId,
                MessageType   = request.MessageType,
                EanCode       = request.EanCode ?? string.Empty,
                DocumentId    = request.DocumentId,
                Quantity      = request.Quantity,
                StartDateTime = request.StartDateTime,
                EndDateTime   = request.EndDateTime,
                Content       = request.XmlContent,
                // Envelope doorsturen
                EnvelopeId                         = request.EnvelopeId,
                EnvelopeType                       = request.EnvelopeType,
                EnvelopeCreatetime                 = request.EnvelopeCreatetime,
                EnvelopeSource                     = request.EnvelopeSource,
                EnvelopeMsgsender                  = request.EnvelopeMsgsender,
                EnvelopeMsgsenderrole              = request.EnvelopeMsgsenderrole,
                EnvelopeMsgreceiver                = request.EnvelopeMsgreceiver,
                EnvelopeMsgreceiverrole            = request.EnvelopeMsgreceiverrole,
                EnvelopeMsgsubtype                 = request.EnvelopeMsgsubtype,
                EnvelopeMsgcreationtime            = request.EnvelopeMsgcreationtime,
                EnvelopeMsgversion                 = request.EnvelopeMsgversion,
                EnvelopeMsgpayloadid               = request.EnvelopeMsgpayloadid,
                EnvelopeMsgcontenttype             = request.EnvelopeMsgcontenttype,
                EnvelopeEntemsendacknowledgement   = request.EnvelopeEntemsendacknowledgement,
                EnvelopeEntemsendtooutput           = request.EnvelopeEntemsendtooutput,
                EnvelopeEntemvalidationresult       = request.EnvelopeEntemvalidationresult,
                EnvelopeEntemtimestamp              = request.EnvelopeEntemtimestamp
            });
            valReq.Headers.Add("X-Correlation-ID", request.CorrelationId ?? messageId);
            _logger.LogInformation("[{MessageId}] → Doorgeven aan MessageValidator", messageId);
            var valResp = await _httpClientFactory.CreateClient().SendAsync(valReq, HttpContext.RequestAborted);
            valResp.EnsureSuccessStatusCode();
            return Content(await valResp.Content.ReadAsStringAsync(HttpContext.RequestAborted), "application/json");
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
        if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
            return BadRequest(new { step = "4A", error = "MessageId is verplicht" });

        var messageId = request.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
        using var responseTypeScope = LogContext.PushProperty("ResponseType", request.HasErrors ? "NACK" : "ACK");
        using var errorCodesScope = LogContext.PushProperty("ErrorCodes", request.ErrorCodes == null ? string.Empty : string.Join(",", request.ErrorCodes));

        _logger.LogInformation("[{MessageId}] === COLUMN 2+4: MESSAGE PROCESSOR PHASE 4 (Steps 4A-4E) ===", messageId);

        try
        {
            // Step 4A: Generate ACK/NACK op basis van validatieresultaat
            var responseType = request.HasErrors ? "NACK" : "ACK";
            _logger.LogInformation("[{MessageId}] ✓ Step 4A: Genereer {ResponseType}-bericht", messageId, responseType);

            // Step 4B: Documenteer fouten en check input-consistentie
            var normalizedCodes = (request.ErrorCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (request.HasErrors && normalizedCodes.Count == 0 && !string.IsNullOrWhiteSpace(request.ErrorCode))
            {
                normalizedCodes.Add(request.ErrorCode.Trim());
            }

            if (request.HasErrors && normalizedCodes.Count == 0)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 4B: HasErrors=true maar geen foutcodes aangeleverd", messageId);
                return BadRequest(new { step = "4B", error = "HasErrors=true vereist minimaal 1 foutcode" });
            }

            if (!request.HasErrors && normalizedCodes.Count > 0)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 4B: HasErrors=false maar foutcodes wel aanwezig", messageId);
                return BadRequest(new { step = "4B", error = "HasErrors=false mag geen foutcodes bevatten" });
            }

            if (request.HasErrors)
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4B: Fouten gedocumenteerd voor NACK", messageId);
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4B: Geen foutcodes toe te voegen", messageId);
            }

            // Step 4C: Valideer en voeg foutcodes toe
            foreach (var code in normalizedCodes)
            {
                if (code.Length != 3 || !code.All(char.IsDigit))
                {
                    _logger.LogWarning("[{MessageId}] ✗ Step 4C: Ongeldige foutcode-indeling: {Code}", messageId, code);
                    return BadRequest(new { step = "4C", error = $"Ongeldige foutcode-indeling: {code}" });
                }
            }

            if (normalizedCodes.Count > 0)
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4C: Foutcodes toegevoegd: {ErrorCodes}", messageId, string.Join(",", normalizedCodes));
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 4C: Geen foutcodes voor ACK", messageId);
            }

            // Step 4D: Registreer validatieresultaat in geheugen
            var registration = new
            {
                MessageId = messageId,
                HasErrors = request.HasErrors,
                ResponseType = responseType,
                ErrorCodes = normalizedCodes,
                RegisteredAt = DateTime.UtcNow
            };

            lock (RegistryLock)
            {
                ValidationResultRegistry[messageId] = registration;
            }

            _logger.LogInformation("[{MessageId}] ✓ Step 4D: Validatieresultaat geregistreerd", messageId);

            // Step 4E: Configureer response-verzending naar volgend kanaal
            var targetChannel = responseType == "NACK" ? "NegativeFlow" : "PositiveFlow";
            _logger.LogInformation("[{MessageId}] ✓ Step 4E: Configureer {ResponseType}-verzending via {TargetChannel}", messageId, responseType, targetChannel);

            // Doorgeven aan NackHandler (volgende in de keten)
            using var nackReq = new HttpRequestMessage(HttpMethod.Post, $"{NackHandlerBaseUrl}/api/nack/send");
            nackReq.Content = JsonContent.Create(new
            {
                MessageId     = messageId,
                CorrelationId = request.CorrelationId,
                Response      = responseType,
                ErrorCodes    = normalizedCodes,
                // Envelope doorsturen
                EnvelopeId                         = request.EnvelopeId,
                EnvelopeType                       = request.EnvelopeType,
                EnvelopeCreatetime                 = request.EnvelopeCreatetime,
                EnvelopeSource                     = request.EnvelopeSource,
                EnvelopeMsgsender                  = request.EnvelopeMsgsender,
                EnvelopeMsgsenderrole              = request.EnvelopeMsgsenderrole,
                EnvelopeMsgreceiver                = request.EnvelopeMsgreceiver,
                EnvelopeMsgreceiverrole            = request.EnvelopeMsgreceiverrole,
                EnvelopeMsgtype                    = request.MessageType,
                EnvelopeMsgsubtype                 = request.EnvelopeMsgsubtype,
                EnvelopeMsgcreationtime            = request.EnvelopeMsgcreationtime,
                EnvelopeMsgversion                 = request.EnvelopeMsgversion,
                EnvelopeMsgpayloadid               = request.EnvelopeMsgpayloadid,
                EnvelopeMsgcontenttype             = request.EnvelopeMsgcontenttype,
                EnvelopeEntemsendacknowledgement   = request.EnvelopeEntemsendacknowledgement,
                EnvelopeEntemsendtooutput           = request.EnvelopeEntemsendtooutput,
                EnvelopeEntemvalidationresult       = request.EnvelopeEntemvalidationresult,
                EnvelopeEntemtimestamp              = request.EnvelopeEntemtimestamp
            });
            nackReq.Headers.Add("X-Correlation-ID", request.CorrelationId ?? messageId);
            _logger.LogInformation("[{MessageId}] → Doorgeven aan NackHandler", messageId);
            var nackResp = await _httpClientFactory.CreateClient().SendAsync(nackReq, HttpContext.RequestAborted);
            nackResp.EnsureSuccessStatusCode();
            return Content(await nackResp.Content.ReadAsStringAsync(HttpContext.RequestAborted), "application/json");
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
    // Doorgegeven door EventHandler, benodigd voor ketenverwerking
    public string? CorrelationId { get; set; }
    public string? XmlContent { get; set; }
    public string? EanCode { get; set; }
    public string? DocumentId { get; set; }
    public decimal? Quantity { get; set; }
    public DateTime? StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
    // Volledige envelope-velden (doorgestuurd door de hele keten)
    public string? EnvelopeId { get; set; }
    public string? EnvelopeType { get; set; }
    public string? EnvelopeCreatetime { get; set; }
    public string? EnvelopeSource { get; set; }
    public string? EnvelopeMsgsender { get; set; }
    public string? EnvelopeMsgsenderrole { get; set; }
    public string? EnvelopeMsgreceiver { get; set; }
    public string? EnvelopeMsgreceiverrole { get; set; }
    public string? EnvelopeMsgsubtype { get; set; }
    public string? EnvelopeMsgcreationtime { get; set; }
    public string? EnvelopeMsgversion { get; set; }
    public string? EnvelopeMsgpayloadid { get; set; }
    public string? EnvelopeMsgcontenttype { get; set; }
    public bool EnvelopeEntemsendacknowledgement { get; set; }
    public bool EnvelopeEntemsendtooutput { get; set; }
    public List<EnvelopeValidationItem>? EnvelopeEntemvalidationresult { get; set; }
    public string? EnvelopeEntemtimestamp { get; set; }
}

public class EnvelopeValidationItem
{
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
