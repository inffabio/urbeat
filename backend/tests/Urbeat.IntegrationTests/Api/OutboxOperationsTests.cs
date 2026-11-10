using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Urbeat.IntegrationTests.Api;

public sealed class OutboxOperationsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Issuer = "urbeat";
    private const string Audience = "urbeat-api";
    private const string Secret = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";

    private readonly TestWebApplicationFactory _factory;

    public OutboxOperationsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSummary_ShouldReturnUnauthorized_WhenNoToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/outbox/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnForbidden_WhenNonAdminToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken("Seller"));

        var response = await client.GetAsync("/api/outbox/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnCounts_WhenAdminToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken("Admin"));

        var response = await client.GetAsync("/api/outbox/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pending").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("failed").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("deliveries").GetProperty("business").GetProperty("pending").GetInt32()
            .Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("deliveries").GetProperty("communications").GetProperty("failed").GetInt32()
            .Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetOperations_ShouldReturnUnauthorized_WhenNoToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/operations/outbox");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOperations_ShouldReturnForbidden_WhenNonAdminToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken("Seller"));

        var response = await client.GetAsync("/api/admin/operations/outbox");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetOperations_ShouldReturnSanitizedProjection_WhenAdminToken()
    {
        var now = DateTime.UtcNow;
        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            Payload = "{\"recipient\":\"sensitive\",\"orderId\":\"sensitive\"}",
            OccurredAtUtc = now.AddMinutes(-10),
            AvailableAtUtc = now.AddMinutes(-10),
            Status = OutboxMessageStatus.Pending,
            LastError = "provider unavailable for sensitive recipient"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OutboxMessages.Add(message);
            db.OutboxHandlerDeliveries.AddRange(
                new OutboxHandlerDelivery
                {
                    OutboxMessageId = message.Id,
                    HandlerKey = "business.notification",
                    Channel = OutboxChannel.Business,
                    Status = OutboxHandlerDeliveryStatus.Pending,
                    AvailableAtUtc = now.AddMinutes(-9),
                    CreatedAtUtc = now.AddMinutes(-9),
                    LastError = "sensitive business error"
                },
                new OutboxHandlerDelivery
                {
                    OutboxMessageId = message.Id,
                    HandlerKey = "communications.email",
                    Channel = OutboxChannel.Communications,
                    Status = OutboxHandlerDeliveryStatus.Failed,
                    AttemptCount = 3,
                    AvailableAtUtc = now.AddMinutes(-8),
                    CreatedAtUtc = now.AddMinutes(-8),
                    LastError = "sensitive communications error"
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken("Admin"));

        var response = await client.GetAsync("/api/admin/operations/outbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("sensitive");
        raw.Should().NotContain("provider unavailable");
        raw.Should().NotContain("sensitive business error");
        raw.Should().NotContain("sensitive communications error");

        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("backlogThreshold").GetInt32().Should().BeGreaterThan(0);

        var business = body.GetProperty("business");
        business.GetProperty("channel").GetString().Should().Be("Business");
        business.GetProperty("pending").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        business.GetProperty("oldestBacklogAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        business.GetProperty("oldestBacklogAgeSeconds").GetInt64().Should().BeGreaterThanOrEqualTo(0);

        var communications = body.GetProperty("communications");
        communications.GetProperty("channel").GetString().Should().Be("Communications");
        communications.GetProperty("failed").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var messages = body.GetProperty("messages");
        messages.GetProperty("pending").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        messages.GetProperty("oldestBacklogAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        var workers = body.GetProperty("workers");
        workers.GetProperty("available").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        workers.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        workers.GetProperty("workers").ValueKind.Should().Be(JsonValueKind.Array);

        var dependencyNames = body.GetProperty("dependencies")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();

        dependencyNames.Should().Contain(new[] { "api", "postgresql", "redis" });
    }

    [Fact]
    public async Task Retry_ShouldRequeueOnlyFailedDeliveries_AndKeepSiblingsProcessed()
    {
        var message = await SeedFailedMessageAsync();
        var processedDeliveryId = Guid.NewGuid();
        var failedDeliveryId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OutboxHandlerDeliveries.AddRange(
                new OutboxHandlerDelivery
                {
                    Id = processedDeliveryId,
                    OutboxMessageId = message.Id,
                    HandlerKey = "business.notification",
                    Channel = OutboxChannel.Business,
                    Status = OutboxHandlerDeliveryStatus.Processed,
                    AvailableAtUtc = DateTime.UtcNow,
                    ProcessedAtUtc = DateTime.UtcNow
                },
                new OutboxHandlerDelivery
                {
                    Id = failedDeliveryId,
                    OutboxMessageId = message.Id,
                    HandlerKey = "communications.email",
                    Channel = OutboxChannel.Communications,
                    Status = OutboxHandlerDeliveryStatus.Failed,
                    AttemptCount = 4,
                    AvailableAtUtc = DateTime.UtcNow,
                    LastError = "invalid recipient"
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken("Admin"));

        var response = await client.PostAsync($"/api/outbox/{message.Id}/retry", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var processed = await db.OutboxHandlerDeliveries.SingleAsync(x => x.Id == processedDeliveryId);
            var failed = await db.OutboxHandlerDeliveries.SingleAsync(x => x.Id == failedDeliveryId);

            processed.Status.Should().Be(OutboxHandlerDeliveryStatus.Processed);
            failed.Status.Should().Be(OutboxHandlerDeliveryStatus.Pending);
            failed.AttemptCount.Should().Be(0);
            failed.LastError.Should().BeNull();
        }
    }

    [Fact]
    public async Task Retry_ShouldRequeueFailedMessage_AndNotExposePayload()
    {
        var message = await SeedFailedMessageAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken("Admin"));

        var retryResponse = await client.PostAsync($"/api/outbox/{message.Id}/retry", null);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var failed = await client.GetFromJsonAsync<JsonElement>("/api/outbox/failed");
        var items = failed.GetProperty("items");
        items.GetArrayLength().Should().Be(0);

        var summary = await client.GetFromJsonAsync<JsonElement>("/api/outbox/summary");
        summary.GetProperty("pending").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // The failed listing must never expose payload contents.
        var raw = await (await client.GetAsync("/api/outbox/failed")).Content.ReadAsStringAsync();
        raw.Should().NotContain("sensitive", "the operations endpoint must not expose outbox payloads");
    }

    private async Task<OutboxMessage> SeedFailedMessageAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            AggregateId = Guid.NewGuid(),
            Payload = "{\"orderId\":\"sensitive\"}",
            OccurredAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            Status = OutboxMessageStatus.Failed,
            LastError = "provider unavailable"
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private static string BuildToken(string role)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "outbox-admin@urbeat.local"),
            new(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
