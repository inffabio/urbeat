using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// A second role is attached to an existing identity only when the caller is authenticated as that
/// identity or when the owner confirms a single-use e-mail challenge. Knowing the e-mail alone never
/// creates a role or credential.
/// </summary>
public sealed class RolePromotionFlowTests : IClassFixture<EmailConfirmationTestWebApplicationFactory>
{
    private const string CustomerPassword = "ClienteForte123";
    private const string SellerPassword = "LojistaForte123";

    private readonly EmailConfirmationTestWebApplicationFactory _factory;

    public RolePromotionFlowTests(EmailConfirmationTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousPromotion_ShouldSendEmail_AndOnlyApplyTheRoleAfterConfirmation()
    {
        var email = NewEmail("anon.promote");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);
        _factory.EmailService.Clear();

        var promote = await PostPromotionAsync(client, "seller", email, SellerPassword, "Contratante Promovido");

        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());
        var payload = await promote.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("rolePromotionPending").GetBoolean().Should().BeTrue();
        payload.GetProperty("roleAddedToExistingAccount").GetBoolean().Should().BeFalse();

        // Nothing is attached before the owner confirms the link.
        (await GetRoleStateAsync(email)).Should().Be((false, false));
        (await CountPendingRequestsAsync(email)).Should().Be(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            (await db.AuditLogs.AnyAsync(x =>
                x.Event == "SellerRegisteredPromotionPending" && x.UserId == user.Id)).Should().BeTrue();
        }

        var sent = _factory.EmailService.FindLastByRecipient(email);
        sent.Should().NotBeNull("the promotion challenge must be delivered to the existing address");
        sent!.Subject.Should().Be("Confirme seu novo acesso de lojista no urbeat");
        sent.HtmlBody.Should().Contain("não altera seu acesso de cliente");
        sent.HtmlBody.Should().Contain("não altera sua senha atual");
        sent.HtmlBody.Should().Contain("não solicita sua senha por e-mail");
        sent.HtmlBody.Should().Contain("30 minutos");
        sent.HtmlBody.Should().Contain("apenas uma vez");
        sent.HtmlBody.Should().Contain("Se você não solicitou esse acesso, ignore esta mensagem");
        sent.HtmlBody.Should().NotContain(SellerPassword);
        sent.HtmlBody.Should().NotContain(CustomerPassword);
        sent.HtmlBody.Should().Contain("#promotionToken=");
        sent.HtmlBody.Should().NotContain("?promotionToken=");

        var token = ExtractPromotionToken(sent.HtmlBody);

        var confirm = await ConfirmPromotionAsync(client, token);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var confirmPayload = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        confirmPayload.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        confirmPayload.GetProperty("alreadyApplied").GetBoolean().Should().BeFalse();
        confirmPayload.GetProperty("role").GetString().Should().Be("Seller");

        (await GetRoleStateAsync(email)).Should().Be((true, true));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            (await db.AuditLogs.AnyAsync(x =>
                x.Event == "RolePromotionConfirmed" && x.UserId == user.Id)).Should().BeTrue();
        }

        // Customer and seller credentials stay independent.
        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Replaying the consumed link is idempotent and never duplicates the credential.
        var replay = await ConfirmPromotionAsync(client, token);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayPayload = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayPayload.GetProperty("alreadyApplied").GetBoolean().Should().BeTrue();
        (await CountCredentialsAsync(email, UserRoleType.Seller)).Should().Be(1);
    }

    [Fact]
    public async Task AnonymousPromotion_ShouldNotRevealOrApplyAnything_WithoutTheConfirmationLink()
    {
        var email = NewEmail("attacker");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);

        var promote = await PostPromotionAsync(client, "seller", email, "AtacanteForte123", "Atacante");

        promote.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await promote.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("rolePromotionPending").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("existingUserEmail", out _).Should().BeFalse();

        (await GetRoleStateAsync(email)).Should().Be((false, false));
        var user = await GetUserAsync(email);
        var claims = await GetClaimsAsync(user.Id);
        claims.Should().NotContain(claim => claim.Type == "FullName" && claim.Value == "Atacante");
    }

    [Fact]
    public async Task AuthenticatedOwner_ShouldAddTheSecondRoleWithoutTypingTheFirstPassword()
    {
        var email = NewEmail("authenticated");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);

        var login = await LoginAsync(client, "customer", email, CustomerPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await ReadAccessTokenAsync(login));

        _factory.EmailService.Clear();
        var promote = await PostPromotionAsync(client, "seller", email, SellerPassword, "Dono Autenticado");

        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());
        var payload = await promote.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("roleAddedToExistingAccount").GetBoolean().Should().BeTrue();
        payload.GetProperty("rolePromotionPending").GetBoolean().Should().BeFalse();

        (await GetRoleStateAsync(email)).Should().Be((true, true));
        _factory.EmailService.FindLastByRecipient(email).Should().BeNull(
            "an authenticated owner does not need a new e-mail challenge");

        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExpiredChallenge_ShouldNotApplyTheRole()
    {
        var email = NewEmail("expired");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);
        _factory.EmailService.Clear();

        (await PostPromotionAsync(client, "seller", email, SellerPassword, "Expirado"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(_factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = await db.RolePromotionRequests.SingleAsync(x => x.NormalizedEmail == email);
            request.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var confirm = await ConfirmPromotionAsync(client, token);
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetRoleStateAsync(email)).Should().Be((false, false));
    }

    [Fact]
    public async Task RepeatedPromotionRequest_ShouldReuseThePendingLinkInsteadOfInvalidatingIt()
    {
        var email = NewEmail("repeated");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);
        _factory.EmailService.Clear();

        (await PostPromotionAsync(client, "seller", email, SellerPassword, "Primeiro"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var firstToken = ExtractPromotionToken(_factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        (await PostPromotionAsync(client, "seller", email, SellerPassword, "Segundo"))
            .StatusCode.Should().Be(HttpStatusCode.Created, "a repeat must not invalidate the link that was already sent");

        // No second e-mail is enqueued and only one pending request exists: the first link is still
        // the one the owner can confirm.
        _factory.EmailService.Messages
            .Count(x => string.Equals(x.ToAddress, email, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        (await CountPendingRequestsAsync(email)).Should().Be(1);
        (await CountRequestsAsync(email)).Should().Be(1);

        var confirm = await ConfirmPromotionAsync(client, firstToken);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        (await GetRoleStateAsync(email)).Should().Be((true, true));
    }

    [Fact]
    public async Task UnknownOrWrongPurposeToken_ShouldBeRejectedGenerically()
    {
        var client = CreateClient();
        var email = NewEmail("wrongtoken");
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);
        var emailChangeChallenge = "eyJ1c2VySWQiOiJ4In0.signature";

        var confirm = await ConfirmPromotionAsync(client, emailChangeChallenge);

        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await confirm.Content.ReadAsStringAsync();
        var payload = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        var message = payload.TryGetProperty("detail", out var detail)
            ? detail.GetString()
            : payload.GetProperty("message").GetString();
        message.Should().Contain("Link de confirmação inválido ou expirado.");
        body.Should().NotContain(email);
        (await GetRoleStateAsync(email)).Should().Be((false, false));
    }

    [Fact]
    public async Task EmptyNewPassword_ShouldBeRejected_WithoutCreatingAPendingRequestOrCredential()
    {
        var email = NewEmail("emptypassword");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);

        var promote = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Sem Senha",
            Email = email,
            Password = string.Empty,
            PhoneNumber = "11988888888"
        });

        promote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetRoleStateAsync(email)).Should().Be((false, false));
        (await CountPendingRequestsAsync(email)).Should().Be(0);
    }

    [Fact]
    public async Task RegisteringTheSameRoleAgain_ShouldStillConflict()
    {
        var email = NewEmail("same.role");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "customer", email, CustomerPassword);

        var duplicate = await RegisterAsync(client, "customer", email, "OutraSenha123");

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountCredentialsAsync(email, UserRoleType.Customer)).Should().Be(1);
    }

    [Fact]
    public async Task RepeatedPromotionRequests_ShouldBeRateLimited()
    {
        using var factory = new EmailConfirmationTestWebApplicationFactory(rolePromotionPermitLimit: 2);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("rate-limit");

        // First permit: create the existing identity (registration shares the dedicated policy).
        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);

        // Second permit: the promotion request is accepted.
        var first = await PostPromotionAsync(client, "seller", email, SellerPassword, "Rate Limit");
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        // Third request in the same window is rejected before it can send another e-mail.
        var second = await PostPromotionAsync(client, "seller", email, SellerPassword, "Rate Limit");
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ReversePromotion_ShouldAddCustomerToAnExistingSeller()
    {
        var email = NewEmail("reverse");
        var client = CreateClient();
        await RegisterAndConfirmAsync(client, "seller", email, SellerPassword);
        _factory.EmailService.Clear();

        var promote = await PostPromotionAsync(client, "customer", email, CustomerPassword, "Cliente Reverso");
        promote.StatusCode.Should().Be(HttpStatusCode.Created);

        var sent = _factory.EmailService.FindLastByRecipient(email);
        sent.Should().NotBeNull();
        sent!.Subject.Should().Be("Confirme seu novo acesso de cliente no urbeat");
        sent.HtmlBody.Should().Contain("não altera seu acesso de lojista");

        var confirm = await ConfirmPromotionAsync(client, ExtractPromotionToken(sent.HtmlBody));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new() { AllowAutoRedirect = false });

    private static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@urbeat.local";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string role, string email, string password)
        => client.PostAsJsonAsync($"/api/auth/register/{role}", new RegisterUserRequestDto
        {
            FullName = $"Pessoa {role} {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11999999999"
        });

    private async Task RegisterAndConfirmAsync(HttpClient client, string role, string email, string password)
    {
        (await RegisterAsync(client, role, email, password)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);
    }

    private static Task<HttpResponseMessage> PostPromotionAsync(
        HttpClient client,
        string role,
        string email,
        string newPassword,
        string fullName)
        => client.PostAsJsonAsync($"/api/auth/register/{role}", new RegisterUserRequestDto
        {
            FullName = fullName,
            Email = email,
            Password = newPassword,
            PhoneNumber = "11988888888"
        });

    private static Task<HttpResponseMessage> ConfirmPromotionAsync(HttpClient client, string token)
        => client.PostAsJsonAsync("/api/auth/role-promotion/confirm", new ConfirmRolePromotionRequestDto
        {
            Token = token
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string role, string email, string password)
        => client.PostAsJsonAsync($"/api/auth/login/{role}", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

    private async Task<(bool HasRole, bool HasCredential)> GetRoleStateAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var hasRole = await userManager.IsInRoleAsync(user, "Seller");
        var hasCredential = await db.UserRoleCredentials
            .AnyAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller);
        return (hasRole, hasCredential);
    }

    private async Task<int> CountPendingRequestsAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RolePromotionRequests.CountAsync(x =>
            x.NormalizedEmail == email && x.ConsumedAtUtc == null && x.SupersededAtUtc == null);
    }

    private async Task<int> CountRequestsAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RolePromotionRequests.CountAsync(x => x.NormalizedEmail == email);
    }

    private async Task<int> CountCredentialsAsync(string email, UserRoleType role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        return await db.UserRoleCredentials.CountAsync(x => x.UserId == user.Id && x.Role == role);
    }

    private async Task<IdentityUser<Guid>> GetUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.SingleAsync(x => x.Email == email);
    }

    private async Task<IList<System.Security.Claims.Claim>> GetClaimsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GetClaimsAsync(user!);
    }

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        payload.Should().NotBeNull();
        return payload!.AccessToken;
    }

    private static string ExtractPromotionToken(string html)
    {
        var match = Regex.Match(html, "#promotionToken=(?<token>[a-zA-Z0-9_-]+)");
        match.Success.Should().BeTrue("the promotion e-mail must contain the opaque confirmation token");
        return match.Groups["token"].Value;
    }
}
