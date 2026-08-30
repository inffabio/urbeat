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
        summary.GetProperty("pending").GetInt32().Should().Be(1);

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
