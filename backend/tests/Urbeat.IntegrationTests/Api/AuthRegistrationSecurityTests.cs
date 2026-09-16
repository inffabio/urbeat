using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Urbeat.IntegrationTests.Api;

public sealed class AuthRegistrationSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthRegistrationSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterSeller_ShouldNotPromote_WhenExistingAccountPasswordDoesNotMatch()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"takeover.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";
        const string attackerPassword = "SenhaErrada123";

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Vitima Takeover",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        var promoteResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Atacante Takeover",
            Email = email,
            Password = attackerPassword,
            PhoneNumber = "11966666666"
        });

        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        (await userManager.IsInRoleAsync(user!, "Seller")).Should().BeFalse();
        var claims = await userManager.GetClaimsAsync(user!);
        claims.Should().NotContain(claim => claim.Type == "FullName" && claim.Value == "Atacante Takeover");
    }

    [Fact]
    public async Task RegisterSeller_ShouldNotIssueEmailChangeChallenge_WhenPromotingConfirmedAccount()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"confirmed.promote.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Confirmado",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        var promoteResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Contratante Confirmado {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });

        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await promoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("emailConfirmationPending").GetBoolean().Should().BeFalse();
        payload.TryGetProperty("emailChangeChallenge", out var challenge).Should().BeTrue();
        challenge.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RegisterSeller_ShouldIssueEmailChangeChallenge_WhenPromotingUnconfirmedAccount()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = $"pending.promote.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var customerResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Pendente",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var promoteResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Contratante Pendente {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11977777777"
        });

        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await promoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("emailConfirmationPending").GetBoolean().Should().BeTrue();
        payload.GetProperty("emailChangeChallenge").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RegisterCustomer_ShouldIssueEmailChangeChallenge_ForPendingDocument_WhenPasswordMatches()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var document = ValidCpf();
        var firstEmail = $"document.pending.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Pessoa Documento",
            Email = firstEmail,
            Password = password,
            PhoneNumber = "11977777777",
            Document = document
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Pessoa Documento Dois",
            Email = $"document.pending.new.{Guid.NewGuid():N}@urbeat.local",
            Password = password,
            PhoneNumber = "11966666666",
            Document = document
        });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("documentAlreadyRegistered").GetBoolean().Should().BeTrue();
        payload.GetProperty("emailConfirmationPending").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("emailChangeChallenge", out var challenge).Should().BeTrue();
        challenge.ValueKind.Should().Be(JsonValueKind.String);
        challenge.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RegisterCustomer_ShouldNotIssueEmailChangeChallenge_ForPendingDocument_WhenPasswordDoesNotMatch()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var document = ValidCpf();
        var firstEmail = $"document.wrongpass.{Guid.NewGuid():N}@urbeat.local";
        const string password = "SenhaForte123";

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Pessoa Documento",
            Email = firstEmail,
            Password = password,
            PhoneNumber = "11977777777",
            Document = document
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Pessoa Documento Dois",
            Email = $"document.wrongpass.new.{Guid.NewGuid():N}@urbeat.local",
            Password = "SenhaErrada123",
            PhoneNumber = "11966666666",
            Document = document
        });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("documentAlreadyRegistered").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("emailChangeChallenge", out var challenge).Should().BeTrue();
        challenge.ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static string ValidCpf()
    {
        var random = Random.Shared;
        var digits = new int[9];
        do
        {
            for (var i = 0; i < digits.Length; i++)
            {
                digits[i] = random.Next(0, 10);
            }
        }
        while (digits.Distinct().Count() == 1);

        var firstCheckDigit = CheckDigit(digits, 10);
        var secondCheckDigit = CheckDigit(digits.Append(firstCheckDigit).ToArray(), 11);

        return string.Concat(digits) + firstCheckDigit + secondCheckDigit;
    }

    private static int CheckDigit(IReadOnlyList<int> digits, int weightStart)
    {
        var sum = 0;
        for (var i = 0; i < digits.Count; i++)
        {
            sum += digits[i] * (weightStart - i);
        }

        var remainder = (sum * 10) % 11;
        return remainder == 10 ? 0 : remainder;
    }
}
