using System.Xml.Linq;

namespace Engie.Mca.Api.Models;

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

public record MarketMessage(
    string MessageId,
    MessageType Type,
    string? XmlContent,
    DateTime ReceivedAt
);

public record MessageContext(
    string MessageId,
    MessageType Type,
    ProcessingStatus Status,
    ResponseType? ResponseType,
    XDocument? Xml,
    List<ValidationError> Errors,
    List<(string StepId, string Description)> Steps,
    DateTime ReceivedAt,
    DateTime? ProcessedAt
)
{
    public void AddStep(string stepId, string description)
    {
        Steps.Add((stepId, description));
    }

    public void AddError(string code, string message, string step)
    {
        Errors.Add(new ValidationError(code, message, step));
    }

    public bool HasErrors => Errors.Count > 0;
}

public record StepResult(
    bool Success,
    string Message,
    List<ValidationError> Errors
);

public record MessageDetailArtifact(
    string MessageId,
    string Type,
    string Status,
    string? ResponseType,
    int ErrorCount,
    string ErrorCodes,
    List<string> StepIds,
    DateTime ReceivedAt,
    DateTime? ProcessedAt
);

// API Request/Response DTOs
public record ProcessMessageRequest(
    string? MessageId = null,
    string? XmlContent = null,
    string? Xml = null
);

public record MessageStatusResponse(
    string MessageId,
    string Status,
    string? ResponseType,
    int ErrorCount,
    List<string> ErrorCodes,
    List<(string StepId, string Description)> Steps,
    DateTime ReceivedAt,
    DateTime? ProcessedAt
);

public record MessageResponseDto(
    string MessageId,
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
