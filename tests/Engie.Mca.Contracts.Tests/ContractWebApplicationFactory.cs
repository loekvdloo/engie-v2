using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Engie.Mca.Contracts.Tests;

public sealed class ContractWebApplicationFactory : WebApplicationFactory<Program>
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
			return new HttpClient(new StubChainHandler(), disposeHandler: true)
			{
				BaseAddress = new Uri("http://stub.local")
			};
		}
	}

	private sealed class StubChainHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri?.AbsolutePath == "/api/processor/phase2")
			{
				var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
				var requestDoc = JsonDocument.Parse(requestJson);

				var messageId = GetStringProperty(requestDoc.RootElement, "MessageId", "messageId") ?? "unknown";
				var correlationId = GetStringProperty(requestDoc.RootElement, "CorrelationId", "correlationId") ?? "unknown";

				var payload = JsonSerializer.Serialize(new
				{
					messageId,
					correlationId,
					status = "Delivered",
					responseType = "Ack",
					errorCount = 0,
					errorCodes = Array.Empty<string>()
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
			if (element.TryGetProperty(pascalName, out var pascal))
			{
				return pascal.GetString();
			}

			if (element.TryGetProperty(camelName, out var camel))
			{
				return camel.GetString();
			}

			return null;
		}
	}
}