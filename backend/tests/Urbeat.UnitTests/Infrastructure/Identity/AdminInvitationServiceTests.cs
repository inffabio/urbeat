using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Identity;

/// <summary>
/// Token derivation and leases of administrative invitation links. Only the hash is persisted, the
/// same id always maps to the same token, and purposes are cryptographically separated.
/// </summary>
public sealed class AdminInvitationServiceTests
{
    private const string SigningKey = "urbeat-unit-tests-admin-account-signing-key-2026";

    [Fact]
    public async Task Token_ShouldBeDeterministic_PerIdAndPurpose_AndNeverPersisted()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateService(db);

        var invitation = await service.CreateAsync(
            Guid.CreateVersion7(),
            "novo@exemplo.com",
            AdminInvitationPurpose.AccountInvitation,
            null);

        var first = service.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation);
        var second = service.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation);
        var reset = service.CreateToken(invitation.Id, AdminInvitationPurpose.PasswordReset);

        first.Should().Be(second);
        reset.Should().NotBe(first);

        var stored = await db.AdminInvitations.SingleAsync();
        stored.TokenHash.Should().NotContain(first);
        var hasher = new TokenHasher();
        hasher.Verify(first, stored.TokenHash).Should().BeTrue();
        hasher.Verify(reset, stored.TokenHash).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveState_ShouldReflectEveryTerminalState()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateService(db);
        var now = DateTime.UtcNow;

        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddHours(1) },
            now).Should().Be(AdminInvitationState.Pending);
        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddHours(1), EmailSendingAtUtc = now.AddMinutes(-1) },
            now).Should().Be(AdminInvitationState.Sending);
        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddMinutes(-1) },
            now).Should().Be(AdminInvitationState.Expired);
        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddHours(1), ConsumedAtUtc = now },
            now).Should().Be(AdminInvitationState.Accepted);
        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddHours(1), RevokedAtUtc = now },
            now).Should().Be(AdminInvitationState.Revoked);
        service.ResolveState(
            new AdminInvitation { ExpiresAtUtc = now.AddHours(1), SupersededAtUtc = now },
            now).Should().Be(AdminInvitationState.Superseded);
    }

    [Fact]
    public async Task Claim_ShouldAllowOnlyOneAttempt_AndReleaseOnlyByItsOwner()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateService(db);
        var invitation = await service.CreateAsync(
            Guid.CreateVersion7(),
            "claim@exemplo.com",
            AdminInvitationPurpose.AccountInvitation,
            null);
        var token = service.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation);

        var first = await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation);
        first.IsValid.Should().BeTrue();

        var second = await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation);
        second.IsValid.Should().BeFalse();
        second.Status.Should().Be(AdminInvitationClaimStatus.AlreadyConsumed);

        await service.ReleaseClaimAsync(invitation.Id, Guid.NewGuid());
        var stillClaimed = await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation);
        stillClaimed.IsValid.Should().BeFalse();

        await service.ReleaseClaimAsync(invitation.Id, first.ClaimAttemptId!.Value);
        var retried = await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation);
        retried.IsValid.Should().BeTrue();

        var consumed = await service.MarkConsumedAsync(invitation.Id, first.ClaimAttemptId!.Value);
        consumed.Should().BeFalse("the second attempt owns the lease now");

        var consumedByOwner = await service.MarkConsumedAsync(
            invitation.Id,
            retried.ClaimAttemptId!.Value);
        consumedByOwner.Should().BeTrue();

        var afterConsume = await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation);
        afterConsume.Status.Should().Be(AdminInvitationClaimStatus.AlreadyConsumed);
    }

    [Fact]
    public async Task Claim_ShouldRejectExpiredRevokedSuperseded_AndSwappedPurpose()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateService(db);
        var invitation = await service.CreateAsync(
            Guid.CreateVersion7(),
            "estado@exemplo.com",
            AdminInvitationPurpose.AccountInvitation,
            null);
        var token = service.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation);

        (await service.TryClaimAsync(token, AdminInvitationPurpose.PasswordReset)).Status
            .Should().Be(AdminInvitationClaimStatus.Invalid);

        var stored = await db.AdminInvitations.SingleAsync();
        stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        (await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation)).Status
            .Should().Be(AdminInvitationClaimStatus.Expired);

        stored.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        stored.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        (await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation)).Status
            .Should().Be(AdminInvitationClaimStatus.Revoked);

        stored.RevokedAtUtc = null;
        stored.SupersededAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        (await service.TryClaimAsync(token, AdminInvitationPurpose.AccountInvitation)).Status
            .Should().Be(AdminInvitationClaimStatus.Superseded);
    }

    [Fact]
    public async Task EmailLease_ShouldAllowOnlyOneDelivery_AndBecomeDeliverableAgainAfterRelease()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateService(db);
        var invitation = await service.CreateAsync(
            Guid.CreateVersion7(),
            "entrega@exemplo.com",
            AdminInvitationPurpose.AccountInvitation,
            null);

        var first = await service.TryClaimEmailDeliveryAsync(invitation.Id);
        first.Should().NotBeNull();
        first!.Token.Should().Be(service.CreateToken(invitation.Id, AdminInvitationPurpose.AccountInvitation));

        (await service.TryClaimEmailDeliveryAsync(invitation.Id)).Should().BeNull();

        await service.ReleaseEmailSendLeaseAsync(invitation.Id, first.AttemptId);
        var retried = await service.TryClaimEmailDeliveryAsync(invitation.Id);
        retried.Should().NotBeNull();

        await service.MarkEmailSentAsync(invitation.Id, retried!.AttemptId);
        (await service.TryClaimEmailDeliveryAsync(invitation.Id)).Should().BeNull();
        (await db.AdminInvitations.SingleAsync()).EmailSentAtUtc.Should().NotBeNull();
    }

    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();
    private const string DatabaseName = "urbeat-admin-invitations-shared";
    private static readonly SemaphoreSlim ResetLock = new(1, 1);

    private static async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(DatabaseName, DatabaseRoot)
            .Options;
        var db = new ApplicationDbContext(options);
        await ResetLock.WaitAsync();
        try
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
        finally
        {
            ResetLock.Release();
        }

        return db;
    }

    private static AdminInvitationService CreateService(ApplicationDbContext db)
        => new(
            db,
            new TokenHasher(),
            Options.Create(new AdminAccountOptions()),
            Options.Create(new RolePromotionChallengeOptions { SigningKey = SigningKey }),
            TimeProvider.System,
            NullLogger<AdminInvitationService>.Instance);
}
