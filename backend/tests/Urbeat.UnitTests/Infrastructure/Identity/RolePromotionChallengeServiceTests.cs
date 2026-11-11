using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Identity;

/// <summary>
/// The role promotion challenge is a separate purpose from e-mail confirmation and e-mail change.
/// It must never persist or log the raw token or the plain password, must expire after 30 minutes
/// (UTC), must invalidate a previous pending request and must be applied exactly once.
/// </summary>
public sealed class RolePromotionChallengeServiceTests
{
    private const string Email = "cliente@urbeat.local";
    private const string PasswordHash = "AQAAAAIAAYagAAAAEHASHVALUE";
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task CreateAsync_ShouldPersistOnlyTheTokenHash_AndReturnAnOpaqueToken()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var created = await sut.CreateAsync(BuildData());

        created.Token.Should().NotBeNullOrWhiteSpace();
        created.RequestId.Should().NotBe(Guid.Empty);
        created.ExpiresAtUtc.Should().Be(time.UtcNow.UtcDateTime.AddMinutes(30));
        created.Token.Should().MatchRegex("^[A-Za-z0-9_-]+$");

        var stored = await db.RolePromotionRequests.SingleAsync();
        stored.TokenHash.Should().NotBe(created.Token);
        stored.TokenHash.Should().HaveLength(64);
        stored.NormalizedEmail.Should().Be(Email);
        stored.TargetRole.Should().Be(UserRoleType.Seller);
        stored.ConsumedAtUtc.Should().BeNull();
        stored.SupersededAtUtc.Should().BeNull();
        stored.ClaimedAtUtc.Should().BeNull();
        stored.EmailSentAtUtc.Should().BeNull();
        stored.PasswordHash.Should().Be(PasswordHash);
        stored.PasswordHash.Should().NotContain("Senha@123");
    }

    [Fact]
    public async Task TryClaimEmailDeliveryAsync_ShouldRebuildTheSameTokenWithoutAnyCache()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        // A different service instance models a restart: nothing was cached in process.
        var restarted = CreateService(db, time, signingKey: TestSigningKey);
        var delivery = await restarted.TryClaimEmailDeliveryAsync(created.RequestId);

        delivery.Should().NotBeNull();
        delivery!.Token.Should().Be(created.Token);
        delivery.Email.Should().Be(Email);
        delivery.TargetRole.Should().Be(UserRoleType.Seller);
        delivery.ExpiresAtUtc.Should().Be(created.ExpiresAtUtc);
        delivery.AttemptId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task TryClaimEmailDeliveryAsync_ShouldStillWorkWhenTheJobRunsLateWithinTheTtl()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        // A delayed job (25 of the 30 minutes elapsed) must still be able to build the link.
        time.UtcNow = time.UtcNow.AddMinutes(25);

        var delivery = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        delivery.Should().NotBeNull();
        delivery!.Token.Should().Be(created.Token);

        time.UtcNow = time.UtcNow.AddMinutes(6);
        (await sut.TryClaimEmailDeliveryAsync(created.RequestId)).Should().BeNull();
    }

    [Fact]
    public async Task TryClaimEmailDeliveryAsync_ShouldNotDeliverAppliedOrExpiredRequests()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var expired = await sut.CreateAsync(BuildData());
        time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.ChallengeTtl).AddMinutes(1);
        var current = await sut.CreateAsync(BuildData());

        (await sut.TryClaimEmailDeliveryAsync(expired.RequestId)).Should().BeNull();

        var validation = await sut.ValidateAndConsumeAsync(current.Token);
        validation.IsValid.Should().BeTrue();
        (await sut.MarkAppliedAsync(current.RequestId, validation.ClaimAttemptId!.Value)).Should().BeTrue();

        (await sut.TryClaimEmailDeliveryAsync(current.RequestId)).Should().BeNull();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldAcceptAFreshToken_AndApplyItOnlyOnce()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var first = await sut.ValidateAndConsumeAsync(created.Token);
        first.IsValid.Should().BeTrue();
        first.Request!.UserId.Should().Be(UserId);
        first.Request.TargetRole.Should().Be(UserRoleType.Seller);

        // Claiming does not consume: the challenge is only applied after the role/credential write.
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().BeNull();

        (await sut.MarkAppliedAsync(created.RequestId, first.ClaimAttemptId!.Value)).Should().BeTrue();

        var second = await sut.ValidateAndConsumeAsync(created.Token);
        second.IsValid.Should().BeFalse();
        second.Status.Should().Be(RolePromotionChallengeStatus.AlreadyConsumed);
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAppliedAsync_ShouldRejectAForeignOrExpiredClaim()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        var validation = await sut.ValidateAndConsumeAsync(created.Token);
        validation.IsValid.Should().BeTrue();
        var claimAttemptId = validation.ClaimAttemptId!.Value;

        // A different attempt id can never close the challenge.
        (await sut.MarkAppliedAsync(created.RequestId, Guid.CreateVersion7())).Should().BeFalse();
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().BeNull();

        // Once the lease expires the original attempt must fail closed even with the right id.
        time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.ClaimLease).AddSeconds(1);
        (await sut.MarkAppliedAsync(created.RequestId, claimAttemptId)).Should().BeFalse();
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldNotStealALiveClaim()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var winner = await sut.ValidateAndConsumeAsync(created.Token);
        winner.IsValid.Should().BeTrue();

        // A concurrent confirmation observes the live lease instead of taking it over.
        var loser = await sut.ValidateAndConsumeAsync(created.Token);

        loser.IsValid.Should().BeFalse();
        loser.Status.Should().Be(RolePromotionChallengeStatus.AlreadyConsumed);

        // The stale attempt cannot release the winner's lease.
        await sut.ReleaseClaimAsync(created.RequestId, Guid.CreateVersion7());
        (await sut.FindAsync(created.RequestId))!.ClaimedBy.Should().Be(winner.ClaimAttemptId);
    }

    [Fact]
    public async Task ReleaseClaimAsync_ShouldAllowAnImmediateRetry()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var first = await sut.ValidateAndConsumeAsync(created.Token);
        first.IsValid.Should().BeTrue();
        await sut.ReleaseClaimAsync(created.RequestId, first.ClaimAttemptId!.Value);

        var retry = await sut.ValidateAndConsumeAsync(created.Token);
        retry.IsValid.Should().BeTrue();
        retry.ClaimAttemptId.Should().NotBe(first.ClaimAttemptId!.Value);
        (await sut.MarkAppliedAsync(created.RequestId, retry.ClaimAttemptId!.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldTakeOverAnExpiredClaim()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        var first = await sut.ValidateAndConsumeAsync(created.Token);
        first.IsValid.Should().BeTrue();

        time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.ClaimLease).AddSeconds(1);
        var takeover = await sut.ValidateAndConsumeAsync(created.Token);

        takeover.IsValid.Should().BeTrue();
        takeover.ClaimAttemptId.Should().NotBe(first.ClaimAttemptId!.Value);

        // The stale attempt can neither apply nor release the new lease.
        (await sut.MarkAppliedAsync(created.RequestId, first.ClaimAttemptId!.Value)).Should().BeFalse();
        await sut.ReleaseClaimAsync(created.RequestId, first.ClaimAttemptId!.Value);
        (await sut.FindAsync(created.RequestId))!.ClaimedBy.Should().Be(takeover.ClaimAttemptId);

        (await sut.MarkAppliedAsync(created.RequestId, takeover.ClaimAttemptId!.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldNotConsumeTheChallengeWhenApplyingFails()
    {
        // Failure between claim and application must leave the challenge retryable, also on the
        // InMemory provider used by the integration tests.
        var databaseName = $"urbeat-role-promotion-{Guid.NewGuid()}";
        using var firstDb = CreateDbContext(databaseName);
        var sut = CreateService(firstDb, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var firstAttempt = await sut.ValidateAndConsumeAsync(created.Token);
        firstAttempt.IsValid.Should().BeTrue();

        // Simulate the role/credential write blowing up after the claim: the failure path releases
        // the lease (the relational transaction rolls it back) and the tracked changes are
        // discarded with the request scope.
        await sut.ReleaseClaimAsync(created.RequestId, firstAttempt.ClaimAttemptId!.Value);
        firstDb.ChangeTracker.Clear();

        await using var retryDb = CreateDbContext(databaseName);
        var retry = CreateService(retryDb, new TestTimeProvider());
        var retried = await retry.ValidateAndConsumeAsync(created.Token);

        retried.IsValid.Should().BeTrue();
        (await retry.MarkAppliedAsync(created.RequestId, retried.ClaimAttemptId!.Value)).Should().BeTrue();
        (await retryDb.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldRejectAnExpiredToken()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        time.UtcNow = time.UtcNow.AddMinutes(31);

        var result = await sut.ValidateAndConsumeAsync(created.Token);

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(RolePromotionChallengeStatus.Expired);
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldRejectATokenFromAnotherPurpose()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());

        // A token shaped like an e-mail change challenge is not a promotion token.
        var result = await sut.ValidateAndConsumeAsync("eyJ1c2VySWQiOiJ4In0.signature");

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(RolePromotionChallengeStatus.Invalid);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldRejectATamperedToken()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var tampered = created.Token[..^1] + (created.Token[^1] == 'A' ? 'B' : 'A');
        var result = await sut.ValidateAndConsumeAsync(tampered);

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(RolePromotionChallengeStatus.Invalid);
        (await db.RolePromotionRequests.SingleAsync()).ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ShouldRejectATokenSignedWithAnotherKey()
    {
        using var db = CreateDbContext();
        var foreign = CreateService(db, new TestTimeProvider(), signingKey: "other-key-with-at-least-32-characters");
        var created = await foreign.CreateAsync(BuildData());

        var sut = CreateService(db, new TestTimeProvider());
        var result = await sut.ValidateAndConsumeAsync(created.Token);

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(RolePromotionChallengeStatus.Invalid);
    }

    [Fact]
    public async Task CreateAsync_ShouldReuseTheActivePendingRequestForTheSameRole()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var first = await sut.CreateAsync(BuildData());
        time.UtcNow = time.UtcNow.AddMinutes(1);

        // A repeated registration with a different password must not replace the pending request:
        // the link already delivered stays valid and the pending password is preserved.
        var second = await sut.CreateAsync(new RolePromotionRequestData
        {
            UserId = UserId,
            Email = Email,
            TargetRole = UserRoleType.Seller,
            PasswordHash = "AQAAAAIAAYagAAAAENEWHASHVALUE",
            FullName = "Outro Contratante"
        });

        second.RequestId.Should().Be(first.RequestId);
        second.Token.Should().Be(first.Token);
        second.ExpiresAtUtc.Should().Be(first.ExpiresAtUtc);
        second.ReusedExisting.Should().BeTrue();
        second.DeliverySuppressed.Should().BeFalse();
        second.EmailDeliveryPending.Should().BeTrue();

        var stored = await db.RolePromotionRequests.SingleAsync();
        stored.PasswordHash.Should().Be(PasswordHash);
        stored.FullName.Should().Be("Contratante Teste");
        stored.SupersededAtUtc.Should().BeNull();
        db.ChangeTracker.HasChanges().Should().BeFalse(
            "reusing the active request must not write anything that a failure could lose");
    }

    [Fact]
    public async Task CreateAsync_ShouldReplaceOnlyTheExpiredPendingRequestForTheSameRole()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var first = await sut.CreateAsync(BuildData());
        time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.ChallengeTtl).AddSeconds(1);
        var second = await sut.CreateAsync(BuildData());

        second.RequestId.Should().NotBe(first.RequestId);
        second.ReusedExisting.Should().BeFalse();
        second.EmailDeliveryPending.Should().BeTrue();

        var firstResult = await sut.ValidateAndConsumeAsync(first.Token);
        var secondResult = await sut.ValidateAndConsumeAsync(second.Token);

        firstResult.IsValid.Should().BeFalse();
        firstResult.Status.Should().Be(RolePromotionChallengeStatus.Superseded);
        secondResult.IsValid.Should().BeTrue();

        var rows = await db.RolePromotionRequests.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].SupersededAtUtc.Should().NotBeNull();
        rows[1].ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ShouldSuppressADifferentRoleRequestDuringTheIdentityCooldown()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var seller = await sut.CreateAsync(BuildData(UserRoleType.Seller));
        var delivery = await sut.TryClaimEmailDeliveryAsync(seller.RequestId);
        delivery.Should().NotBeNull();
        await sut.MarkEmailSentAsync(seller.RequestId, delivery!.AttemptId);

        time.UtcNow = time.UtcNow.AddSeconds(30);

        // The other role is a different request, but the identity just received a promotion e-mail:
        // no second message is emitted inside the cooldown, and nothing about the identity leaks.
        var customer = await sut.CreateAsync(BuildData(UserRoleType.Customer));

        customer.DeliverySuppressed.Should().BeTrue();
        customer.ReusedExisting.Should().BeTrue();
        customer.EmailDeliveryPending.Should().BeFalse();
        customer.RequestId.Should().Be(seller.RequestId);
        (await db.RolePromotionRequests.CountAsync()).Should().Be(1);

        // After the window a legitimate request for the other role is created normally.
        time.UtcNow = time.UtcNow.AddMinutes(3);
        var laterCustomer = await sut.CreateAsync(BuildData(UserRoleType.Customer));

        laterCustomer.DeliverySuppressed.Should().BeFalse();
        laterCustomer.ReusedExisting.Should().BeFalse();
        laterCustomer.EmailDeliveryPending.Should().BeTrue();
        (await db.RolePromotionRequests.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_ShouldKeepTheActiveLinkWhenARepeatCannotPersist()
    {
        var interceptor = new FailNextSaveInterceptor();
        using var db = CreateDbContext(interceptor: interceptor);
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);

        var first = await sut.CreateAsync(BuildData());

        // A repeat request performs no write at all, so an unrelated persistence failure cannot
        // invalidate the link that was already delivered.
        interceptor.Arm();
        var second = await sut.CreateAsync(
            new RolePromotionRequestData
            {
                UserId = UserId,
                Email = Email,
                TargetRole = UserRoleType.Seller,
                PasswordHash = "AQAAAAIAAYagAAAAENEWHASHVALUE",
                FullName = "Outro Contratante"
            });

        second.RequestId.Should().Be(first.RequestId);
        second.Token.Should().Be(first.Token);

        var stored = await db.RolePromotionRequests.SingleAsync();
        stored.SupersededAtUtc.Should().BeNull();
        stored.PasswordHash.Should().Be(PasswordHash);
        db.ChangeTracker.HasChanges().Should().BeFalse(
            "reusing the active request must not write anything that a failure could lose");

        // The create path proves it performed no write; the confirmation claim is a different
        // operation and must not be confused with the armed failure.
        interceptor.Disarm();
        (await sut.ValidateAndConsumeAsync(first.Token)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ComputeIdentityKey_ShouldNotBeReversibleToTheEmail()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());

        var first = sut.ComputeIdentityKey(Email);
        var second = sut.ComputeIdentityKey(Email);
        var otherSut = CreateService(db, new TestTimeProvider(), signingKey: "other-key-with-at-least-32-characters");

        first.Should().Be(second);
        first.Should().NotContain(Email);
        first.Should().NotBe(Email);
        otherSut.ComputeIdentityKey(Email).Should().NotBe(
            first,
            "the log key is bound to the deployment signing key");
    }

    [Fact]
    public async Task CreateAsync_ShouldKeepIndependentPendingRequestsForDifferentRoles()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());

        var seller = await sut.CreateAsync(BuildData(UserRoleType.Seller));
        var customer = await sut.CreateAsync(BuildData(UserRoleType.Customer));

        (await sut.ValidateAndConsumeAsync(seller.Token)).IsValid.Should().BeTrue();
        (await sut.ValidateAndConsumeAsync(customer.Token)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MarkEmailSentAsync_ShouldRemoveTheRequestFromTheRecoverySweep()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        // The grace period avoids racing the original enqueue on the happy path.
        (await sut.FindPendingEmailRequestIdsAsync(RecoveryGrace, RolePromotionChallengeService.EmailSendLease))
            .Should().BeEmpty();

        time.UtcNow = time.UtcNow.AddMinutes(2);
        (await sut.FindPendingEmailRequestIdsAsync(RecoveryGrace, RolePromotionChallengeService.EmailSendLease))
            .Should().ContainSingle().Which.Should().Be(created.RequestId);

        var delivery = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        delivery.Should().NotBeNull();
        await sut.MarkEmailSentAsync(created.RequestId, delivery!.AttemptId);

        (await sut.FindPendingEmailRequestIdsAsync(TimeSpan.Zero, RolePromotionChallengeService.EmailSendLease))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverySweep_ShouldSkipRequestsWithAnActiveDeliveryLease()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var sut = CreateService(db, time);
        var created = await sut.CreateAsync(BuildData());

        time.UtcNow = time.UtcNow.AddMinutes(2);
        var delivery = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        delivery.Should().NotBeNull("the first job wins the lease");

        // While the lease is valid, neither the recovery sweep nor a duplicate job may send again.
        (await sut.FindPendingEmailRequestIdsAsync(RecoveryGrace, RolePromotionChallengeService.EmailSendLease))
            .Should().BeEmpty();
        (await sut.TryClaimEmailDeliveryAsync(created.RequestId)).Should().BeNull(
            "a concurrent or late job must observe the active lease");

        // After the lease expires (crashed worker) recovery may reclaim it.
        time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.EmailSendLease).AddSeconds(1);
        (await sut.FindPendingEmailRequestIdsAsync(RecoveryGrace, RolePromotionChallengeService.EmailSendLease))
            .Should().ContainSingle().Which.Should().Be(created.RequestId);

        var reclaimer = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        reclaimer.Should().NotBeNull();
        reclaimer!.AttemptId.Should().NotBe(delivery!.AttemptId);

        // The stale attempt can no longer record the send or steal the new lease.
        await sut.MarkEmailSentAsync(created.RequestId, delivery.AttemptId);
        (await sut.FindAsync(created.RequestId))!.EmailSentAtUtc.Should().BeNull();

        await sut.ReleaseEmailSendLeaseAsync(created.RequestId, delivery.AttemptId);
        (await sut.FindAsync(created.RequestId))!.EmailSendAttemptId.Should().Be(reclaimer.AttemptId);

        await sut.MarkEmailSentAsync(created.RequestId, reclaimer.AttemptId);
        (await sut.FindAsync(created.RequestId))!.EmailSentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ReleasingTheLease_ShouldLetARetryClaimTheSameRequest()
    {
        using var db = CreateDbContext();
        var sut = CreateService(db, new TestTimeProvider());
        var created = await sut.CreateAsync(BuildData());

        var first = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        first.Should().NotBeNull();

        await sut.ReleaseEmailSendLeaseAsync(created.RequestId, first!.AttemptId);

        var retry = await sut.TryClaimEmailDeliveryAsync(created.RequestId);
        retry.Should().NotBeNull();
        retry!.AttemptId.Should().NotBe(first.AttemptId);
        retry.Token.Should().Be(created.Token);
    }

    private static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(1);
    private const string TestSigningKey = "urbeat-test-role-promotion-signing-key-32chars";

    private static RolePromotionRequestData BuildData(UserRoleType role = UserRoleType.Seller) => new()
    {
        UserId = UserId,
        Email = Email,
        TargetRole = role,
        PasswordHash = PasswordHash,
        FullName = "Contratante Teste"
    };

    private static ApplicationDbContext CreateDbContext(
        string? databaseName = null,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"urbeat-role-promotion-{Guid.NewGuid()}");

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static RolePromotionChallengeService CreateService(
        ApplicationDbContext db,
        TimeProvider time,
        string signingKey = TestSigningKey)
        => new(
            db,
            Options.Create(new RolePromotionChallengeOptions { SigningKey = signingKey }),
            time,
            NullLogger<RolePromotionChallengeService>.Instance);

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    /// <summary>Fails exactly the next <c>SaveChanges</c>, whichever entity it would write.</summary>
    private sealed class FailNextSaveInterceptor : SaveChangesInterceptor
    {
        private int _armed;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public void Disarm() => Interlocked.Exchange(ref _armed, 0);

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowIfArmed()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                throw new InvalidOperationException("Simulated persistence failure while creating a role promotion request.");
            }
        }
    }
}
