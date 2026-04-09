
using Engie.Mca.Common.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using Engie.Mca.EventHandler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddEngieServiceDefaults("eh", "block1-event-handler-.log");
builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSingleton<MetricsAggregator>(
    sp => new MetricsAggregator(Environment.GetEnvironmentVariable("REDIS_URL")));

var app = builder.Build();
app.UseEngieServiceDefaults();
app.Run();

public partial class Program;
