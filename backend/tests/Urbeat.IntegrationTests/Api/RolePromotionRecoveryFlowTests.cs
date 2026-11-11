using System.Net;
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
/// A promotion confirmation is applied exactly once and stays retryable: a failure after the
/// challenge was claimed but before the role/credential were written must not consume the link, and
/// a retry after a partial apply must never duplicate the role or the credential.
/// </summary>
public sealed class RolePromotionRecoveryFlowTests
{
    private const string CustomerPassword = "ClienteForte123";
    private const string SellerPassword = "LojistaForte123";

    [Fact]
    public async Task FailureBetweenClaimAndCredential_ShouldKeepTheChallengeRetryable()
    {
        var interceptor = new FailOnceCredentialWriteInterceptor();
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.failure");

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email)).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // The credential write blows up after the challenge was claimed. The request fails (500) but
        // nothing may be consumed.
        interceptor.Arm();
        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be((false, false));
        var afterFailure = await GetRequestStateAsync(factory, email);
        afterFailure.Consumed.Should().BeFalse("a failed attempt must never consume the challenge");
        afterFailure.Claimed.Should().BeFalse("the claim must not outlive a failed non-relational attempt");

        // The same link can be retried and applies exactly once.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();

        var replay = await ConfirmPromotionAsync(client, token);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayPayload = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayPayload.GetProperty("alreadyApplied").GetBoolean().Should().BeTrue();
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
    }

    [Fact]
    public async Task RetryAfterAPartialApply_ShouldBeIdempotent_AndReleaseThePendingSlot()
    {
        using var factory = new EmailConfirmationTestWebApplicationFactory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.partial");

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email)).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        (await ConfirmPromotionAsync(client, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulate a crash right before the applied marker was persisted: the role and credential
        // exist, but the request still looks pending.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = await db.RolePromotionRequests.SingleAsync(x => x.NormalizedEmail == email);
            request.ConsumedAtUtc = null;
            await db.SaveChangesAsync();
        }

        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyApplied").GetBoolean().Should().BeTrue();

        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue(
            "the idempotent replay must close the pending request instead of leaving it active");
    }

    [Fact]
    public async Task PromotionEmail_ShouldBeRecoveredFromTheDatabaseWhenTheOriginalEnqueueWasLost()
    {
        using var factory = new EmailConfirmationTestWebApplicationFactory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.recovery");

        (await RegisterAsync(client, "customer", email, CustomerPassword)).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Simulate the lost enqueue: the request exists but was never recorded as sent, and the
        // original job run is in the past. Nothing is cached anywhere.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = await db.RolePromotionRequests.SingleAsync(x => x.NormalizedEmail == email);
            request.EmailSentAtUtc = null;
            db.Entry(request).Property(nameof(RolePromotionRequest.CreatedAtUtc)).CurrentValue =
                DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        factory.EmailService.Clear();

        using (var scope = factory.Services.CreateScope())
        {
            var recovery = scope.ServiceProvider
                .GetRequiredService<Urbeat.Infrastructure.Jobs.RolePromotionEmailRecoveryJob>();
            await recovery.ExecuteAsync();
        }

        // The recovery sweep re-enqueued the delivery job (synchronously in tests), which rebuilt the
        // link from the persisted request and delivered it.
        var sent = factory.EmailService.FindLastByRecipient(email);
        sent.Should().NotBeNull("the recovery sweep must be able to deliver a stranded request");
        sent!.Subject.Should().Be("Confirme seu novo acesso de lojista no urbeat");

        var token = ExtractPromotionToken(sent.HtmlBody);
        var confirm = await ConfirmPromotionAsync(client, token);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
    }

    [Fact]
    public async Task FailureDuringRoleAssignment_ShouldCompensateCredentialAndFullNameClaim()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.RoleAssignment);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.role");

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Original")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Novo")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // The role row fails after the claim and the credential were written.
        interceptor.Arm();
        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be((false, false));
        (await GetFullNameClaimAsync(factory, email)).Should().Be(
            "Cliente Original",
            "the FullName claim applied by the failed attempt must be reverted");
        var afterFailure = await GetRequestStateAsync(factory, email);
        afterFailure.Consumed.Should().BeFalse();
        afterFailure.Claimed.Should().BeFalse();

        // A retry applies the promotion exactly once and the claim reflects the new request.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetFullNameClaimAsync(factory, email)).Should().Be("Lojista Novo");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
        (await IsEmailConfirmedAsync(factory, email)).Should().BeTrue();
    }

    [Fact]
    public async Task FailureDuringEmailConfirmation_ShouldCompensateRoleCredentialClaimAndConfirmationFlag()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(
            RolePromotionFailurePoint.EmailConfirmationUpdate);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.confirm");

        // The identity is intentionally left unconfirmed: confirming the promotion link also proves
        // control of the mailbox and must flip EmailConfirmed only when the whole apply succeeds.
        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Pendente")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Confirmado")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // Fail exactly on the update that marks the e-mail as confirmed, after role and credential.
        interceptor.Arm();
        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be((false, false));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(0);
        (await GetFullNameClaimAsync(factory, email)).Should().Be(
            "Cliente Pendente",
            "the claim must be reverted together with the role and credential");
        (await IsEmailConfirmedAsync(factory, email)).Should().BeFalse(
            "a failed attempt must not confirm the e-mail");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetFullNameClaimAsync(factory, email)).Should().Be("Lojista Confirmado");
        (await IsEmailConfirmedAsync(factory, email)).Should().BeTrue();
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
    }

    [Fact]
    public async Task IncompleteCompensation_ShouldNotReportSuccess_AndTheRetryShouldRepairTheLeftoverRole()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.EmailConfirmationUpdate);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.compensation");

        // The identity is left unconfirmed so AddToRoleAsync does not itself look like the explicit
        // e-mail confirmation update; only the promotion apply sets EmailConfirmed there.
        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Estavel")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Persistente")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // The apply fails on the e-mail confirmation step and the compensation of the role keeps
        // failing: the request must fail, never report success, and the leftover must stay repairable.
        interceptor.Arm(RolePromotionFailurePoint.EmailConfirmationUpdate);
        interceptor.ArmUntilDisarmed(RolePromotionFailurePoint.CompensationRoleRemoval);

        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var afterFailure = await GetRequestStateAsync(factory, email);
        afterFailure.Consumed.Should().BeFalse("an incomplete compensation must not consume the challenge");
        afterFailure.Claimed.Should().BeFalse();

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (true, false),
            "the credential is compensated first, the role that could not be removed is left for the retry");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            (await db.AuditLogs.AnyAsync(x =>
                x.Event == "RolePromotionCompensationFailed" && x.UserId == user.Id)).Should().BeTrue(
                "an incomplete compensation must be audited instead of being treated as a rollback");
        }

        // The retry runs while the compensation step is still failing: it must complete the
        // promotion by reusing the leftover role instead of blocking the owner forever.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyApplied").GetBoolean().Should().BeFalse();

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetFullNameClaimAsync(factory, email)).Should().Be("Lojista Persistente");
        (await IsEmailConfirmedAsync(factory, email)).Should().BeTrue();
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
    }

    [Fact]
    public async Task CompensationThatKeepsTheCredential_ShouldBeRecoveredByTheIdempotentReplay()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.EmailConfirmationUpdate);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.replay");

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Original")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Replay")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // Role and credential stay applied because the compensation itself fails on the credential.
        interceptor.Arm(RolePromotionFailurePoint.EmailConfirmationUpdate);
        interceptor.ArmUntilDisarmed(RolePromotionFailurePoint.CompensationCredentialRemoval);

        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (true, true),
            "the failed attempt must not be reported as success even though it left the role applied");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        // Retrying the same link closes the request idempotently: no duplicate credential is created.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyApplied").GetBoolean().Should().BeTrue();

        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await IsEmailConfirmedAsync(factory, email)).Should().BeTrue();
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
    }

    [Fact]
    public async Task CredentialLeftoverWithoutTheRole_ShouldBeCompletedByTheRetry()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.RoleAssignment);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.credential");

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Original")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Recuperado")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // The role write fails after the credential was persisted, and the compensation cannot remove
        // the credential either: the account is left with a credential but no role.
        interceptor.Arm(RolePromotionFailurePoint.RoleAssignment);
        interceptor.ArmUntilDisarmed(RolePromotionFailurePoint.CompensationCredentialRemoval);

        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (false, true),
            "the credential is written before the role, so only it can be left behind");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        // The leftover credential is linked to the pending request, so the retry finishes the
        // promotion with it instead of duplicating it.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyApplied").GetBoolean().Should().BeFalse();

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
        (await IsEmailConfirmedAsync(factory, email)).Should().BeTrue();
    }

    [Fact]
    public async Task PreexistingCredentialWithoutLink_ShouldNotBeAdoptedNorOverwritten()
    {
        using var factory = new EmailConfirmationTestWebApplicationFactory(rolePromotionPermitLimit: 10000);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.ambiguous");

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Ambíguo")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Ambíguo")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // Simulate a credential created out of band: it has no link to the pending request.
        const string externalHash = "AQAAAAIAAYagAAAAEEXTERNALHASHVALUE";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            db.UserRoleCredentials.Add(new UserRoleCredential
            {
                UserId = user.Id,
                Role = UserRoleType.Seller,
                PasswordHash = externalHash,
                RolePromotionRequestId = null
            });
            await db.SaveChangesAsync();
        }

        var confirm = await ConfirmPromotionAsync(client, token);
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (false, true),
            "the role must not be attached to a credential of unknown origin");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            var credential = await db.UserRoleCredentials
                .SingleAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller);
            credential.PasswordHash.Should().Be(externalHash, "an existing credential is never overwritten");
            (await db.AuditLogs.AnyAsync(x =>
                x.Event == "RolePromotionConfirmationFailed" && x.UserId == user.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task CredentialLeftoverWithoutTheRole_ShouldBeResumedThroughThePendingLink()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.RoleAssignment);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.resume");

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Resume")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        await factory.ConfirmEmailAsync(email);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Resume")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        interceptor.Arm(RolePromotionFailurePoint.RoleAssignment);
        interceptor.ArmUntilDisarmed(RolePromotionFailurePoint.CompensationCredentialRemoval);
        (await ConfirmPromotionAsync(client, token)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be((false, true));

        // Registering again must not reject the orphaned credential with "account exists": it
        // resumes the pending request and lets the owner finish through the same e-mail link.
        var resumed = await PostPromotionAsync(client, email, "Lojista Resume");
        resumed.StatusCode.Should().Be(HttpStatusCode.Created, await resumed.Content.ReadAsStringAsync());
        (await resumed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rolePromotionPending").GetBoolean().Should().BeTrue();

        factory.EmailService.Messages
            .Count(x => string.Equals(x.ToAddress, email, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "the pending link was already delivered and must not be re-sent");

        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());

        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeTrue();
    }

    [Fact]
    public async Task Compensation_ShouldNotRemoveACredentialCreatedByAnotherOperation()
    {
        var injection = new InjectCompetingCredentialInterceptor();
        var failure = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.EmailConfirmationUpdate);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: failure,
            additionalSaveChangesInterceptors: [injection]);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.concurrent.credential");
        const string competingHash = "AQAAAAIAAYagAAAAECOMPETINGCONCURRENTHASH";

        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Concorrente")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Concorrente")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // The promotion credential is written together with a credential created by another
        // operation (no request link), then the promotion fails on the e-mail confirmation step.
        injection.Arm(competingHash);
        failure.Arm();

        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (false, true),
            "the promotion credential is compensated, but the other operation's credential must survive");
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            var credential = await db.UserRoleCredentials
                .SingleAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller);
            credential.PasswordHash.Should().Be(competingHash);
            credential.RolePromotionRequestId.Should().BeNull(
                "only the promotion credential was linked to the request and only it may be removed");
        }

        // The remaining credential has an unknown origin, so the retry fails closed instead of
        // adopting or overwriting it.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
    }

    [Fact]
    public async Task Compensation_ShouldNotRemoveARoleCreatedByAnotherOperation()
    {
        var interceptor = new FailOnceIdentityWriteInterceptor(RolePromotionFailurePoint.EmailConfirmationUpdate);
        using var factory = new EmailConfirmationTestWebApplicationFactory(
            rolePromotionPermitLimit: 10000,
            saveChangesInterceptor: interceptor);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var email = NewEmail("retry.concurrent.role");

        // The identity stays unconfirmed so only the promotion sets EmailConfirmed and triggers the
        // armed failure after the role/credential steps.
        (await RegisterAsync(client, "customer", email, CustomerPassword, "Cliente Papel")).StatusCode
            .Should().Be(HttpStatusCode.Created);
        factory.EmailService.Clear();

        (await PostPromotionAsync(client, email, "Lojista Papel")).StatusCode.Should().Be(HttpStatusCode.Created);
        var token = ExtractPromotionToken(factory.EmailService.FindLastByRecipient(email)!.HtmlBody);

        // Another operation attaches the same role before the confirmation applies it.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            (await userManager.AddToRoleAsync(user, "Seller")).Succeeded.Should().BeTrue();
        }

        interceptor.Arm();
        var failedAttempt = await ConfirmPromotionAsync(client, token);
        failedAttempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        (await GetRoleStateAsync(factory, email)).Should().Be(
            (true, false),
            "the role was not created by this attempt, so the compensation must not remove it");
        (await GetRequestStateAsync(factory, email)).Consumed.Should().BeFalse();

        // The retry completes the promotion by reusing the role, without duplicating the credential.
        var retry = await ConfirmPromotionAsync(client, token);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await GetRoleStateAsync(factory, email)).Should().Be((true, true));
        (await CountCredentialsAsync(factory, email, UserRoleType.Seller)).Should().Be(1);
    }

    private static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@urbeat.local";

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string role,
        string email,
        string password,
        string? fullName = null)
        => client.PostAsJsonAsync($"/api/auth/register/{role}", new RegisterUserRequestDto
        {
            FullName = fullName ?? $"Pessoa {role} {Guid.NewGuid():N}",
            Email = email,
            Password = password,
            PhoneNumber = "11999999999"
        });

    private static Task<HttpResponseMessage> PostPromotionAsync(
        HttpClient client,
        string email,
        string fullName = "Contratante Retry")
        => client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = fullName,
            Email = email,
            Password = SellerPassword,
            PhoneNumber = "11988888888"
        });

    private static Task<HttpResponseMessage> ConfirmPromotionAsync(HttpClient client, string token)
        => client.PostAsJsonAsync("/api/auth/role-promotion/confirm", new ConfirmRolePromotionRequestDto
        {
            Token = token
        });

    private static async Task<(bool HasRole, bool HasCredential)> GetRoleStateAsync(
        EmailConfirmationTestWebApplicationFactory factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var hasRole = await userManager.IsInRoleAsync(user, "Seller");
        var hasCredential = await db.UserRoleCredentials
            .AnyAsync(x => x.UserId == user.Id && x.Role == UserRoleType.Seller);
        return (hasRole, hasCredential);
    }

    private static async Task<(bool Consumed, bool Claimed)> GetRequestStateAsync(
        EmailConfirmationTestWebApplicationFactory factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var request = await db.RolePromotionRequests.SingleAsync(x => x.NormalizedEmail == email);
        return (request.ConsumedAtUtc is not null, request.ClaimedAtUtc is not null);
    }

    private static async Task<string?> GetFullNameClaimAsync(
        EmailConfirmationTestWebApplicationFactory factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var claims = await userManager.GetClaimsAsync(user);
        return claims.FirstOrDefault(claim => claim.Type == "FullName")?.Value;
    }

    private static async Task<bool> IsEmailConfirmedAsync(
        EmailConfirmationTestWebApplicationFactory factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Users.SingleAsync(x => x.Email == email)).EmailConfirmed;
    }

    private static async Task<int> CountCredentialsAsync(
        EmailConfirmationTestWebApplicationFactory factory,
        string email,
        UserRoleType role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        return await db.UserRoleCredentials.CountAsync(x => x.UserId == user.Id && x.Role == role);
    }

    private static string ExtractPromotionToken(string html)
    {
        var match = Regex.Match(html, "#promotionToken=(?<token>[a-zA-Z0-9_-]+)");
        match.Success.Should().BeTrue("the promotion e-mail must contain the derived confirmation token");
        return match.Groups["token"].Value;
    }
}
