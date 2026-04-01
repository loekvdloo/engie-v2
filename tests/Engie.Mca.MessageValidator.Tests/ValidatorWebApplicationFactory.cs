using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Engie.Mca.MessageValidator.Tests;

public sealed class ValidatorWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubPhase4Handler(), disposeHandler: true)
            {
                BaseAddress = new Uri("http://stub.local")
            };
        }
    }

    private sealed class StubPhase4Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/processor/phase4")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(body);

                var messageId = GetStringProperty(doc.RootElement, "MessageId", "messageId") ?? "unknown";
                var correlationId = GetStringProperty(doc.RootElement, "CorrelationId", "correlationId") ?? "unknown";

                var hasErrors = false;
                if (doc.RootElement.TryGetProperty("HasErrors", out var pHasErrors))
                {
                    hasErrors = pHasErrors.ValueKind == JsonValueKind.True;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    messageId,
                    correlationId,
                    status = hasErrors ? "Failed" : "Delivered",
                    responseType = hasErrors ? "Nack" : "Ack",
                    errorCodes = hasErrors ? new[] { "686" } : Array.Empty<string>()
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"stub route not found\"}", Encoding.UTF8, "application/json")
            };
        }

        private static string? GetStringProperty(JsonElement element, string pascalName, string camelName)
        {
            if (element.TryGetProperty(pascalName, out var p1)) return p1.GetString();
            if (element.TryGetProperty(camelName, out var p2)) return p2.GetString();
            return null;
        }
    }
}
