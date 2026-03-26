using Engie.Mca.Api.Models;
using Engie.Mca.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Engie.Mca.Contracts.Tests;

public sealed class ContractWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMicroserviceOrchestrator>();
            services.RemoveAll<MessageStore>();

            services.AddSingleton<MessageStore>();
            services.AddScoped<IMicroserviceOrchestrator, FakeOrchestrator>();
        });
    }
}

internal sealed class FakeOrchestrator : IMicroserviceOrchestrator
{
    public Task<MessageContext> ProcessAsync(MarketMessage message, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var status = ProcessingStatus.Delivered;
        var responseType = ResponseType.Ack;

        if (message.XmlContent?.Contains("<EAN></EAN>", StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add(new ValidationError("686", "Ongeldige EAN-code", "3A"));
            status = ProcessingStatus.Failed;
            responseType = ResponseType.Nack;
        }

        return Task.FromResult(new MessageContext(
            message.MessageId,
            message.CorrelationId,
            message.Type,
            status,
            responseType,
            null,
            errors,
            new List<(string StepId, string Description)>
            {
                ("1A", "Ontvang event"),
                ("2A", "Classificeer berichttype"),
                ("3A", "Controleer EAN-code")
            },
            message.ReceivedAt,
            message.ReceivedAt.AddMilliseconds(25),
            25));
    }
}