

using Serilog;
using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Context;
using Engie.Mca.EventHandler.Services;

var builder = WebApplication.CreateBuilder(args);
// Configure Serilog
var logsDirectory = Path.Combine(@"c:\Users\loek\engie\engie-v2", "logs", "blocks");
Directory.CreateDirectory(logsDirectory);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
        .MinimumLevel.Override("System", LogEventLevel.Error)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("BlockCode", "eh")
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(logsDirectory, "block1-event-handler-.log"),
                rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(logsDirectory, "all-blocks-.log"),
            rollingInterval: RollingInterval.Day,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}")
);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MessageStore>();

var app = builder.Build();

app.Use(async (httpContext, next) =>
{
    var correlationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = httpContext.TraceIdentifier;
    }

    httpContext.TraceIdentifier = correlationId;
    httpContext.Response.Headers["X-Correlation-ID"] = correlationId;

    using var correlationScope = LogContext.PushProperty("CorrelationId", correlationId);
    await next();
});

app.UseAuthorization();
app.MapControllers();
app.Run("http://localhost:5001");

public partial class Program;
