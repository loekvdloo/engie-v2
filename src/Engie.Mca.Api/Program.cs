using Engie.Mca.Api.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure console and file logging
var logsDirectory = Path.Combine(@"c:\Users\loek\engie\engie-v2", "logs");
Directory.CreateDirectory(logsDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDirectory, "pipeline-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register services
builder.Services.AddSingleton<MessageStore>();

builder.Services.AddHttpClient();
builder.Services.AddScoped<MicroserviceOrchestrator>();
var app = builder.Build();

app.UseAuthorization();
app.MapControllers();

app.Run();
