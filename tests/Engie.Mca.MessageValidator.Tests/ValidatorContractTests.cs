using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Engie.Mca.MessageValidator.Tests;

public sealed class ValidatorContractTests : IClassFixture<ValidatorWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ValidatorContractTests(ValidatorWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostValidateEndpoint_AcceptsExistingPayload()
    {
        var now = DateTime.UtcNow;

        var response = await _client.PostAsJsonAsync("/api/validator/validate", new
        {
            messageId = "validator-contract-001",
            correlationId = "corr-001",
            messageType = "AllocationSeries",
            eanCode = "123456789012345678",
            documentId = "DOC-001",
            quantity = 42.5m,
            startDateTime = now.AddMinutes(-5),
            endDateTime = now.AddMinutes(10),
            content = "<AllocationSeries><DocumentID>DOC-001</DocumentID></AllocationSeries>"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostRootEndpoint_AcceptsAliasPayload()
    {
        var now = DateTime.UtcNow;

        var response = await _client.PostAsJsonAsync("/api/validator", new
        {
            messageId = "validator-contract-002",
            correlationId = "corr-002",
            messageType = "AllocationSeries",
            ean = "123456789012345678",
            documentID = "DOC-002",
            quantity = 12.0m,
            startDateTime = now.AddMinutes(-3),
            endDateTime = now.AddMinutes(20),
            xmlContent = "<AllocationSeries><DocumentID>DOC-002</DocumentID></AllocationSeries>"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
