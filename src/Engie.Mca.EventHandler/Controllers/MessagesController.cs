using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Engie.Mca.EventHandler.Models;
using Engie.Mca.EventHandler.Services;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Engie.Mca.EventHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private static readonly string MessageProcessorBaseUrl =
        Environment.GetEnvironmentVariable("MESSAGE_PROCESSOR_BASE_URL")
        ?? "http://engie-mca-message-processor:8080";

    private readonly MessageStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MessagesController> _logger;
    private readonly MetricsAggregator _metrics;

    public MessagesController(MessageStore store, IHttpClientFactory httpClientFactory, ILogger<MessagesController> logger, MetricsAggregator metrics)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _metrics = metrics;
    }

    // POST api/messages — doet stap 1A-1F, dan keten naar MessageProcessor (2A-2E) → ... → OutputHandler (6B)
    [HttpPost]
    public async Task<IActionResult> ProcessMessage([FromBody] ProcessMessageRequest request)
    {
        var messageId     = request?.MessageId ?? Guid.NewGuid().ToString();
        var correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? request?.CorrelationId
                            ?? HttpContext.TraceIdentifier;
        var xmlContent = request?.XmlContent ?? request?.Xml ?? string.Empty;
        var receivedAt = DateTime.UtcNow;

        Response.Headers["X-Correlation-ID"] = correlationId;

        var steps  = new List<(string StepId, string Description)>();
        var errors = new List<ValidationError>();
        var sw     = Stopwatch.StartNew();

        using var corrScope = LogContext.PushProperty("CorrelationId", correlationId);
        using var midScope  = LogContext.PushProperty("MessageId", messageId);

        _logger.LogInformation("[{MessageId}] ===== PIPELINE START =====", messageId);

        try
        {
            // ── Kolom 1 – Event Handler (1A-1F) ─────────────────────────
            _logger.LogInformation("[{MessageId}] === KOLOM 1: EVENT HANDLER (1A-1F) ===", messageId);

            // 1A: MessageId aanwezig
            if (string.IsNullOrWhiteSpace(request?.MessageId))
            {
                errors.Add(new ValidationError("001", "MessageId is verplicht", "1A"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }
            _logger.LogInformation("[{MessageId}] ✓ 1A: Ontvang event", messageId);
            steps.Add(("1A", "Ontvang event"));

            // 1B: Content aanwezig
            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                errors.Add(new ValidationError("001", "XmlContent is verplicht", "1B"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }
            _logger.LogInformation("[{MessageId}] ✓ 1B: Technische ontvangstbevestiging", messageId);
            steps.Add(("1B", "Technische ontvangstbevestiging"));

            // 1C: XML parsen
            XDocument xmlDoc;
            try { xmlDoc = XDocument.Parse(xmlContent); }
            catch (Exception xmlEx)
            {
                errors.Add(new ValidationError("001", $"XML ongeldig: {xmlEx.Message}", "1C"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }
            _logger.LogInformation("[{MessageId}] ✓ 1C: XML geldig — root <{Root}>", messageId, xmlDoc.Root?.Name.LocalName);
            steps.Add(("1C", "Technische validatie XML geslaagd"));

            // 1D: Ontvangsttijd
            _logger.LogInformation("[{MessageId}] ✓ 1D: Ontvangsttijd: {ReceivedAt:O}", messageId, receivedAt);
            steps.Add(("1D", "Logging van ontvangsttijd"));

            // 1E: Berichttype
            var messageType = DetermineMessageType(xmlContent);
            using var typeScope = LogContext.PushProperty("MessageType", messageType.ToString());
            if (messageType == MessageType.Unknown)
            {
                errors.Add(new ValidationError("001", "Onbekend berichttype", "1E"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }
            _logger.LogInformation("[{MessageId}] ✓ 1E: Berichttype: {Type}", messageId, messageType);
            steps.Add(("1E", $"Berichttype geïdentificeerd: {messageType}"));

            // 1F: Root-element check
            var rootName = xmlDoc.Root?.Name.LocalName ?? string.Empty;
            if (!rootName.Contains(messageType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError("001", $"Root-element <{rootName}> matcht berichttype niet", "1F"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }
            _logger.LogInformation("[{MessageId}] ✓ 1F: Root <{Root}> matcht {Type}", messageId, rootName, messageType);
            steps.Add(("1F", "Bereid verwerking voor"));

            // ── Keten: stuur door naar MessageProcessor (die doet stap 2A→6B) ─
            _logger.LogInformation("[{MessageId}] → Doorgeven aan MessageProcessor/phase2", messageId);

            using var phase2Req = new HttpRequestMessage(HttpMethod.Post, $"{MessageProcessorBaseUrl}/api/processor/phase2");
            phase2Req.Content = JsonContent.Create(new
            {
                MessageId     = messageId,
                CorrelationId = correlationId,
                MessageType   = messageType.ToString(),
                XmlContent    = xmlContent,
                EanCode       = ExtractField(xmlContent, "EAN") ?? string.Empty,
                DocumentId    = ExtractField(xmlContent, "DocumentID"),
                Quantity      = ExtractDecimal(xmlContent, "Quantity"),
                StartDateTime = ExtractDateTime(xmlContent, "StartDateTime"),
                EndDateTime   = ExtractDateTime(xmlContent, "EndDateTime")
            });
            phase2Req.Headers.Add("X-Correlation-ID", correlationId);

            var phase2Resp = await _httpClientFactory.CreateClient().SendAsync(phase2Req, HttpContext.RequestAborted);

            if (!phase2Resp.IsSuccessStatusCode)
            {
                errors.Add(new ValidationError("002", "MessageProcessor niet bereikbaar", "2A"));
                return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
            }

            var chainResult = await phase2Resp.Content.ReadAsStringAsync(HttpContext.RequestAborted);

            // Parse eindresultaat (komt terug via keten: Processor → Validator → Processor → Nack → Output)
            var chainJson   = JsonDocument.Parse(chainResult);
            var chainStatus = chainJson.RootElement.TryGetProperty("status",       out var spProp) ? spProp.GetString() ?? "Failed" : "Failed";
            var chainRespType = chainJson.RootElement.TryGetProperty("responseType", out var rtProp) ? rtProp.GetString() ?? "Nack" : "Nack";
            var chainCodes  = chainJson.RootElement.TryGetProperty("errorCodes",   out var ecProp)
                ? ecProp.EnumerateArray().Select(e => e.GetString() ?? "").Where(c => c.Length > 0).ToList()
                : new List<string>();

            var finalStatus   = chainStatus  == "Delivered" ? ProcessingStatus.Delivered : ProcessingStatus.Failed;
            var finalResponse = chainRespType == "Ack"       ? ResponseType.Ack           : ResponseType.Nack;
            var finalErrors   = chainCodes.Select(c => new ValidationError(c, c, "chain")).ToList();

            sw.Stop();
            _store.Save(new MessageContext(
                messageId, correlationId, messageType,
                finalStatus, finalResponse, null,
                finalErrors, steps, receivedAt, DateTime.UtcNow, sw.Elapsed.TotalMilliseconds
            ));
            _metrics.Record(finalResponse, finalStatus, sw.Elapsed.TotalMilliseconds, finalErrors);

            using var rtScope = LogContext.PushProperty("ResponseType", chainRespType);
            using var ecScope = LogContext.PushProperty("ErrorCodes", string.Join(",", chainCodes));
            _logger.LogInformation("[{MessageId}] ===== PIPELINE COMPLETE ===== Status={Status}", messageId, chainStatus);
            return Content(chainResult, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Pipeline mislukt", messageId);
            errors.Add(new ValidationError("999", ex.Message, "Pipeline"));
            return BuildFailedResult(messageId, correlationId, steps, errors, receivedAt, sw);
        }
    }

    // GET api/messages/{messageId}
    [HttpGet("{messageId}")]
    public IActionResult GetMessage(string messageId)
    {
        var msg = _store.Get(messageId);
        if (msg == null)
            return NotFound(new ErrorResponse("004", $"Bericht {messageId} niet gevonden"));

        return Ok(new MessageStatusResponse(
            msg.MessageId, msg.CorrelationId,
            msg.Status.ToString(), msg.ResponseType?.ToString(),
            msg.Errors.Count, msg.Errors.Select(e => e.Code).ToList(),
            msg.Steps, msg.ReceivedAt, msg.ProcessedAt, msg.ProcessingDurationMs
        ));
    }

    // GET api/messages/{messageId}/steps
    [HttpGet("{messageId}/steps")]
    public IActionResult GetMessageSteps(string messageId)
    {
        var msg = _store.Get(messageId);
        if (msg == null)
            return NotFound(new ErrorResponse("004", $"Bericht {messageId} niet gevonden"));

        var stepsWithCols = msg.Steps.Select(s => new
        {
            Step        = s.StepId,
            Description = s.Description,
            Column      = DetermineColumn(s.StepId),
            HasError    = msg.Errors.Any(e => e.Step == s.StepId)
        }).ToList();

        return Ok(new
        {
            msg.MessageId,
            Status      = msg.Status.ToString(),
            TotalSteps  = msg.Steps.Count,
            TotalErrors = msg.Errors.Count,
            Steps       = stepsWithCols,
            Errors      = msg.Errors.Select(e => new { e.Code, e.Message, e.Step }).ToList()
        });
    }

    // GET api/messages/status/{status}
    [HttpGet("status/{status}")]
    public IActionResult GetMessagesByStatus(string status)
    {
        var msgs = _store.GetByStatus(status);
        return Ok(msgs.Select(m => new MessageResponseDto(
            m.MessageId, m.CorrelationId,
            m.Status.ToString(), m.ResponseType?.ToString() ?? "Unknown",
            m.Errors.Count, m.Errors.Select(e => e.Code).ToList()
        )).ToList());
    }

    // GET api/messages
    [HttpGet]
    public IActionResult GetAllMessages()
    {
        var msgs = _store.GetAll();
        return Ok(msgs.Select(m => new MessageResponseDto(
            m.MessageId, m.CorrelationId,
            m.Status.ToString(), m.ResponseType?.ToString() ?? "Unknown",
            m.Errors.Count, m.Errors.Select(e => e.Code).ToList()
        )).ToList());
    }

    // GET api/messages/stats/summary
    [HttpGet("stats/summary")]
    public IActionResult GetStatsSummary()
    {
        var all        = _store.GetAll();
        var delivered  = all.Count(m => m.Status == ProcessingStatus.Delivered);
        var failed     = all.Count(m => m.Status == ProcessingStatus.Failed);
        var totalSteps = all.Sum(m => m.Steps.Count);

        return Ok(new
        {
            TotalMessages          = all.Count,
            Delivered              = delivered,
            Failed                 = failed,
            SuccessRate            = all.Count > 0 ? (decimal)delivered / all.Count * 100 : 0,
            TotalStepsExecuted     = totalSteps,
            AverageStepsPerMessage = all.Count > 0 ? totalSteps / all.Count : 0,
            TotalErrorsDetected    = all.Sum(m => m.Errors.Count),
            MessageTypes           = all.GroupBy(m => m.Type)
                                       .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
                                       .ToList()
        });
    }

    // GET /api/metrics
    [HttpGet("/api/metrics")]
    public IActionResult GetMetrics()
    {
        var snap = _metrics.GetSnapshot();
        var all = _store.GetAll();
        var rate = snap.Total > 0 ? (decimal)snap.Delivered / snap.Total * 100 : 0;

        return Ok(new
        {
            TotalMessages               = snap.Total,
            DeliveredMessages          = snap.Delivered,
            FailedMessages             = snap.Failed,
            AckMessages                = snap.Ack,
            NackMessages               = snap.Nack,
            SuccessRate                = Math.Round(rate, 2),
            AverageProcessingDurationMs = Math.Round(snap.AvgDurationMs, 2),
            P95ProcessingDurationMs    = Math.Round(snap.P95DurationMs, 2),
            TotalErrors                = snap.ErrorsByCode.Sum(e => e.Count),
            LastProcessedAt            = all.OrderByDescending(m => m.ProcessedAt).FirstOrDefault()?.ProcessedAt,
            MessagesByStatus           = all.GroupBy(m => m.Status.ToString())
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList(),
            MessagesByType             = all.GroupBy(m => m.Type.ToString())
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToList(),
            ErrorsByCode               = snap.ErrorsByCode
                .Select(e => new { e.Code, e.Count, Description = "" })
                .ToList()
        });
    }

    // POST api/messages/{messageId}/reprocess
    [HttpPost("{messageId}/reprocess")]
    public async Task<IActionResult> ReprocessMessage(string messageId)
    {
        var existing = _store.Get(messageId);
        if (existing == null)
            return NotFound(new ErrorResponse("004", $"Bericht {messageId} niet gevonden"));

        // Verwerk opnieuw met een nieuw suffix
        var reprocessRequest = new ProcessMessageRequest(
            MessageId:   messageId + "-retry",
            XmlContent:  null,
            Xml:         null,
            CorrelationId: existing.CorrelationId
        );
        return await ProcessMessage(reprocessRequest);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private IActionResult BuildFailedResult(
        string messageId, string correlationId,
        List<(string StepId, string Description)> steps,
        List<ValidationError> errors,
        DateTime receivedAt, Stopwatch sw)
    {
        sw.Stop();
        var ctx = new MessageContext(
            messageId, correlationId, MessageType.Unknown,
            ProcessingStatus.Failed, ResponseType.Nack, null,
            errors, steps, receivedAt, DateTime.UtcNow, sw.Elapsed.TotalMilliseconds
        );
        _store.Save(ctx);
        _metrics.Record(ResponseType.Nack, ProcessingStatus.Failed, sw.Elapsed.TotalMilliseconds, errors);

        using var rtScope = LogContext.PushProperty("ResponseType", "Nack");
        using var ecScope = LogContext.PushProperty("ErrorCodes", string.Join(",", errors.Select(e => e.Code)));
        _logger.LogWarning("[{MessageId}] ✗ Pipeline mislukt. Codes: {Codes}",
            messageId, string.Join(",", errors.Select(e => e.Code)));

        return Ok(new MessageResponseDto(
            messageId, correlationId,
            ProcessingStatus.Failed.ToString(), "Nack",
            errors.Count, errors.Select(e => e.Code).ToList()
        ));
    }

    private static MessageType DetermineMessageType(string xml)
    {
        if (xml.Contains("AllocationFactorSeries", StringComparison.OrdinalIgnoreCase))
            return MessageType.AllocationFactorSeries;
        if (xml.Contains("AggregatedAllocationSeries", StringComparison.OrdinalIgnoreCase))
            return MessageType.AggregatedAllocationSeries;
        if (xml.Contains("AllocationSeries", StringComparison.OrdinalIgnoreCase))
            return MessageType.AllocationSeries;
        return MessageType.Unknown;
    }

    private static string DetermineColumn(string stepId)
    {
        if (stepId.StartsWith("1")) return "Kolom 1 – Event Handler";
        if (stepId.StartsWith("2")) return "Kolom 2 – Message Processor (phase2)";
        if (stepId.StartsWith("3")) return "Kolom 3 – Message Validator";
        if (stepId.StartsWith("4")) return "Kolom 4 – Message Processor (phase4)";
        if (stepId.StartsWith("5")) return "Kolom 5 – N-ACK Handler";
        if (stepId.StartsWith("6")) return "Kolom 6 – Output Handler";
        return "Onbekend";
    }

    private static string? ExtractField(string? xml, string tag)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(xml, $"<{tag}>(.*?)</{tag}>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static decimal? ExtractDecimal(string? xml, string tag)
    {
        var val = ExtractField(xml, tag);
        return decimal.TryParse(val,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : null;
    }

    private static DateTime? ExtractDateTime(string? xml, string tag)
    {
        var val = ExtractField(xml, tag);
        return DateTime.TryParse(val, null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var dt) ? dt : null;
    }
}
