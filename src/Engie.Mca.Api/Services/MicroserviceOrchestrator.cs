using Engie.Mca.Api.Models;
using Serilog.Context;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Http.Json;
using System.Text.Json;

namespace Engie.Mca.Api.Services;

public interface IMicroserviceOrchestrator
{
    Task<MessageContext> ProcessAsync(MarketMessage message, CancellationToken cancellationToken = default);
}

public class MicroserviceOrchestrator : IMicroserviceOrchestrator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MicroserviceOrchestrator> _logger;

    private const string EventHandlerUrl = "http://localhost:5001/api/event";
    private const string MessageProcessorUrl = "http://localhost:5002/api/processor";
    private const string MessageValidatorUrl = "http://localhost:5003/api/validator";
    private const string NackHandlerUrl = "http://localhost:5004/api/nack";
    private const string OutputHandlerUrl = "http://localhost:5005/api/output";

    public MicroserviceOrchestrator(HttpClient httpClient, ILogger<MicroserviceOrchestrator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MessageContext> ProcessAsync(MarketMessage message, CancellationToken cancellationToken = default)
    {
        var steps = new List<(string StepId, string Description)>();
        var errors = new List<ValidationError>();
        var response = ResponseType.Ack;
        var stopwatch = Stopwatch.StartNew();

        using var correlationScope = LogContext.PushProperty("CorrelationId", message.CorrelationId);
        using var messageIdScope = LogContext.PushProperty("MessageId", message.MessageId);
        using var messageTypeScope = LogContext.PushProperty("MessageType", message.Type.ToString());

        try
        {
            _logger.LogInformation("[{MessageId}] ===== ORCHESTRATION START =====", message.MessageId);

            // Step 1: Event Handler (Steps 1A-1F)
            _logger.LogInformation("[{MessageId}] Calling Event Handler Service...", message.MessageId);
            var eventResponse = await CallService(EventHandlerUrl + "/handle", new
            {
                MessageId = message.MessageId,
                MessageType = message.Type.ToString(),
                Content = message.XmlContent
            }, message.CorrelationId, cancellationToken);

            if (eventResponse == null)
            {
                errors.Add(new ValidationError("001", "Event handler service failed", "1A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            steps.Add(("1A", "Ontvang event"));
            steps.Add(("1B", "Technische ontvangstbevestiging"));
            steps.Add(("1C", "Technische validatie XML geslaagd"));
            steps.Add(("1D", "Logging van ontvangsttijd"));
            steps.Add(("1E", $"Berichttype geïdentificeerd: {message.Type}"));
            steps.Add(("1F", "Bereid verwerking voor"));

            // Step 2: Message Processor Phase 2 (Steps 2A-2E)
            _logger.LogInformation("[{MessageId}] Calling Message Processor Phase 2...", message.MessageId);
            var phase2Response = await CallService(MessageProcessorUrl + "/phase2", new
            {
                MessageId = message.MessageId,
                MessageType = message.Type.ToString()
            }, message.CorrelationId, cancellationToken);

            if (phase2Response == null)
            {
                errors.Add(new ValidationError("002", "Message processor phase 2 failed", "2A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            steps.Add(("2A", $"Classificeer berichttype: {message.Type}"));
            steps.Add(("2B", "Bepaal prioriteit: Normaal"));
            steps.Add(("2C", "Plaats in wachtrij: Queue-OK"));
            steps.Add(("2D", "Uitzonderingen geen gepark"));
            steps.Add(("2E", "Geen herversending vereist"));

            // Step 3: Message Validator (Steps 3A-3G)
            _logger.LogInformation("[{MessageId}] Calling Message Validator...", message.MessageId);
            var validatorResponse = await CallService(MessageValidatorUrl + "/validate", new
            {
                MessageId = message.MessageId,
                EanCode = ExtractField(message.XmlContent, "EAN") ?? string.Empty,
                DocumentId = ExtractField(message.XmlContent, "DocumentID"),
                Quantity = ExtractDecimal(message.XmlContent, "Quantity"),
                StartDateTime = ExtractDateTime(message.XmlContent, "StartDateTime"),
                EndDateTime = ExtractDateTime(message.XmlContent, "EndDateTime"),
                Content = message.XmlContent
            }, message.CorrelationId, cancellationToken);

            if (validatorResponse == null)
            {
                errors.Add(new ValidationError("003", "Message validator service failed", "3A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            // Check if validation passed — collect ALL error codes returned
            bool hasErrors = false;
            if (validatorResponse.RootElement.TryGetProperty("isValid", out var isValidProp))
            {
                hasErrors = !isValidProp.GetBoolean();
                if (hasErrors && validatorResponse.RootElement.TryGetProperty("errorCodes", out var errorCodesProp))
                {
                    foreach (var codeElem in errorCodesProp.EnumerateArray())
                    {
                        var code = codeElem.GetString();
                        if (!string.IsNullOrEmpty(code))
                        {
                            var description = FaultCodeCatalog.GetDescription(code);
                            errors.Add(new ValidationError(code, description, "3"));
                        }
                    }
                    response = ResponseType.Nack;
                }
            }

            steps.Add(("3A", "Controleer EAN-code"));
            steps.Add(("3B", "Controleer datum/tijd"));
            steps.Add(("3C", "Controleer verplichte velden"));
            steps.Add(("3D", "Validatieregels zijn configureerbaar"));
            steps.Add(("3E", "Controleer tijdvenster"));
            steps.Add(("3F", "Controleer volgordelijkheid"));
            steps.Add(("3G", "Herbruikbare validatieregels"));

            // Step 4: Message Processor Phase 4 (Steps 4A-4E)
            _logger.LogInformation("[{MessageId}] Calling Message Processor Phase 4...", message.MessageId);
            var phase4Response = await CallService(MessageProcessorUrl + "/phase4", new
            {
                MessageId = message.MessageId,
                MessageType = message.Type.ToString(),
                HasErrors = hasErrors,
                ErrorCode = errors.FirstOrDefault()?.Code,
                ErrorCodes = errors.Select(error => error.Code).ToList()
            }, message.CorrelationId, cancellationToken);

            if (phase4Response == null)
            {
                errors.Add(new ValidationError("004", "Message processor phase 4 failed", "4A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            steps.Add(("4A", $"Genereer {response}-bericht"));
            steps.Add(("4B", "Foutcodificatie"));
            steps.Add(("4C", "Voeg foutcodes toe"));
            steps.Add(("4D", "Registreer validatieresultaat"));
            steps.Add(("4E", $"Configureer {response}-verzending"));

            // Step 5: N-ACK Handler (Steps 5A-5D)
            _logger.LogInformation("[{MessageId}] Calling N-ACK Handler...", message.MessageId);
            var nackResponse = await CallService(NackHandlerUrl + "/send", new
            {
                MessageId = message.MessageId,
                Response = response.ToString()
            }, message.CorrelationId, cancellationToken);

            if (nackResponse == null)
            {
                errors.Add(new ValidationError("005", "N-ACK handler service failed", "5A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            steps.Add(("5A", $"Verstuur {response}-bericht"));
            steps.Add(("5B", "Geconfigureerde respons"));
            steps.Add(("5C", "Logging verzendtijd"));
            steps.Add(("5D", "Zelfstandige verzending"));

            // Step 6: Output Handler (Steps 6A-6B)
            _logger.LogInformation("[{MessageId}] Calling Output Handler...", message.MessageId);
            var outputResponse = await CallService(OutputHandlerUrl + "/finalize", new
            {
                MessageId = message.MessageId,
                Status = response == ResponseType.Nack ? "Failed" : "Delivered"
            }, message.CorrelationId, cancellationToken);

            if (outputResponse == null)
            {
                errors.Add(new ValidationError("006", "Output handler service failed", "6A"));
                return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
            }

            steps.Add(("6A", "Doorzetten naar raw-layer"));
            steps.Add(("6B", "Registreer afleverstatus"));

            var status = hasErrors ? ProcessingStatus.Failed : ProcessingStatus.Delivered;
            var finalResponse = hasErrors ? ResponseType.Nack : ResponseType.Ack;

            _logger.LogInformation("[{MessageId}] ===== ORCHESTRATION COMPLETE ===== Status: {Status} | Response: {Response} | Steps: {StepCount}", 
                message.MessageId, status, finalResponse, steps.Count);

            return new MessageContext(
                message.MessageId,
                message.CorrelationId,
                message.Type,
                status,
                finalResponse,
                null,
                errors,
                steps,
                message.ReceivedAt,
                DateTime.UtcNow,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Orchestration failed", message.MessageId);
            errors.Add(new ValidationError("999", ex.Message, "Pipeline"));
            return CreateFailedMessage(message, steps, errors, ResponseType.Nack, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private MessageContext CreateFailedMessage(
        MarketMessage message,
        List<(string StepId, string Description)> steps,
        List<ValidationError> errors,
        ResponseType response,
        double? processingDurationMs)
    {
        return new MessageContext(
            message.MessageId,
            message.CorrelationId,
            message.Type,
            ProcessingStatus.Failed,
            response,
            null,
            errors,
            steps,
            message.ReceivedAt,
            DateTime.UtcNow,
            processingDurationMs
        );
    }

    private async Task<JsonDocument?> CallService(string url, object requestBody, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("X-Correlation-ID", correlationId);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Service call to {Url} returned {StatusCode}", url, response.StatusCode);
                return default;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call service {Url}", url);
            return default;
        }
    }

    private string? ExtractField(string? xml, string tag)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(xml, $"<{tag}>(.*?)</{tag}>");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private decimal? ExtractDecimal(string? xml, string tag)
    {
        var val = ExtractField(xml, tag);
        return decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private DateTime? ExtractDateTime(string? xml, string tag)
    {
        var val = ExtractField(xml, tag);
        return DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }
}
