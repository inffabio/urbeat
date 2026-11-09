using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class WhatsAppWebhookFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string AppSecret = "test-whatsapp-app-secret";
    private const string VerifyToken = "test-whatsapp-verify-token";

    private readonly TestWebApplicationFactory _factory;

    public WhatsAppWebhookFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Verify_WithCorrectTokenAndMode_ShouldReturnChallengeOnly()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            $"/api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(VerifyToken)}&hub.challenge=challenge-123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("challenge-123");
    }

    [Fact]
    public async Task Verify_WithWrongToken_ShouldReturnForbiddenWithoutDetails()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            "/api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=challenge-123");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(VerifyToken);
        body.Should().NotContain("challenge-123");
    }

    [Fact]
    public async Task Verify_WithWrongMode_ShouldReturnForbidden()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            $"/api/webhooks/whatsapp?hub.mode=unsubscribe&hub.verify_token={Uri.EscapeDataString(VerifyToken)}&hub.challenge=challenge-123");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_WithoutSignature_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var payload = MessagePayload("wamid.integration.unsigned");

        var response = await client.PostAsync("/api/webhooks/whatsapp", JsonContent(payload));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountEventsAsync("meta:message:wamid.integration.unsigned")).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithInvalidSignature_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var payload = MessagePayload("wamid.integration.badsignature");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
        {
            Content = JsonContent(payload)
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('a', 64));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountEventsAsync("meta:message:wamid.integration.badsignature")).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithValidSignature_ShouldPersistOnceAndAcknowledgeRetries()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var payload = MessagePayload("wamid.integration.valid");
        var signature = ComputeSignature(payload);

        var first = await SendSignedAsync(client, payload, signature);
        var second = await SendSignedAsync(client, payload, signature);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountEventsAsync("meta:message:wamid.integration.valid")).Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.WhatsAppWebhookEvents
            .AsNoTracking()
            .SingleAsync(x => x.EventKey == "meta:message:wamid.integration.valid");
        stored.Provider.Should().Be("meta");
        stored.MessageId.Should().Be("wamid.integration.valid");
        stored.Payload.Should().Be(payload);
    }

    [Fact]
    public async Task Post_WithValidSignatureFromAnotherPhoneNumber_ShouldBeAcknowledgedAndIgnored()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var payload = MessagePayload("wamid.integration.foreign", phoneNumberId: "999999999999999");
        var signature = ComputeSignature(payload);

        var response = await SendSignedAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountEventsAsync("meta:message:wamid.integration.foreign")).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithValidSignatureButInvalidJson_ShouldReturnBadRequest()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        const string payload = "not-json";
        var signature = ComputeSignature(payload);

        var response = await SendSignedAsync(client, payload, signature);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<int> CountEventsAsync(string eventKey)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.WhatsAppWebhookEvents.CountAsync(x => x.EventKey == eventKey);
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        string payload,
        string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
        {
            Content = JsonContent(payload)
        };
        request.Headers.Add("X-Hub-Signature-256", signature);
        return await client.SendAsync(request);
    }

    private static HttpContent JsonContent(string payload)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return content;
    }

    private static string ComputeSignature(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    private static string MessagePayload(string messageId, string phoneNumberId = "1436364406224525") => $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [
        {
          "id": "waba-integration",
          "changes": [
            {
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "phone_number_id": "{{phoneNumberId}}" },
                "messages": [
                  { "from": "5511999999999", "id": "{{messageId}}", "timestamp": "1730000000", "type": "text", "text": { "body": "oi" } }
                ]
              }
            }
          ]
        }
      ]
    }
    """;
}
