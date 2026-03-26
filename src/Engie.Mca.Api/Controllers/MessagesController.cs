using Microsoft.AspNetCore.Mvc;
using Engie.Mca.Api.Models;
using Engie.Mca.Api.Services;
using Serilog.Context;

namespace Engie.Mca.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{


    private readonly IMicroserviceOrchestrator _orchestrator;
    private readonly MessageStore _store;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMicroserviceOrchestrator orchestrator, MessageStore store, ILogger<MessagesController> logger)
    {
        _orchestrator = orchestrator;
        _store = store;
        _logger = logger;
    }
    /// <summary>
    /// Process a market message (29 steps end-to-end)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ProcessMessage([FromBody] ProcessMessageRequest request)
    {
        try
        {
            var messageId = request.MessageId ?? Guid.NewGuid().ToString();
            var correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? request.CorrelationId
                ?? HttpContext.TraceIdentifier;
            var content = request.XmlContent ?? request.Xml ?? "<Message/>";
            var messageType = DetermineMessageType(content);

            Response.Headers["X-Correlation-ID"] = correlationId;

            using var correlationScope = LogContext.PushProperty("CorrelationId", correlationId);
            using var messageIdScope = LogContext.PushProperty("MessageId", messageId);
            using var messageTypeScope = LogContext.PushProperty("MessageType", messageType.ToString());

            _logger.LogInformation("Processing message request received");

            var message = new MarketMessage(
                messageId,
                correlationId,
                messageType,
                content,
                DateTime.UtcNow
            );

            var result = await _orchestrator.ProcessAsync(message, HttpContext.RequestAborted);
            _store.Save(result);

            var response = new MessageResponseDto(
                result.MessageId,
                result.CorrelationId,
                result.Status.ToString(),
                result.ResponseType?.ToString() ?? "Unknown",
                result.Errors.Count,
                result.Errors.Select(e => e.Code).ToList()
            );

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return BadRequest(new ErrorResponse("999", "Failed to process message", ex.Message));
        }
    }

    /// <summary>
    /// Get message status by ID
    /// </summary>
    [HttpGet("{messageId}")]
    public IActionResult GetMessage(string messageId)
    {
        var msg = _store.Get(messageId);
        if (msg == null)
            return NotFound(new ErrorResponse("004", $"Message {messageId} not found"));

        var response = new MessageStatusResponse(
            msg.MessageId,
            msg.CorrelationId,
            msg.Status.ToString(),
            msg.ResponseType?.ToString(),
            msg.Errors.Count,
            msg.Errors.Select(e => e.Code).ToList(),
            msg.Steps,
            msg.ReceivedAt,
            msg.ProcessedAt,
            msg.ProcessingDurationMs
        );

        return Ok(response);
    }

    /// <summary>
    /// Get all steps with columns for a message
    /// </summary>
    [HttpGet("{messageId}/steps")]
    public IActionResult GetMessageSteps(string messageId)
    {
        var msg = _store.Get(messageId);
        if (msg == null)
            return NotFound(new ErrorResponse("004", $"Message {messageId} not found"));

        var stepsWithColumns = msg.Steps.Select(s => new
        {
            Step = s.StepId,
            Description = s.Description,
            Column = DetermineColumn(s.StepId),
            HasError = msg.Errors.Any(e => e.Step == s.StepId)
        }).ToList();

        return Ok(new
        {
            MessageId = msg.MessageId,
            Status = msg.Status.ToString(),
            TotalSteps = msg.Steps.Count,
            TotalErrors = msg.Errors.Count,
            Steps = stepsWithColumns,
            Errors = msg.Errors.Select(e => new
            {
                Code = e.Code,
                Message = e.Message,
                Step = e.Step
            }).ToList()
        });
    }

    /// <summary>
    /// Get messages by status
    /// </summary>
    [HttpGet("status/{status}")]
    public IActionResult GetMessagesByStatus(string status)
    {
        var messages = _store.GetByStatus(status);
        var response = messages.Select(m => new MessageResponseDto(
            m.MessageId,
            m.CorrelationId,
            m.Status.ToString(),
            m.ResponseType?.ToString() ?? "Unknown",
            m.Errors.Count,
            m.Errors.Select(e => e.Code).ToList()
        )).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Get all messages
    /// </summary>
    [HttpGet]
    public IActionResult GetAllMessages()
    {
        var messages = _store.GetAll();
        var response = messages.Select(m => new MessageResponseDto(
            m.MessageId,
            m.CorrelationId,
            m.Status.ToString(),
            m.ResponseType?.ToString() ?? "Unknown",
            m.Errors.Count,
            m.Errors.Select(e => e.Code).ToList()
        )).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Get processing statistics
    /// </summary>
    [HttpGet("stats/summary")]
    public IActionResult GetStatsSummary()
    {
        var allMessages = _store.GetAll();
        var delivered = allMessages.Count(m => m.Status == ProcessingStatus.Delivered);
        var failed = allMessages.Count(m => m.Status == ProcessingStatus.Failed);
        var totalSteps = allMessages.Sum(m => m.Steps.Count);
        var totalErrors = allMessages.Sum(m => m.Errors.Count);

        return Ok(new
        {
            TotalMessages = allMessages.Count,
            Delivered = delivered,
            Failed = failed,
            SuccessRate = allMessages.Count > 0 ? (decimal)delivered / allMessages.Count * 100 : 0,
            TotalStepsExecuted = totalSteps,
            AverageStepsPerMessage = allMessages.Count > 0 ? totalSteps / allMessages.Count : 0,
            TotalErrorsDetected = totalErrors,
            MessageTypes = allMessages.GroupBy(m => m.Type).Select(g => new
            {
                Type = g.Key.ToString(),
                Count = g.Count()
            }).ToList()
        });
    }

    /// <summary>
    /// Reprocess a message
    /// </summary>
    [HttpPost("{messageId}/reprocess")]
    public async Task<IActionResult> ReprocessMessage(string messageId)
    {
        var original = _store.Get(messageId);
        if (original == null)
            return NotFound(new ErrorResponse("004", $"Message {messageId} not found"));

        _logger.LogInformation("Reprocessing message: {MessageId}", messageId);

        var message = new MarketMessage(
            messageId,
            original.CorrelationId,
            original.Type,
            original.Xml?.ToString() ?? "<Message/>",
            DateTime.UtcNow
        );

        Response.Headers["X-Correlation-ID"] = original.CorrelationId;

        var result = await _orchestrator.ProcessAsync(message, HttpContext.RequestAborted);
        _store.Save(result);

        var response = new MessageResponseDto(
            result.MessageId,
            result.CorrelationId,
            result.Status.ToString(),
            result.ResponseType?.ToString() ?? "Unknown",
            result.Errors.Count,
            result.Errors.Select(e => e.Code).ToList()
        );

        return Ok(response);
    }

    private string DetermineColumn(string stepId)
    {
        return stepId[0] switch
        {
            '1' => "Column 1: Event Handler",
            '2' => "Column 2: Message Processor (Phase 2)",
            '3' => "Column 3: Message Validator",
            '4' => "Column 2+4: Message Processor (Phase 4)",
            '5' => "Column 5: N-ACK Handler",
            '6' => "Column 6: Output Handler",
            _ => "Unknown"
        };
    }

    private MessageType DetermineMessageType(string xmlContent)
    {
        try
        {
            if (xmlContent.Contains("AllocationSeries", StringComparison.OrdinalIgnoreCase))
                return MessageType.AllocationSeries;
            if (xmlContent.Contains("AllocationFactorSeries", StringComparison.OrdinalIgnoreCase))
                return MessageType.AllocationFactorSeries;
            if (xmlContent.Contains("AggregatedAllocationSeries", StringComparison.OrdinalIgnoreCase))
                return MessageType.AggregatedAllocationSeries;
        }
        catch { }

        return MessageType.Unknown;
    }
}
