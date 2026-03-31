using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Engie.Mca.EventHandler.Models;

public enum MessageType
{
    AllocationSeries,
    AllocationFactorSeries,
    AggregatedAllocationSeries,
    Unknown
}

public enum ProcessingStatus
{
    Received,
    Processing,
    Delivered,
    Failed,
    Retrying
}

public enum ResponseType
{
    Ack,
    Nack
}

public record ValidationError(string Code, string Message, string Step);

public record MessageContext(
    string MessageId,
    string CorrelationId,
    MessageType Type,
    ProcessingStatus Status,
    ResponseType? ResponseType,
    XDocument? Xml,
    List<ValidationError> Errors,
    List<(string StepId, string Description)> Steps,
    DateTime ReceivedAt,
    DateTime? ProcessedAt,
    double? ProcessingDurationMs
);

public record ProcessMessageRequest(
    string? MessageId = null,
    string? XmlContent = null,
    string? Xml = null,
    string? CorrelationId = null
);

public record MessageStatusResponse(
    string MessageId,
    string CorrelationId,
    string Status,
    string? ResponseType,
    int ErrorCount,
    List<string> ErrorCodes,
    List<(string StepId, string Description)> Steps,
    DateTime ReceivedAt,
    DateTime? ProcessedAt,
    double? ProcessingDurationMs
);

public record MessageResponseDto(
    string MessageId,
    string CorrelationId,
    string Status,
    string ResponseType,
    int ErrorCount,
    List<string> ErrorCodes
);

public record ErrorResponse(
    string Code,
    string Message,
    string? Details = null
);
