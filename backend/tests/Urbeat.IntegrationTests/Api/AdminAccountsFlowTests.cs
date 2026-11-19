using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

/// <summary>
/// End-to-end contract of administrative account management: the admin-only surface rejects other
/// roles, invitations are single-use and expire, accepts activate the account, block/remove honor
/// the last-active-administrator invariant, and no response leaks a token or secret.
/// </summary>
public sealed class AdminAccountsFlowTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string AdminPassword = "Admin12345";
    private const string InvitedPassword = "NovoAdmin123";
    private const string SecondInvitedPassword = "SegundoAdmin123";

    [Fact]
    public async Task ManagementEndpoints_ShouldRequireTheAdminRole()
    {
        using var factory = new TestWebApplicationFactory();
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/admin/administrators")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(
            "/api/admin/administrators",
            new { email = "x@exemplo.com" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/admin/administrators/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var sellerEmail = $"admin.flow.seller.{Guid.NewGuid():N}@urbeat.local";
        await factory.RegisterSellerAsync(anonymous, sellerEmail, "Lojista123!", "Lojista Fluxo");
        var sellerToken = await LoginAsync(anonymous, sellerEmail, "Lojista123!", "/api/auth/login/seller");
        sellerToken.Should().NotBeNull();

        var customerEmail = $"admin.flow.customer.{Guid.NewGuid():N}@urbeat.local";
        (await anonymous.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Fluxo",
            Email = customerEmail,
            Password = "Cliente123!",
            PhoneNumber = "11977777777"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(customerEmail);
        var customerToken = await LoginAsync(anonymous, customerEmail, "Cliente123!", "/api/auth/login/customer");

        foreach (var token in new[] { sellerToken!, customerToken! })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/administrators");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await anonymous.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var adminToken = await LoginAsync(anonymous, AdminEmail, AdminPassword, "/api/auth/login/admin");
        adminToken.Should().NotBeNull();

        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/administrators");
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await anonymous.SendAsync(adminRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_ShouldPaginateAdministrators_AndClampThePageSize()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true);
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var invite = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = $"paginado.admin.{Guid.NewGuid():N}@exemplo.com" });
        invite.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstPage = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Get,
            "/api/admin/administrators?page=1&pageSize=1");
        firstPage.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid firstId;
        int totalItems;
        int totalPages;
        using (var json = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("page").GetInt32().Should().Be(1);
            json.RootElement.GetProperty("pageSize").GetInt32().Should().Be(1);
            totalItems = json.RootElement.GetProperty("totalItems").GetInt32();
            totalPages = json.RootElement.GetProperty("totalPages").GetInt32();
            totalItems.Should().BeGreaterThanOrEqualTo(2);
            totalPages.Should().Be(totalItems);
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            items.Should().HaveCount(1);
            firstId = items[0].GetProperty("id").GetGuid();
        }

        var secondPage = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Get,
            "/api/admin/administrators?page=2&pageSize=1");
        secondPage.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("page").GetInt32().Should().Be(2);
            var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
            item.GetProperty("id").GetGuid().Should().NotBe(firstId);
        }

        var clamped = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Get,
            "/api/admin/administrators?page=1&pageSize=1000");
        clamped.StatusCode.Should().Be(HttpStatusCode.OK);
        using var clampedJson = JsonDocument.Parse(await clamped.Content.ReadAsStringAsync());
        clampedJson.RootElement.GetProperty("pageSize").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task FullInvitationFlow_ShouldActivateTheAccount_AllowLogin_AndBeSingleUse()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true);
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;
        var invitedEmail = $"novo.admin.{Guid.NewGuid():N}@exemplo.com";

        var invite = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = invitedEmail });

        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        var inviteBody = await invite.Content.ReadAsStringAsync();
        AssertNoSecrets(inviteBody);

        using var inviteJson = JsonDocument.Parse(inviteBody);
        var administratorId = inviteJson.RootElement.GetProperty("administratorId").GetGuid();
        var invitationId = inviteJson.RootElement.GetProperty("invitation").GetProperty("id").GetGuid();
        inviteJson.RootElement.GetProperty("emailQueued").GetBoolean().Should().BeTrue(
            "the delivery job must be enqueued right after the invitation is committed");
        inviteJson.RootElement.GetProperty("emailDeliveryPending").GetBoolean().Should().BeTrue();

        // The durable job receives only the invitation id and reconstructs the link. Running it in
        // the test proves the fake transport gets the fragment URL and the row is marked as sent.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SendAdminAccountEmailJob>();
            await job.ExecuteAsync(invitationId);
        }

        await WaitForEmailAsync(factory.EmailService, invitedEmail);
        factory.EmailService.Messages.Should().Contain(message => message.ToAddress == invitedEmail);
        factory.EmailService.Messages.Last().HtmlBody.Should().Contain("/convite-admin#token=");

        var token = CreateToken(factory, invitationId, AdminInvitationPurpose.AccountInvitation);

        var validation = await client.PostAsJsonAsync(
            "/api/auth/admin-account/invitation/validate",
            new { token });
        validation.StatusCode.Should().Be(HttpStatusCode.OK);
        var validationBody = await validation.Content.ReadAsStringAsync();
        AssertNoSecrets(validationBody);
        using (var validationJson = JsonDocument.Parse(validationBody))
        {
            validationJson.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
            validationJson.RootElement.GetProperty("purpose").GetString().Should().Be("AccountInvitation");
            validationJson.RootElement.GetProperty("emailMasked").GetString().Should().Contain("***@exemplo.com");
        }

        var accept = await client.PostAsJsonAsync("/api/auth/admin-account/invitation/accept", new
        {
            token,
            newPassword = InvitedPassword,
            confirmPassword = InvitedPassword
        });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNoSecrets(await accept.Content.ReadAsStringAsync());

        var invitedToken = await LoginAsync(
            client,
            invitedEmail,
            InvitedPassword,
            "/api/auth/login/admin");
        invitedToken.Should().NotBeNull("the invited account must be able to log in");

        var reused = await client.PostAsJsonAsync("/api/auth/admin-account/invitation/accept", new
        {
            token,
            newPassword = InvitedPassword,
            confirmPassword = InvitedPassword
        });
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var reusedJson = JsonDocument.Parse(await reused.Content.ReadAsStringAsync()))
        {
            reusedJson.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.TokenAlreadyUsed);
        }

        var list = await SendAuthorizedAsync(client, adminToken, HttpMethod.Get, "/api/admin/administrators?search=exemplo.com");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadAsStringAsync();
        AssertNoSecrets(listBody);
        using (var listJson = JsonDocument.Parse(listBody))
        {
            var items = listJson.RootElement.GetProperty("items");
            var activated = items.EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == administratorId);
            activated.GetProperty("status").GetString().Should().Be("Active");
            activated.GetProperty("isEmailConfirmed").GetBoolean().Should().BeTrue();
            activated.GetProperty("isLastActiveAdmin").GetBoolean().Should().BeFalse(
                "the hint must consider every active administrator, not only the filtered page");
        }

        // Block revokes sessions and prevents a new login.
        var block = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            $"/api/admin/administrators/{administratorId}/block",
            new { reason = "Teste de bloqueio" });
        block.StatusCode.Should().Be(HttpStatusCode.OK);

        var blockedLogin = await client.PostAsJsonAsync("/api/auth/login/admin", new LoginRequestDto
        {
            Email = invitedEmail,
            Password = InvitedPassword
        });
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.Locked);
        using (var blockedJson = JsonDocument.Parse(await blockedLogin.Content.ReadAsStringAsync()))
        {
            blockedJson.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.AdminBlocked);
        }

        var unblock = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            $"/api/admin/administrators/{administratorId}/unblock");
        unblock.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, invitedEmail, InvitedPassword, "/api/auth/login/admin"))
            .Should().NotBeNullOrEmpty("unblocking must allow a new login");

        var remove = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Delete,
            $"/api/admin/administrators/{administratorId}?reason=Teste");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A token issued before the role was removed must not keep managing administrators.
        var afterRemoval = await SendAuthorizedAsync(
            client,
            invitedToken,
            HttpMethod.Get,
            "/api/admin/administrators");
        afterRemoval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scopeCheck = factory.Services.CreateScope();
        var db = scopeCheck.ServiceProvider.GetRequiredService<Urbeat.Infrastructure.Persistence.ApplicationDbContext>();
        var identity = db.Users.Single(user => user.Email == invitedEmail);
        identity.Should().NotBeNull("removing an active administrator preserves the identity");

        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminInvitationSent")).Should().Be(1);
        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminInvitationAccepted")).Should().Be(1);
        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminBlocked")).Should().Be(1);
        (await db.AuditLogs.CountAsync(audit => audit.Event == "AdminRemoved")).Should().Be(1);

        foreach (var audit in db.AuditLogs)
        {
            audit.Description.Should().NotContain(token);
            audit.Description.Should().NotContain(InvitedPassword);
        }
    }

    [Fact]
    public async Task LastActiveAdminInvariant_ShouldBlockBlockAndRemoval()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true);
        var client = factory.CreateClient();
        var seededToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var seatedList = await SendAuthorizedAsync(client, seededToken, HttpMethod.Get, "/api/admin/administrators");
        Guid seededId;
        using (var json = JsonDocument.Parse(await seatedList.Content.ReadAsStringAsync()))
        {
            seededId = json.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("isCurrentUser").GetBoolean())
                .GetProperty("id").GetGuid();
        }

        var selfRemove = await SendAuthorizedAsync(
            client,
            seededToken,
            HttpMethod.Delete,
            $"/api/admin/administrators/{seededId}");
        selfRemove.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var selfJson = JsonDocument.Parse(await selfRemove.Content.ReadAsStringAsync()))
        {
            selfJson.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.SelfActionNotAllowed);
        }

        var invitedEmail = $"segundo.admin.{Guid.NewGuid():N}@exemplo.com";
        var invite = await SendAuthorizedAsync(
            client,
            seededToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = invitedEmail });
        invite.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid secondId;
        Guid invitationId;
        using (var json = JsonDocument.Parse(await invite.Content.ReadAsStringAsync()))
        {
            secondId = json.RootElement.GetProperty("administratorId").GetGuid();
            invitationId = json.RootElement.GetProperty("invitation").GetProperty("id").GetGuid();
        }

        var token = CreateToken(factory, invitationId, AdminInvitationPurpose.AccountInvitation);
        (await client.PostAsJsonAsync("/api/auth/admin-account/invitation/accept", new
        {
            token,
            newPassword = SecondInvitedPassword,
            confirmPassword = SecondInvitedPassword
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var secondToken = (await LoginAsync(
            client,
            invitedEmail,
            SecondInvitedPassword,
            "/api/auth/login/admin"))!;

        // Second admin blocks the seeded admin; it remains the only active administrator.
        (await SendAuthorizedAsync(
            client,
            secondToken,
            HttpMethod.Post,
            $"/api/admin/administrators/{seededId}/block",
            new { reason = "Teste de invariante" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The seeded access token is still valid by design (R2), but the invariant must hold.
        var blockLast = await SendAuthorizedAsync(
            client,
            seededToken,
            HttpMethod.Post,
            $"/api/admin/administrators/{secondId}/block",
            new { });
        blockLast.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var blockJson = JsonDocument.Parse(await blockLast.Content.ReadAsStringAsync()))
        {
            blockJson.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.LastActiveAdmin);
        }

        var removeLast = await SendAuthorizedAsync(
            client,
            seededToken,
            HttpMethod.Delete,
            $"/api/admin/administrators/{secondId}");
        removeLast.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var removeJson = JsonDocument.Parse(await removeLast.Content.ReadAsStringAsync());
        removeJson.RootElement.GetProperty("code").GetString()
            .Should().Be(AdminAccountErrorCodes.LastActiveAdmin);
    }

    [Fact]
    public async Task PublicLinkEndpoints_ShouldBeRateLimited_AndNeverBeAnExistenceOracle()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 1,
            useFakeEmail: false);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/api/auth/admin-account/invitation/validate",
            new { token = new string('a', 64) });
        var second = await client.PostAsJsonAsync(
            "/api/auth/admin-account/invitation/validate",
            new { token = new string('b', 64) });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        json.RootElement.TryGetProperty("emailMasked", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("purpose", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Invite_ShouldRejectASellerEmail_WithASpecificConflict_AndChangeNothing()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true);
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var sellerEmail = $"admin.invite.seller.{Guid.NewGuid():N}@urbeat.local";
        await factory.RegisterSellerAsync(client, sellerEmail, "Lojista123!", "Lojista Convite");

        int usersBefore;
        int invitationsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            usersBefore = await db.Users.CountAsync();
            invitationsBefore = await db.AdminInvitations.CountAsync();
        }

        var response = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = sellerEmail });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.SellerEmailAlreadyInUse);
            json.RootElement.GetProperty("detail").GetString().Should().Be(
                "Este e-mail já pertence a uma conta de lojista. O convite de administrador não pode ser enviado.");
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        (await verifyDb.Users.CountAsync()).Should().Be(usersBefore);
        (await verifyDb.AdminInvitations.CountAsync()).Should().Be(invitationsBefore);
        var seller = await userManager.FindByEmailAsync(sellerEmail);
        seller.Should().NotBeNull();
        (await userManager.IsInRoleAsync(seller!, "Seller")).Should().BeTrue();
        (await userManager.IsInRoleAsync(seller!, "Admin")).Should().BeFalse();
        (await verifyDb.AuditLogs.CountAsync(
                audit => audit.Event == "AdminInvitationSent")).Should().Be(0);
    }

    [Fact]
    public async Task Invite_ShouldKeepTheGenericConflict_ForANonSellerEmail()
    {
        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true);
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;

        var customerEmail = $"admin.invite.customer.{Guid.NewGuid():N}@urbeat.local";
        (await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Cliente Convite",
            Email = customerEmail,
            Password = "Cliente123!",
            PhoneNumber = "11977776666"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(customerEmail);

        var response = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = customerEmail });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString()
                .Should().Be(AdminAccountErrorCodes.EmailAlreadyInUse);
            json.RootElement.GetProperty("detail").GetString().Should().Be("Este e-mail já está em uso.");
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await verifyDb.AdminInvitations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Invite_ShouldReportAPendingDelivery_WhenTheEnqueueFails_ButKeepTheInvitation()
    {
        var failingClient = new Mock<IBackgroundJobClient>();
        failingClient
            .Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Throws(new InvalidOperationException("Hangfire storage is unavailable."));

        using var factory = new TestWebApplicationFactory(
            adminRecoveryEnabled: false,
            adminRecoveryKey: null,
            passwordRecoveryPermitLimit: 50,
            useFakeEmail: true)
        {
            BackgroundJobClient = failingClient.Object
        };
        var client = factory.CreateClient();
        var adminToken = (await LoginAsync(client, AdminEmail, AdminPassword, "/api/auth/login/admin"))!;
        var invitedEmail = $"convite.pendente.{Guid.NewGuid():N}@exemplo.com";

        var invite = await SendAuthorizedAsync(
            client,
            adminToken,
            HttpMethod.Post,
            "/api/admin/administrators",
            new { email = invitedEmail });

        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        AssertNoSecrets(await invite.Content.ReadAsStringAsync());

        using var json = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("emailQueued").GetBoolean().Should().BeFalse(
            "a failed enqueue must never be reported as a queued delivery");
        json.RootElement.GetProperty("emailDeliveryPending").GetBoolean().Should().BeTrue(
            "the invitation is committed and visibly pending for a manual resend");
        var invitationId = json.RootElement.GetProperty("invitation").GetProperty("id").GetGuid();

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await verifyDb.AdminInvitations.SingleAsync(row => row.Id == invitationId);
        stored.EmailSentAtUtc.Should().BeNull();
        (await verifyDb.Users.CountAsync(user => user.Email == invitedEmail)).Should().Be(1);
        failingClient.Verify(
            client => client.Create(
                It.Is<Job>(job => job.Type == typeof(SendAdminAccountEmailJob)),
                It.IsAny<IState>()),
            Times.Once);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        string token,
        HttpMethod method,
        string path,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string?> LoginAsync(
        HttpClient client,
        string email,
        string password,
        string path)
    {
        var response = await client.PostAsJsonAsync(path, new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "login must succeed for " + email);
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return payload?.AccessToken;
    }

    /// <summary>
    /// The test host keeps a Hangfire server running, so the job enqueued by the invite endpoint and
    /// the explicit execution below race for the same e-mail lease. Waiting for the transport
    /// instead of asserting immediately keeps the narrative deterministic.
    /// </summary>
    private static async Task WaitForEmailAsync(
        FakeEmailService emailService,
        string toAddress,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline
               && !emailService.Messages.Any(message => message.ToAddress == toAddress))
        {
            await Task.Delay(50);
        }
    }

    private static string CreateToken(
        TestWebApplicationFactory factory,
        Guid invitationId,
        AdminInvitationPurpose purpose)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
        return service.CreateToken(invitationId, purpose);
    }

    private static void AssertNoSecrets(string body)
    {
        var lower = body.ToLowerInvariant();
        lower.Should().NotContain("\"token\"");
        lower.Should().NotContain("tokenhash");
        lower.Should().NotContain("\"password\"");
        lower.Should().NotContain("securitystamp");
        lower.Should().NotContain("secret");
    }
}
