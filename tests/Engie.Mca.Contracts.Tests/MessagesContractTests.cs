using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Engie.Mca.Contracts.Tests;

public sealed class MessagesContractTests : IClassFixture<ContractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MessagesContractTests(ContractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostMessages_ReturnsExpectedResponseContract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages")
        {
            Content = JsonContent.Create(new
            {
                id = Guid.NewGuid().ToString(),
                type = "mma.msg.new",
                createtime = DateTime.UtcNow.ToString("o"),
                source = "ENTEM",
                msgsender = "8712423009196",
                msgsenderrole = "DDK",
                msgreceiver = "8716867999990",
                msgreceiverrole = "DDM",
                msgtype = "AllocationServiceNotification",
                msgsubtype = "N101",
                msgid = "contract-001",
                msgcorrelationid = "contract-correlation-001",
                msgcreationtime = DateTime.UtcNow.ToString("o"),
                msgversion = "1.2",
                msgpayloadid = Guid.NewGuid().ToString(),
                msgcontenttype = "application/xml",
                msgpayload = "<AllocationSeries><EAN>8712345678901</EAN><DocumentID>DOC-001</DocumentID><Quantity>10</Quantity></AllocationSeries>",
                entemsendacknowledgement = true,
                entemsendtooutput = true,
                entemvalidationresult = Array.Empty<object>(),
                entemtimestamp = DateTime.UtcNow.ToString("o")
            })
        };
        request.Headers.Add("X-Correlation-ID", "contract-correlation-001");

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<MessageResponseContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("contract-001", payload.MessageId);
        Assert.Equal("contract-correlation-001", payload.CorrelationId);
        Assert.Equal("Delivered", payload.Status);
        Assert.Equal("Ack", payload.ResponseType);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Contains("contract-correlation-001", values!);
    }

    [Fact]
    public async Task GetMessageStatus_ReturnsStoredMessageContract()
    {
        await _client.PostAsJsonAsync("/api/messages", new
        {
            id = Guid.NewGuid().ToString(),
            type = "mma.msg.new",
            source = "ENTEM",
            msgtype = "AllocationServiceNotification",
            msgsubtype = "N101",
            msgid = "contract-002",
            msgcorrelationid = "contract-correlation-002",
            msgpayload = "<AllocationSeries><EAN>8712345678901</EAN><DocumentID>DOC-002</DocumentID><Quantity>25</Quantity></AllocationSeries>",
            entemsendacknowledgement = true,
            entemsendtooutput = true,
            entemvalidationresult = Array.Empty<object>()
        });

        var response = await _client.GetAsync("/api/messages/contract-002");
        var payload = await response.Content.ReadFromJsonAsync<MessageStatusContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("contract-002", payload.MessageId);
        Assert.Equal("contract-correlation-002", payload.CorrelationId);
        Assert.Equal("Delivered", payload.Status);
        Assert.NotNull(payload.ProcessingDurationMs);
        Assert.NotEmpty(payload.Steps);
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsAggregateMetricsContract()
    {
        await _client.PostAsJsonAsync("/api/messages", new
        {
            id = Guid.NewGuid().ToString(),
            type = "mma.msg.new",
            source = "ENTEM",
            msgtype = "AllocationServiceNotification",
            msgsubtype = "N101",
            msgid = "contract-003",
            msgcorrelationid = "contract-correlation-003",
            msgpayload = "<AllocationSeries><EAN>8712345678901</EAN><DocumentID>DOC-003</DocumentID><Quantity>25</Quantity></AllocationSeries>",
            entemsendacknowledgement = true,
            entemsendtooutput = true,
            entemvalidationresult = Array.Empty<object>()
        });

        var response = await _client.GetAsync("/api/metrics");
        var payload = await response.Content.ReadFromJsonAsync<MetricsContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.TotalMessages >= 1);
        Assert.True(payload.DeliveredMessages >= 1);
        Assert.True(payload.AverageProcessingDurationMs >= 0);
        Assert.Contains(payload.MessagesByStatus, metric => metric.Status == "Delivered");
    }

    private sealed record MessageResponseContract(
        string MessageId,
        string CorrelationId,
        string Status,
        string ResponseType,
        int ErrorCount,
        List<string> ErrorCodes);

    private sealed record MessageStatusContract(
        string MessageId,
        string CorrelationId,
        string Status,
        string? ResponseType,
        int ErrorCount,
        List<string> ErrorCodes,
        List<StepContract> Steps,
        DateTime ReceivedAt,
        DateTime? ProcessedAt,
        double? ProcessingDurationMs);

    private sealed record StepContract(string StepId, string Description);

    private sealed record MetricsContract(
        int TotalMessages,
        int DeliveredMessages,
        int FailedMessages,
        int AckMessages,
        int NackMessages,
        decimal SuccessRate,
        double AverageProcessingDurationMs,
        double P95ProcessingDurationMs,
        int TotalErrors,
        DateTime? LastProcessedAt,
        List<StatusMetricContract> MessagesByStatus,
        List<TypeMetricContract> MessagesByType,
        List<ErrorMetricContract> ErrorsByCode);

    private sealed record StatusMetricContract(string Status, int Count);

    private sealed record TypeMetricContract(string Type, int Count);

    private sealed record ErrorMetricContract(string Code, int Count);
}