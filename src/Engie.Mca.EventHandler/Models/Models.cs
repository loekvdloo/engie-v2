using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Engie.Mca.EventHandler.Models;

public enum MessageType
{
    AllocationSeries,
    AllocationFactorSeries,
    AggregatedAllocationSeries,
    AllocationServiceNotification,
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

/// <summary>
/// Real Engie MCA envelope event format — incoming event from ENTEM.
/// </summary>
public class EnvelopeEvent
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Createtime { get; set; }
    public string? Source { get; set; }
    public string? Msgsender { get; set; }
    public string? Msgsenderrole { get; set; }
    public string? Msgreceiver { get; set; }
    public string? Msgreceiverrole { get; set; }
    public string Msgtype { get; set; } = string.Empty;
    public string? Msgsubtype { get; set; }
    public string Msgid { get; set; } = string.Empty;
    public string? Msgcorrelationid { get; set; }
    public string? Msgcreationtime { get; set; }
    public string? Msgversion { get; set; }
    public string? Msgpayloadid { get; set; }
    public string? Msgcontenttype { get; set; }
    public string Msgpayload { get; set; } = string.Empty;
    public bool Entemsendacknowledgement { get; set; }
    public bool Entemsendtooutput { get; set; }
    public List<EntemValidationResultItem>? Entemvalidationresult { get; set; }
    public string? Entemtimestamp { get; set; }
}

public class EntemValidationResultItem
{
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
