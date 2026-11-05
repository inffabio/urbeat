using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.SystemParameters;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class AdminSystemParametersFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AdminSystemParametersFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BusinessSystemParametersEndpoints_ShouldRequireAdminRole()
    {
        await UpsertParameterAsync(
            "Store.DefaultDeliveryFee",
            "0",
            SystemParameterType.Decimal,
            "Business",
            "Taxa de entrega padrão");

        var anonymousClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        (await anonymousClient.GetAsync("/api/admin/system-parameters/business")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var sellerClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var sellerToken = await RegisterAndLoginSellerAsync(sellerClient);
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
        (await sellerClient.GetAsync("/api/admin/system-parameters/business")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var adminClient = await CreateAdminClientAsync();
        var response = await adminClient.GetAsync("/api/admin/system-parameters/business");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var parameters = await response.Content.ReadFromJsonAsync<List<SystemParameterDto>>();
        parameters.Should().NotBeNull();
        var fee = parameters!.Single(x => x.Key == "Store.DefaultDeliveryFee");
        fee.Display.Should().Be("currency");
        fee.MaxLength.Should().Be(18);
        fee.MinValue.Should().Be(0m);
        fee.MaxValue.Should().Be(1_000_000m);
        fee.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task BusinessList_ShouldNotReturnTechnicalParameters()
    {
        await UpsertParameterAsync("Store.DefaultDeliveryFee", "0", SystemParameterType.Decimal, "Business");
        await UpsertParameterAsync("Subscription.Description", "Assinatura urbeat", SystemParameterType.String, "Business");
        await UpsertParameterAsync("Jwt.ExpirationMinutes", "15", SystemParameterType.Int32, "Security");
        await UpsertParameterAsync("Email.SmtpPort", "587", SystemParameterType.Int32, "Email");
        await UpsertParameterAsync("Url.Asaas", "https://api.asaas.com", SystemParameterType.String, "Url");
        await UpsertParameterAsync("Upload.MaxFileSizeBytes", "5242880", SystemParameterType.Int32, "Upload");
        await UpsertParameterAsync("Cors.AllowAnyOrigin", "true", SystemParameterType.Boolean, "Cors");

        var client = await CreateAdminClientAsync();

        var listed = await client.GetFromJsonAsync<List<SystemParameterDto>>(
            "/api/admin/system-parameters/business");

        listed.Should().NotBeNull();
        listed!.Should().OnlyContain(x => BusinessSystemParameterCatalog.Contains(x.Key));
        listed.Should().Contain(x => x.Key == "Store.DefaultDeliveryFee");
        listed.Should().Contain(x => x.Key == "Subscription.Description");
        listed.Should().NotContain(x =>
            x.Key == "Jwt.ExpirationMinutes"
            || x.Key == "Email.SmtpPort"
            || x.Key == "Url.Asaas"
            || x.Key == "Upload.MaxFileSizeBytes"
            || x.Key == "Cors.AllowAnyOrigin");
    }

    [Theory]
    [InlineData("Jwt.ExpirationMinutes")]
    [InlineData("Email.SmtpPort")]
    [InlineData("Url.Asaas")]
    [InlineData("Upload.MaxFileSizeBytes")]
    [InlineData("Cors.AllowAnyOrigin")]
    [InlineData("Hangfire.WorkerCount")]
    [InlineData("Payment.Currency")]
    [InlineData("Cookie.Secure")]
    public async Task BusinessUpdate_ShouldRejectTechnicalKeysWithoutPersisting(string key)
    {
        await UpsertParameterAsync(key, "original", SystemParameterType.String, "Technical");

        var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/system-parameters/business/{key}",
            new
            {
                value = "changed",
                confirmRestrictedChange = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(response))
            .Should().Contain("not available in the business settings");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.SystemParameters.SingleAsync(x => x.Key == key);
        persisted.Value.Should().Be("original");
    }

    [Theory]
    [InlineData("Jwt.ExpirationMinutes")]
    [InlineData("Email.SmtpPort")]
    [InlineData("Url.Asaas")]
    public async Task BusinessGet_ShouldReturnNotFoundForTechnicalKeys(string key)
    {
        await UpsertParameterAsync(key, "value", SystemParameterType.String, "Technical");
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/admin/system-parameters/business/{key}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(response))
            .Should().Contain("not available in the business settings");
    }

    [Fact]
    public async Task BusinessUpdate_ShouldRejectFormattedDecimalAndPersistCanonicalValue()
    {
        await UpsertParameterAsync("Store.DefaultDeliveryFee", "0", SystemParameterType.Decimal, "Business");
        var client = await CreateAdminClientAsync();

        var invalidResponse = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Store.DefaultDeliveryFee",
            new
            {
                value = "R$ 19,90"
            });
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(invalidResponse)).Should().Contain("ponto");

        var validResponse = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Store.DefaultDeliveryFee",
            new
            {
                value = "19.90"
            });
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await validResponse.Content.ReadFromJsonAsync<SystemParameterDto>();
        updated.Should().NotBeNull();
        updated!.Value.Should().Be("19.90");

        var reloaded = await client.GetFromJsonAsync<SystemParameterDto>(
            "/api/admin/system-parameters/business/Store.DefaultDeliveryFee");
        reloaded!.Value.Should().Be("19.90");

        var cached = await _factory.Services.GetRequiredService<ISystemParameterService>()
            .GetValueAsync("Store.DefaultDeliveryFee");
        cached.Should().Be("19.90");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AuditLogs
            .Where(x => x.Event == "SystemParameterUpdated"
                && x.Description.Contains("Store.DefaultDeliveryFee"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.Description.Should().NotContain("19.90");
    }

    [Fact]
    public async Task BusinessUpdate_ShouldRejectInvalidValuesWithSpecificMessages()
    {
        await UpsertParameterAsync("Order.CodeLength", "8", SystemParameterType.Int32, "Business");
        await UpsertParameterAsync("Customer.MaxAddresses", "3", SystemParameterType.Int32, "Business");
        await UpsertParameterAsync("Order.CodePrefix", "URB-", SystemParameterType.String, "Business");

        var client = await CreateAdminClientAsync();

        var invalidInt = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Order.CodeLength",
            new
            {
                value = "8.5"
            });
        invalidInt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(invalidInt)).Should().Contain("inteiro");

        var outOfRange = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Customer.MaxAddresses",
            new
            {
                value = "0"
            });
        outOfRange.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(outOfRange)).Should().Contain("entre 1 e 50");

        var overMaxLength = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Order.CodePrefix",
            new
            {
                value = new string('A', 21)
            });
        overMaxLength.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(overMaxLength)).Should().Contain("20");
    }

    [Fact]
    public async Task BusinessUpdate_ShouldReturnNotFoundForUnknownKey()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/admin/system-parameters/business/Unknown.Parameter",
            new
            {
                value = "value"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await LoginAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            return error.GetString() ?? string.Empty;
        }

        if (document.RootElement.TryGetProperty("detail", out var detail))
        {
            return detail.GetString() ?? string.Empty;
        }

        if (document.RootElement.TryGetProperty("title", out var title))
        {
            return title.GetString() ?? body;
        }

        return body;
    }

    private async Task UpsertParameterAsync(
        string key,
        string value,
        SystemParameterType type,
        string group,
        string? description = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.SystemParameters.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null)
        {
            db.SystemParameters.Add(new SystemParameter
            {
                Key = key,
                Value = value,
                Type = type,
                Group = group,
                Description = description
            });
        }
        else
        {
            existing.Value = value;
            existing.Type = type;
            existing.Group = group;
            existing.Description = description;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> LoginAdminAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }

    private async Task<string> RegisterAndLoginSellerAsync(HttpClient client)
    {
        var email = $"admin.params.seller.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Admin Params Seller",
            Email = email,
            Password = password,
            PhoneNumber = "11987776666"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }
}
