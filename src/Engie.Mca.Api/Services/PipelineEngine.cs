using System.Xml.Linq;
using Engie.Mca.Api.Models;

namespace Engie.Mca.Api.Services;

public class PipelineEngine
{
    private readonly IEventHandlerColumn1 _eventHandler;
    private readonly IMessageProcessorColumn2And4 _messageProcessor;
    private readonly IMessageValidatorColumn3 _validator;
    private readonly INackHandlerColumn5 _nackHandler;
    private readonly IOutputHandlerColumn6 _outputHandler;
    private readonly ILogger<PipelineEngine> _logger;

    public PipelineEngine(
        IEventHandlerColumn1 eventHandler,
        IMessageProcessorColumn2And4 messageProcessor,
        IMessageValidatorColumn3 validator,
        INackHandlerColumn5 nackHandler,
        IOutputHandlerColumn6 outputHandler,
        ILogger<PipelineEngine> logger)
    {
        _eventHandler = eventHandler;
        _messageProcessor = messageProcessor;
        _validator = validator;
        _nackHandler = nackHandler;
        _outputHandler = outputHandler;
        _logger = logger;
    }

    public async Task<MessageContext> ProcessAsync(MarketMessage message)
    {
        var context = new MessageContext(
            message.MessageId,
            message.Type,
            ProcessingStatus.Received,
            null,
            null,
            new List<ValidationError>(),
            new List<(string, string)>(),
            message.ReceivedAt,
            null
        );

        _logger.LogInformation("[{Id}] ===== PIPELINE START =====", message.MessageId);
        _logger.LogInformation("[{Id}] Message Type: {Type}", message.MessageId, message.Type);

        try
        {
            // Phase 1: Event Handler (Steps 1A-1F)
            _logger.LogInformation("[{Id}] === COLUMN 1: EVENT HANDLER (Steps 1A-1F) ===", message.MessageId);
            context = await _eventHandler.ProcessAsync(context, message);
            LogSteps(message.MessageId, context);
            if (context.HasErrors)
            {
                _logger.LogWarning("[{Id}] Column 1 failed with {Count} errors", message.MessageId, context.Errors.Count);
                context = context with { Status = ProcessingStatus.Failed };
                return context;
            }

            // Phase 2: Message Processor (Steps 2A-2E)
            _logger.LogInformation("[{Id}] === COLUMN 2: MESSAGE PROCESSOR PHASE 2 (Steps 2A-2E) ===", message.MessageId);
            context = await _messageProcessor.ProcessPhase2Async(context);
            LogSteps(message.MessageId, context);
            if (context.HasErrors)
            {
                _logger.LogWarning("[{Id}] Column 2 Phase 2 failed with {Count} errors", message.MessageId, context.Errors.Count);
                context = context with { Status = ProcessingStatus.Failed };
                return context;
            }

            // Phase 3: Message Validator (Steps 3A-3G)
            _logger.LogInformation("[{Id}] === COLUMN 3: MESSAGE VALIDATOR (Steps 3A-3G) ===", message.MessageId);
            context = await _validator.ProcessAsync(context);
            LogSteps(message.MessageId, context);
            if (context.HasErrors)
            {
                _logger.LogWarning("[{Id}] Column 3 validation failed with {Count} errors", message.MessageId, context.Errors.Count);
                context = context with { Status = ProcessingStatus.Failed };
            }

            // Phase 4: Message Processor Phase 4 (Steps 4A-4E) + N-ACK
            _logger.LogInformation("[{Id}] === COLUMN 2+4: MESSAGE PROCESSOR PHASE 4 (Steps 4A-4E) ===", message.MessageId);
            context = await _messageProcessor.ProcessPhase4Async(context);
            LogSteps(message.MessageId, context);

            // Phase 5: N-ACK Handler (Steps 5A-5D)
            _logger.LogInformation("[{Id}] === COLUMN 5: N-ACK HANDLER (Steps 5A-5D) ===", message.MessageId);
            context = await _nackHandler.ProcessAsync(context);
            LogSteps(message.MessageId, context);

            // Phase 6: Output Handler (Steps 6A-6B)
            _logger.LogInformation("[{Id}] === COLUMN 6: OUTPUT HANDLER (Steps 6A-6B) ===", message.MessageId);
            context = await _outputHandler.ProcessAsync(context);
            LogSteps(message.MessageId, context);

            context = context with 
            { 
                Status = context.HasErrors ? ProcessingStatus.Failed : ProcessingStatus.Delivered,
                ProcessedAt = DateTime.UtcNow
            };

            _logger.LogInformation("[{Id}] ===== PIPELINE COMPLETE ===== Status: {Status} | Response: {Response} | Steps: {Count} | Errors: {Errors}", 
                message.MessageId, context.Status, context.ResponseType, context.Steps.Count, context.Errors.Count);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] PIPELINE EXCEPTION", message.MessageId);
            context.AddError("999", $"System error: {ex.Message}", "Pipeline");
            context = context with 
            { 
                Status = ProcessingStatus.Failed,
                ProcessedAt = DateTime.UtcNow
            };
            return context;
        }
    }

    private void LogSteps(string messageId, MessageContext context)
    {
        var newSteps = context.Steps.Skip(Math.Max(0, context.Steps.Count - 5)).ToList();
        foreach (var (stepId, description) in newSteps)
        {
            _logger.LogInformation("[{Id}]   ✓ Step {StepId}: {Description}", messageId, stepId, description);
        }
        
        if (context.Errors.Count > 0)
        {
            var newErrors = context.Errors.Skip(Math.Max(0, context.Errors.Count - 2)).ToList();
            foreach (var error in newErrors)
            {
                _logger.LogWarning("[{Id}]   ✗ Error {Code} ({Step}): {Message}", messageId, error.Code, error.Step, error.Message);
            }
        }
    }
}

public interface IEventHandlerColumn1
{
    Task<MessageContext> ProcessAsync(MessageContext context, MarketMessage message);
}

public interface IMessageProcessorColumn2And4
{
    Task<MessageContext> ProcessPhase2Async(MessageContext context);
    Task<MessageContext> ProcessPhase4Async(MessageContext context);
}

public interface IMessageValidatorColumn3
{
    Task<MessageContext> ProcessAsync(MessageContext context);
}

public interface INackHandlerColumn5
{
    Task<MessageContext> ProcessAsync(MessageContext context);
}

public interface IOutputHandlerColumn6
{
    Task<MessageContext> ProcessAsync(MessageContext context);
}

// Default implementations
public class EventHandlerColumn1 : IEventHandlerColumn1
{
    public async Task<MessageContext> ProcessAsync(MessageContext context, MarketMessage message)
    {
        context.AddStep("1A", "Ontvangen marktbericht");
        context.AddStep("1B", "Technische ontvangstbevestiging");

        try
        {
            if (!string.IsNullOrEmpty(message.XmlContent))
            {
                var doc = XDocument.Parse(message.XmlContent);
                context = context with { Xml = doc };
                context.AddStep("1C", "Technische validatie XML geslaagd");
            }
        }
        catch (Exception ex)
        {
            context.AddError("651", $"XML parsing failed: {ex.Message}", "1C");
        }

        context.AddStep("1D", "Logging van ontvangsttijd");
        context.AddStep("1E", $"Berichttype geïdentificeerd: {message.Type}");
        context.AddStep("1F", "Bereid verwerking voor");

        return await Task.FromResult(context);
    }
}

public class MessageProcessorColumn2And4 : IMessageProcessorColumn2And4
{
    public async Task<MessageContext> ProcessPhase2Async(MessageContext context)
    {
        context.AddStep("2A", $"Classificeer berichttype: {context.Type}");
        context.AddStep("2B", "Bepaal prioriteit: Normaal");
        context.AddStep("2C", "Plaats in wachtrij: Queue-OK");
        context.AddStep("2D", "Uitzonderingen geen gepark");
        context.AddStep("2E", "Geen herversending vereist");

        return await Task.FromResult(context);
    }

    public async Task<MessageContext> ProcessPhase4Async(MessageContext context)
    {
        var responseType = context.HasErrors ? ResponseType.Nack : ResponseType.Ack;
        context = context with { ResponseType = responseType };

        if (responseType == ResponseType.Ack)
        {
            context.AddStep("4A", "Genereer ACK-bericht");
        }
        else
        {
            context.AddStep("4A", "Genereer NACK-bericht");
            context.AddStep("4B", "Voeg foutcodes toe aan NACK");
        }

        context.AddStep("4C", "Voeg foutcodes toe");
        context.AddStep("4D", "Registreer validatieresultaat");
        context.AddStep("4E", "Configureer NACK-verzending");

        return await Task.FromResult(context);
    }
}

public class MessageValidatorColumn3 : IMessageValidatorColumn3
{
    public async Task<MessageContext> ProcessAsync(MessageContext context)
    {
        context.AddStep("3A", "Controleer BRP-register");

        // Sample BRP/EAN validation
        if (context.Xml?.Root?.Element("EAN") != null)
        {
            var ean = context.Xml.Root.Element("EAN")?.Value;
            if (string.IsNullOrEmpty(ean) || !System.Text.RegularExpressions.Regex.IsMatch(ean, @"^[0-9]{10,14}$"))
            {
                context.AddError("686", "Ongeldige EAN-code", "3A");
            }
        }

        context.AddStep("3B", "Voer marktbusiness-validaties uit");
        context.AddStep("3C", "Controleer verplichte velden");
        context.AddStep("3D", "Validatieregels zijn configureerbaar");
        context.AddStep("3E", "Controleer tijdvenster");

        if (context.ReceivedAt > DateTime.UtcNow.AddDays(1))
        {
            context.AddError("760", "Bericht in toekomst", "3E");
        }

        context.AddStep("3F", "Controleer volgordelijkheid");
        context.AddStep("3G", "Herbruikbare validatieregels");

        return await Task.FromResult(context);
    }
}

public class NackHandlerColumn5 : INackHandlerColumn5
{
    public async Task<MessageContext> ProcessAsync(MessageContext context)
    {
        context.AddStep("5A", "Verstuur ACK/NACK");
        context.AddStep("5B", "Geconfigureerde respons");
        context.AddStep("5C", "Logging verzendtijd");
        context.AddStep("5D", "Zelfstandige verzending");

        return await Task.FromResult(context);
    }
}

public class OutputHandlerColumn6 : IOutputHandlerColumn6
{
    public async Task<MessageContext> ProcessAsync(MessageContext context)
    {
        context.AddStep("6A", "Doorzetten naar raw-layer");
        context.AddStep("6B", "Registreer afleverstatus");

        return await Task.FromResult(context);
    }
}
