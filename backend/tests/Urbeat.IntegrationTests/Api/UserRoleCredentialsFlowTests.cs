using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Security;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// The same e-mail owns a single AspNetUsers identity, but Customer and Seller keep independent
/// passwords, lockouts, sessions and password recovery flows.
/// </summary>
public sealed class UserRoleCredentialsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string CustomerPassword = "ClienteForte123";
    private const string SellerPassword = "LojistaForte123";
    private const string NewSellerPassword = "LojistaNova456";
    private const string NewCustomerPassword = "ClienteNova456";

    private readonly TestWebApplicationFactory _factory;

    public UserRoleCredentialsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NewCustomer_ShouldCreateCustomerCredential_AndLogin()
    {
        var email = UniqueEmail("customer");
        var client = CreateClient();

        var response = await RegisterAsync(client, "customer", email, CustomerPassword);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var credential = await GetCredentialAsync(email, UserRoleType.Customer);
        credential.Should().NotBeNull();
        credential!.PasswordHash.Should().NotBe(CustomerPassword);

        var login = await LoginAsync(client, "customer", email, CustomerPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NewSeller_ShouldCreateSellerCredential_AndLogin()
    {
        var email = UniqueEmail("seller");
        var client = CreateClient();

        var response = await RegisterAsync(client, "seller", email, SellerPassword);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        await _factory.ConfirmEmailAsync(email);

        var credential = await GetCredentialAsync(email, UserRoleType.Seller);
        credential.Should().NotBeNull();
        credential!.PasswordHash.Should().NotBe(SellerPassword);

        var login = await LoginAsync(client, "seller", email, SellerPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExistingCustomer_ShouldAddSellerWithNewPassword_WithoutChangingCustomer()
    {
        var email = UniqueEmail("promote");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);
        var customerHashBefore = (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash;

        await AuthenticateAsAsync(client, "customer", email, CustomerPassword);

        var promote = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Contratante Promovido",
            Email = email,
            Password = NewSellerPassword,
            PhoneNumber = "11988888888"
        });
        client.DefaultRequestHeaders.Authorization = null;
        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());

        var payload = await promote.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("roleAddedToExistingAccount").GetBoolean().Should().BeTrue();

        var customerHashAfter = (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash;
        customerHashAfter.Should().Be(customerHashBefore);

        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, NewSellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExistingSeller_ShouldAddCustomerWithNewPassword_WithoutChangingSeller()
    {
        var email = UniqueEmail("promote.customer");
        var client = CreateClient();

        (await RegisterAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);
        var sellerHashBefore = (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash;

        await AuthenticateAsAsync(client, "seller", email, SellerPassword);

        var promote = await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Promovido",
            Email = email,
            Password = NewCustomerPassword,
            PhoneNumber = "11977777777"
        });
        client.DefaultRequestHeaders.Authorization = null;
        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());

        var sellerHashAfter = (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash;
        sellerHashAfter.Should().Be(sellerHashBefore);

        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "customer", email, NewCustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExistingAccount_ShouldNotGetTheSameRoleTwice()
    {
        var email = UniqueEmail("duplicate");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await RegisterAsync(client, "customer", email, "OutraSenha123");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        (await db.UserRoleCredentials.CountAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Customer)).Should().Be(1);
    }

    [Fact]
    public async Task AddRoleWithoutOwnershipProof_ShouldOnlyCreateAPendingChallenge()
    {
        var email = UniqueEmail("no-proof");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);

        var promote = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Sem Prova",
            Email = email,
            Password = NewSellerPassword,
            PhoneNumber = "11988888888"
        });

        promote.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await promote.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("rolePromotionPending").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        (await db.UserRoleCredentials.AnyAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller)).Should().BeFalse();
        (await db.RolePromotionRequests.AnyAsync(x =>
            x.UserId == user.Id && x.TargetRole == UserRoleType.Seller && x.ConsumedAtUtc == null)).Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticatedOwner_ShouldAddSellerWithoutTypingTheExistingPassword()
    {
        var email = UniqueEmail("authenticated");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        var login = await LoginAsync(client, "customer", email, CustomerPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await ReadAccessTokenAsync(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var promote = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Dono Autenticado",
            Email = email,
            Password = NewSellerPassword,
            PhoneNumber = "11988888888"
        });

        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_ShouldFail_WhenUsingTheOtherRolePassword()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();

        (await LoginAsync(client, "customer", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, "seller", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lockout_ShouldBeIsolatedByRole()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();

        HttpStatusCode customerStatus = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            customerStatus = (await LoginAsync(client, "customer", email, "SenhaErrada123")).StatusCode;
        }

        customerStatus.Should().Be(HttpStatusCode.Locked);
        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Locked);

        // The seller credential did not accumulate a single failure.
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var sellerCredential = await db.UserRoleCredentials
            .SingleAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller);
        sellerCredential.AccessFailedCount.Should().Be(0);
        sellerCredential.LockoutEndUtc.Should().BeNull();
    }

    [Fact]
    public async Task SellerPasswordRecovery_ShouldNotChangeCustomerCredential()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();
        var customerHashBefore = (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash;
        var sellerHashBefore = (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash;

        var rawToken = $"seller-reset-{Guid.NewGuid():N}";
        await InsertResetTokenAsync(email, UserRoleType.Seller, rawToken);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequestDto
        {
            Token = rawToken,
            NewPassword = "LojistaReset789!",
            ConfirmPassword = "LojistaReset789!",
            Role = "Seller"
        });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash.Should().Be(customerHashBefore);
        (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash.Should().NotBe(sellerHashBefore);

        (await LoginAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, "seller", email, "LojistaReset789!")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomerPasswordRecovery_ShouldNotChangeSellerCredential()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();
        var customerHashBefore = (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash;
        var sellerHashBefore = (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash;

        var rawToken = $"customer-reset-{Guid.NewGuid():N}";
        await InsertResetTokenAsync(email, UserRoleType.Customer, rawToken);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequestDto
        {
            Token = rawToken,
            NewPassword = "ClienteReset789!",
            ConfirmPassword = "ClienteReset789!",
            Role = "Customer"
        });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        (await GetCredentialAsync(email, UserRoleType.Seller))!.PasswordHash.Should().Be(sellerHashBefore);
        (await GetCredentialAsync(email, UserRoleType.Customer))!.PasswordHash.Should().NotBe(customerHashBefore);

        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "customer", email, "ClienteReset789!")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPassword_ShouldBindTokenToTheRequestedRole_AndNotInvalidateTheOtherRole()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();

        string customerRawToken = $"customer-forgot-{Guid.NewGuid():N}";
        await InsertResetTokenAsync(email, UserRoleType.Customer, customerRawToken);

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequestDto
        {
            Email = email,
            Role = "Seller"
        });
        forgot.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);

        var customerToken = await db.PasswordResetTokens
            .SingleAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Customer);
        customerToken.Used.Should().BeFalse();
        (await db.PasswordResetTokens.AnyAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller && !x.Used))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AdminLogin_ShouldNotDependOnUserRoleCredentials()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = "admin@urbeat.local",
            Password = "Admin12345"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = await db.Users.SingleAsync(x => x.Email == "admin@urbeat.local");
        (await db.UserRoleCredentials.AnyAsync(x => x.UserId == admin.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Jwt_ShouldContainOnlyTheRequestedLoginContext_WhenUserOwnsBothRoles()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();

        var customerLogin = await LoginAsync(client, "customer", email, CustomerPassword);
        var sellerLogin = await LoginAsync(client, "seller", email, SellerPassword);

        var customerToken = await ReadAccessTokenAsync(customerLogin);
        var sellerToken = await ReadAccessTokenAsync(sellerLogin);

        ReadRoleClaims(customerToken).Should().Equal("Customer");
        ReadRoleClaims(sellerToken).Should().Equal("Seller");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sellerToken);
        (await client.GetAsync("/api/customer/home")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        (await client.GetAsync("/api/customer/home")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LegacyRefreshTokenWithoutRole_ShouldStillRefreshUntilItExpires()
    {
        var email = UniqueEmail("legacy-refresh");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        const string rawRefreshToken = "legacy-refresh-token-value";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Role = null,
                TokenHash = RefreshTokenHasher.Hash(rawRefreshToken),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"urbeat.refresh_token={rawRefreshToken}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await ReadAccessTokenAsync(response);
        ReadRoleClaims(token).Should().Contain("Customer");
    }

    [Fact]
    public async Task AuthenticatedChangePassword_ShouldUpdateOnlyTheRequestedRole()
    {
        var (email, client) = await CreateAccountWithBothRolesAsync();

        var login = await LoginAsync(client, "customer", email, CustomerPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await ReadAccessTokenAsync(login));

        var change = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequestDto
        {
            Role = "Customer",
            CurrentPassword = CustomerPassword,
            NewPassword = "ClienteAlterada789!",
            ConfirmPassword = "ClienteAlterada789!"
        });
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        // A Seller token cannot change the Customer credential.
        var sellerLogin = await LoginAsync(client, "seller", email, SellerPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await ReadAccessTokenAsync(sellerLogin));
        var forbidden = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequestDto
        {
            Role = "Customer",
            CurrentPassword = "ClienteAlterada789!",
            NewPassword = "OutraCliente789!",
            ConfirmPassword = "OutraCliente789!"
        });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = null;
        (await LoginAsync(client, "customer", email, "ClienteAlterada789!")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "seller", email, SellerPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new() { AllowAutoRedirect = false });

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@urbeat.local";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string role, string email, string password)
        => client.PostAsJsonAsync($"/api/auth/register/{role}", new RegisterUserRequestDto
        {
            FullName = $"Pessoa {role}",
            Email = email,
            Password = password,
            PhoneNumber = "11999999999"
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string role, string email, string password)
        => client.PostAsJsonAsync($"/api/auth/login/{role}", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

    private async Task<(string Email, HttpClient Client)> CreateAccountWithBothRolesAsync()
    {
        var email = UniqueEmail("both");
        var client = CreateClient();

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode.Should().Be(HttpStatusCode.Created);
        await _factory.ConfirmEmailAsync(email);

        // Promotion uses a fresh client/token so the returned client keeps no stale Authorization
        // header for the subsequent login/password tests.
        var promotionClient = CreateClient();
        await AuthenticateAsAsync(promotionClient, "customer", email, CustomerPassword);
        var promote = await promotionClient.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = "Contratante Dual",
            Email = email,
            Password = SellerPassword,
            PhoneNumber = "11988888888"
        });
        promote.StatusCode.Should().Be(HttpStatusCode.Created, await promote.Content.ReadAsStringAsync());

        return (email, client);
    }

    private static async Task AuthenticateAsAsync(HttpClient client, string role, string email, string password)
    {
        var login = await LoginAsync(client, role, email, password);
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = await ReadAccessTokenAsync(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task InsertResetTokenAsync(string email, UserRoleType role, string rawToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var tokenHasher = scope.ServiceProvider.GetRequiredService<Urbeat.Application.Interfaces.ITokenHasher>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user!.Id,
            Role = role,
            TokenHash = tokenHasher.Hash(rawToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Used = false
        });
        await db.SaveChangesAsync();
    }

    private async Task<UserRoleCredential?> GetCredentialAsync(string email, UserRoleType role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        return await db.UserRoleCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Role == role);
    }

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        payload.Should().NotBeNull();
        return payload!.AccessToken;
    }

    private static string[] ReadRoleClaims(string accessToken)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return token.Claims
            .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
            .Select(claim => claim.Value)
            .OrderBy(value => value)
            .ToArray();
    }
}
