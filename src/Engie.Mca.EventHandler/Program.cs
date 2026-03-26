

using Serilog;
using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
// Configure Serilog
var logsDirectory = Path.Combine(@"c:\Users\loek\engie\engie-v2", "logs", "blocks");
Directory.CreateDirectory(logsDirectory);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
        .MinimumLevel.Override("System", LogEventLevel.Error)
        .Enrich.WithProperty("BlockCode", "eh")
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(logsDirectory, "block1-event-handler-.log"),
                rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(logsDirectory, "all-blocks-.log"),
            rollingInterval: RollingInterval.Day,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{BlockCode}] {Message:lj}{NewLine}{Exception}")
);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();
app.Run("http://localhost:5001");
