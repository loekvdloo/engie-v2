using Engie.Mca.Api.Models;
using Engie.Mca.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace Engie.Mca.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly MessageStore _store;

    public MetricsController(MessageStore store)
    {
        _store = store;
    }

    [HttpGet]
    public IActionResult GetMetrics()
    {
        var metrics = BuildSnapshot();
        return Ok(metrics);
    }

    [HttpGet("/metrics")]
    public IActionResult GetPrometheusMetrics()
    {
        var snapshot = BuildSnapshot();
        var builder = new StringBuilder();

        builder.AppendLine("# HELP engie_messages_total Total number of processed messages");
        builder.AppendLine("# TYPE engie_messages_total counter");
        builder.AppendLine($"engie_messages_total {snapshot.TotalMessages}");
        builder.AppendLine($"engie_messages_delivered_total {snapshot.DeliveredMessages}");
        builder.AppendLine($"engie_messages_failed_total {snapshot.FailedMessages}");
        builder.AppendLine($"engie_message_success_rate {snapshot.SuccessRate.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"engie_processing_duration_avg_ms {snapshot.AverageProcessingDurationMs.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"engie_processing_duration_p95_ms {snapshot.P95ProcessingDurationMs.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"engie_errors_total {snapshot.TotalErrors}");

        foreach (var error in snapshot.ErrorsByCode)
        {
            builder.AppendLine($"engie_error_code_total{{code=\"{error.Code}\"}} {error.Count}");
        }

        foreach (var status in snapshot.MessagesByStatus)
        {
            builder.AppendLine($"engie_messages_by_status{{status=\"{status.Status}\"}} {status.Count}");
        }

        foreach (var type in snapshot.MessagesByType)
        {
            builder.AppendLine($"engie_messages_by_type{{type=\"{type.Type}\"}} {type.Count}");
        }

        return Content(builder.ToString(), "text/plain");
    }

    private MetricsSnapshot BuildSnapshot()
    {
        var messages = _store.GetAll();
        var delivered = messages.Count(message => message.Status == ProcessingStatus.Delivered);
        var failed = messages.Count(message => message.Status == ProcessingStatus.Failed);
        var durations = messages
            .Where(message => message.ProcessingDurationMs.HasValue)
            .Select(message => message.ProcessingDurationMs!.Value)
            .OrderBy(value => value)
            .ToList();

        return new MetricsSnapshot(
            TotalMessages: messages.Count,
            DeliveredMessages: delivered,
            FailedMessages: failed,
            AckMessages: messages.Count(message => message.ResponseType == ResponseType.Ack),
            NackMessages: messages.Count(message => message.ResponseType == ResponseType.Nack),
            SuccessRate: messages.Count == 0 ? 0 : Math.Round((decimal)delivered / messages.Count * 100, 2),
            AverageProcessingDurationMs: durations.Count == 0 ? 0 : Math.Round(durations.Average(), 2),
            P95ProcessingDurationMs: durations.Count == 0 ? 0 : Math.Round(Percentile(durations, 0.95), 2),
            TotalErrors: messages.Sum(message => message.Errors.Count),
            LastProcessedAt: messages.MaxBy(message => message.ProcessedAt)?.ProcessedAt,
            MessagesByStatus: messages
                .GroupBy(message => message.Status.ToString())
                .Select(group => new StatusMetric(group.Key, group.Count()))
                .OrderBy(metric => metric.Status)
                .ToList(),
            MessagesByType: messages
                .GroupBy(message => message.Type.ToString())
                .Select(group => new TypeMetric(group.Key, group.Count()))
                .OrderBy(metric => metric.Type)
                .ToList(),
            ErrorsByCode: messages
                .SelectMany(message => message.Errors)
                .GroupBy(error => error.Code)
                .Select(group => new ErrorMetric(group.Key, group.Count()))
                .OrderBy(metric => metric.Code)
                .ToList());
    }

    private static double Percentile(IReadOnlyList<double> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        var rawIndex = (orderedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(rawIndex);
        var upperIndex = (int)Math.Ceiling(rawIndex);
        if (lowerIndex == upperIndex)
        {
            return orderedValues[lowerIndex];
        }

        var weight = rawIndex - lowerIndex;
        return orderedValues[lowerIndex] + ((orderedValues[upperIndex] - orderedValues[lowerIndex]) * weight);
    }

    private sealed record MetricsSnapshot(
        int TotalMessages,
        int DeliveredMessages,
        int FailedMessages,
        int AckMessages,
        int NackMessages,
        decimal SuccessRate,
        double AverageProcessingDurationMs,
        double P95ProcessingDurationMs,
        int TotalErrors,
        DateTime? LastProcessedAt,
        List<StatusMetric> MessagesByStatus,
        List<TypeMetric> MessagesByType,
        List<ErrorMetric> ErrorsByCode);

    private sealed record StatusMetric(string Status, int Count);

    private sealed record TypeMetric(string Type, int Count);

    private sealed record ErrorMetric(string Code, int Count);
}