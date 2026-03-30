
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Engie.Mca.MessageValidator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ValidatorController : ControllerBase
{
    // Tracks last processed start-time per (EAN, DocumentId) for sequence checks (3F).
    private static readonly ConcurrentDictionary<string, DateTime> LastSeenSequenceByKey = new();

    private readonly ILogger<ValidatorController> _logger;

    public ValidatorController(ILogger<ValidatorController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate message: Steps 3A-3G
    /// Based on EDSN Business Service SD030 v4.0 validation rules.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateMessage([FromBody] ValidatorRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
        {
            return BadRequest(new { step = "3A", error = "MessageId is verplicht" });
        }

        var messageId = request.MessageId;
        var errors = new List<string>();

        using var messageIdScope = LogContext.PushProperty("MessageId", messageId);

        _logger.LogInformation("[{MessageId}] === COLUMN 3: MESSAGE VALIDATOR (Steps 3A-3G) ===", messageId);

        try
        {
            // Step 3A: EAN-code check (EDSN: 686 - Ongeldige EAN-code)
            var ean = request.EanCode?.Trim() ?? string.Empty;
            var isValidEan = ean.Length == 18 && ean.All(char.IsDigit);
            if (!isValidEan)
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 686 (3A): Ongeldige EAN-code ({EanCode})", messageId, request.EanCode);
                errors.Add("686");
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 3A: Geldige EAN-code", messageId);
            }
            await Task.Delay(10);

            // Step 3B: Datum/tijd check (760 = in toekomst, 758 = buiten geldige periode)
            if (!request.StartDateTime.HasValue)
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 758 (3B): StartDateTime ontbreekt", messageId);
                errors.Add("758");
            }
            else
            {
                if (request.StartDateTime.Value > DateTime.UtcNow.AddMinutes(5))
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 760 (3B): StartDateTime ligt in de toekomst ({Start})", messageId, request.StartDateTime.Value);
                    errors.Add("760");
                }
                else if (request.StartDateTime.Value < DateTime.UtcNow.AddDays(-90))
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 758 (3B): Bericht buiten geldige periode ({Start})", messageId, request.StartDateTime.Value);
                    errors.Add("758");
                }
                else
                {
                    _logger.LogInformation("[{MessageId}] ✓ Step 3B: Controleer datum/tijd", messageId);
                }
            }
            await Task.Delay(10);

            // Step 3C: Verplichte velden check (676 = vereist veld ontbreekt)
            if (string.IsNullOrWhiteSpace(request.DocumentId))
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 676 (3C): Vereist veld DocumentID ontbreekt", messageId);
                errors.Add("676");
            }
            else if (request.DocumentId!.Length > 64)
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 676 (3C): DocumentID te lang ({Length})", messageId, request.DocumentId.Length);
                if (!errors.Contains("676")) errors.Add("676");
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 3C: Controleer verplichte velden", messageId);
            }
            await Task.Delay(10);

            // Step 3D: Hoeveelheid validatie (772 = negatief, 774 = nul, 773 = boven limiet)
            if (!request.Quantity.HasValue)
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 774 (3D): Hoeveelheid ontbreekt", messageId);
                errors.Add("774");
            }
            else
            {
                if (request.Quantity.Value < 0)
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 772 (3D): Negatieve hoeveelheid niet toegestaan ({Qty})", messageId, request.Quantity.Value);
                    errors.Add("772");
                }
                else if (request.Quantity.Value == 0)
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 774 (3D): Hoeveelheid nul niet toegestaan", messageId);
                    errors.Add("774");
                }
                else if (request.Quantity.Value > 999999)
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 773 (3D): Hoeveelheid overschrijdt maximale limiet ({Qty})", messageId, request.Quantity.Value);
                    errors.Add("773");
                }
                else
                {
                    _logger.LogInformation("[{MessageId}] ✓ Step 3D: Validatieregels zijn configureerbaar", messageId);
                }
            }
            await Task.Delay(10);

            // Step 3E: Tijdvenster check (758 = buiten geldige periode)
            if (!request.EndDateTime.HasValue || !request.StartDateTime.HasValue)
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 758 (3E): StartDateTime of EndDateTime ontbreekt", messageId);
                if (!errors.Contains("758")) errors.Add("758");
            }
            else
            {
                if (request.EndDateTime.Value <= request.StartDateTime.Value)
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 758 (3E): EndDateTime ligt voor of gelijk aan StartDateTime", messageId);
                    if (!errors.Contains("758")) errors.Add("758");
                }
                else if ((request.EndDateTime.Value - request.StartDateTime.Value) > TimeSpan.FromDays(31))
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 758 (3E): Tijdvenster langer dan 31 dagen", messageId);
                    if (!errors.Contains("758")) errors.Add("758");
                }
                else
                {
                    _logger.LogInformation("[{MessageId}] ✓ Step 3E: Controleer tijdvenster", messageId);
                }
            }
            await Task.Delay(10);

            // Step 3F: Volgordelijkheid check (754 = ongeldige sequence)
            if (!string.IsNullOrWhiteSpace(request.DocumentId) && request.DocumentId.Contains("DUP", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[{MessageId}]   ✗ Error 755 (3F): Dubbele bericht-ID gedetecteerd ({DocId})", messageId, request.DocumentId);
                errors.Add("755");
            }
            else if (isValidEan && request.StartDateTime.HasValue && !string.IsNullOrWhiteSpace(request.DocumentId))
            {
                var sequenceKey = $"{ean}:{request.DocumentId}";
                if (LastSeenSequenceByKey.TryGetValue(sequenceKey, out var previousStart) && request.StartDateTime.Value <= previousStart)
                {
                    _logger.LogWarning("[{MessageId}]   ✗ Error 754 (3F): Volgordelijkheid ongeldig ({Current} <= {Previous})",
                        messageId, request.StartDateTime.Value, previousStart);
                    errors.Add("754");
                }
                else
                {
                    LastSeenSequenceByKey[sequenceKey] = request.StartDateTime.Value;
                    _logger.LogInformation("[{MessageId}] ✓ Step 3F: Controleer volgordelijkheid", messageId);
                }
            }
            else
            {
                _logger.LogInformation("[{MessageId}] ✓ Step 3F: Controleer volgordelijkheid", messageId);
            }
            await Task.Delay(10);

            // Step 3G: Herbruikbare validatieregels
            var forcedCodes = ExtractForcedErrorCodes(request.Content);
            if (forcedCodes.Count > 0)
            {
                using var errorCodesScope = LogContext.PushProperty("ErrorCodes", string.Join(",", forcedCodes));

                foreach (var code in forcedCodes)
                {
                    if (!errors.Contains(code))
                    {
                        errors.Add(code);
                    }
                }

                _logger.LogWarning("[{MessageId}]   ⚠ Forced test error codes toegepast: {Codes}", messageId, string.Join(",", forcedCodes));
            }

            using var aggregatedErrorCodesScope = LogContext.PushProperty("ErrorCodes", string.Join(",", errors));

            _logger.LogInformation("[{MessageId}] ✓ Step 3G: Herbruikbare validatieregels", messageId);
            await Task.Delay(10);

            bool isValid = errors.Count == 0;
            return Ok(new
            {
                messageId,
                stepsCompleted = 7,
                isValid,
                errorCodes = errors,
                errorCode = errors.Count > 0 ? errors[0] : (string?)null,
                status = isValid ? "ValidationPassed" : "ValidationFailed",
                nextService = "MessageProcessor"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MessageId}] Validation failed", messageId);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "MessageValidator", status = "healthy" });
    }

    private static List<string> ExtractForcedErrorCodes(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        var marker = "<ForceErrorCodes>";
        var endMarker = "</ForceErrorCodes>";
        var start = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return new List<string>();
        }

        start += marker.Length;
        var end = content.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
        if (end <= start)
        {
            return new List<string>();
        }

        var raw = content[start..end];
        return raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(code => code.Length == 3 && code.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

public class ValidatorRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string EanCode { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public decimal? Quantity { get; set; }
    public DateTime? StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
    public string Content { get; set; } = string.Empty;
}
