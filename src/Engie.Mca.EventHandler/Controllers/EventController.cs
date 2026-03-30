
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.EventHandler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private static readonly HashSet<string> KnownMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllocationSeries",
        "AllocationFactorSeries",
        "AggregatedAllocationSeries"
    };

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
        var receivedAt = DateTime.UtcNow;
        var messageId = request?.MessageId;

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId ?? "(none)");
        using var messageTypeScope = LogContext.PushProperty("MessageType", request?.MessageType ?? "(none)");

        _logger.LogInformation("[{MessageId}] === COLUMN 1: EVENT HANDLER (Steps 1A-1F) ===", messageId);

        try
        {
            // Step 1A: Ontvang event — check request aanwezig en MessageId ingevuld
            if (request == null)
            {
                _logger.LogWarning("[?] ✗ Step 1A: Request body ontbreekt volledig");
                return BadRequest(new { step = "1A", error = "Request body is leeg of null" });
            }
            if (string.IsNullOrWhiteSpace(request.MessageId))
            {
                _logger.LogWarning("[?] ✗ Step 1A: MessageId ontbreekt");
                return BadRequest(new { step = "1A", error = "MessageId is verplicht" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1A: Ontvang event — request aanwezig, MessageId: {MessageId}", messageId, messageId);
            await Task.Delay(5);

            // Step 1B: Technische ontvangstbevestiging — check alle verplichte JSON-velden
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(request.MessageType)) missingFields.Add("MessageType");
            if (string.IsNullOrWhiteSpace(request.Content))     missingFields.Add("Content");

            if (missingFields.Count > 0)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1B: Verplichte velden ontbreken: {Fields}", messageId, string.Join(", ", missingFields));
                return BadRequest(new { step = "1B", error = "Verplichte JSON-velden ontbreken", missingFields });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1B: Technische ontvangstbevestiging — alle velden aanwezig", messageId);
            await Task.Delay(5);

            // Step 1C: Technische validatie XML — daadwerkelijk parsen als XML
            XDocument xmlDoc;
            try
            {
                xmlDoc = XDocument.Parse(request.Content);
            }
            catch (Exception xmlEx)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1C: XML parse mislukt: {Error}", messageId, xmlEx.Message);
                return BadRequest(new { step = "1C", error = "Content is geen geldige XML", detail = xmlEx.Message });
            }

            if (xmlDoc.Root == null)
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1C: XML heeft geen root-element", messageId);
                return BadRequest(new { step = "1C", error = "XML heeft geen root-element" });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1C: XML geldig — root-element: <{RootElement}>", messageId, xmlDoc.Root.Name.LocalName);
            await Task.Delay(5);

            // Step 1D: Logging van ontvangsttijd
            _logger.LogInformation("[{MessageId}] ✓ Step 1D: Ontvangstijd vastgelegd: {ReceivedAt:O}", messageId, receivedAt);
            await Task.Delay(5);

            // Step 1E: Berichttype identificeren — check tegen bekende types
            if (!KnownMessageTypes.Contains(request.MessageType))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1E: Onbekend berichttype: {MessageType}. Geldige types: {KnownTypes}",
                    messageId, request.MessageType, string.Join(", ", KnownMessageTypes));
                return BadRequest(new
                {
                    step = "1E",
                    error = $"Onbekend berichttype: {request.MessageType}",
                    knownTypes = KnownMessageTypes
                });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1E: Berichttype geïdentificeerd: {MessageType}", messageId, request.MessageType);
            await Task.Delay(5);

            // Step 1F: Bereid verwerking voor — check root-element komt overeen met berichttype
            var rootName = xmlDoc.Root.Name.LocalName;
            if (!rootName.Contains(request.MessageType, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[{MessageId}] ✗ Step 1F: Root-element <{RootElement}> komt niet overeen met berichttype {MessageType}",
                    messageId, rootName, request.MessageType);
                return BadRequest(new
                {
                    step = "1F",
                    error = $"Root-element <{rootName}> komt niet overeen met opgegeven berichttype {request.MessageType}"
                });
            }
            _logger.LogInformation("[{MessageId}] ✓ Step 1F: Bereid verwerking voor — root <{RootElement}> matcht {MessageType}",
                messageId, rootName, request.MessageType);
            await Task.Delay(5);

            return Ok(new
            {
                messageId,
                stepsCompleted = 6,
                status = "EventHandled",
                receivedAt,
                identifiedMessageType = request.MessageType,
                xmlRootElement = xmlDoc.Root.Name.LocalName,
                nextService = "MessageProcessor"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Event handling failed", messageId);
            return BadRequest(new { step = "unknown", error = ex.Message });
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
