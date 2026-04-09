using Engie.Mca.Common.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using System.IO;
using System.Linq;

namespace Engie.Mca.Common.Hosting;

public static class EngieServiceHostExtensions
{
    private const string ConsoleOutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}";
    private const string FileOutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] [{CorrelationId}] [msg:{MessageId}] [type:{MessageType}] [resp:{ResponseType}] [codes:{ErrorCodes}] {Message:lj}{NewLine}{Exception}";

    public static WebApplicationBuilder AddEngieServiceDefaults(this WebApplicationBuilder builder, string blockCode, string blockLogFileName)
    {
        var logsDirectory = RuntimeSettings.GetLogsDirectory();
        Directory.CreateDirectory(logsDirectory);

        builder.Host.UseSerilog((context, services, configuration) =>
            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
                .MinimumLevel.Override("System", LogEventLevel.Error)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("BlockCode", blockCode)
                .WriteTo.Console(outputTemplate: ConsoleOutputTemplate)
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, blockLogFileName),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: FileOutputTemplate)
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, "all-blocks-.log"),
                    rollingInterval: RollingInterval.Day,
                    shared: true,
                    outputTemplate: FileOutputTemplate));

        builder.Services.AddControllers();
        builder.Services.AddHttpClient();

        return builder;
    }

    public static WebApplication UseEngieServiceDefaults(this WebApplication app)
    {
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

        return app;
    }
}