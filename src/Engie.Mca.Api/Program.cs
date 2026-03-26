using Engie.Mca.Api.Services;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Configure console and file logging
var logsDirectory = Path.Combine(@"c:\Users\loek\engie\engie-v2", "logs");
Directory.CreateDirectory(logsDirectory);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .Enrich.WithProperty("BlockCode", "api")
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logsDirectory, "pipeline-.log"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register services
builder.Services.AddSingleton<MessageStore>();

builder.Services.AddHttpClient();
builder.Services.AddScoped<IMicroserviceOrchestrator, MicroserviceOrchestrator>();
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
    using var pathScope = LogContext.PushProperty("RequestPath", httpContext.Request.Path.Value ?? string.Empty);
    await next();
});

app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
