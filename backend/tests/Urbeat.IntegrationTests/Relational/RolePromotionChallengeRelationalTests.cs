using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// PostgreSQL enforces the invariants InMemory cannot: a single pending promotion request per
/// identity/role (partial unique index plus the creation advisory lock) and a single confirmation
/// winner (conditional claim). Skipped when Docker is unavailable, like the other Relational tests.
/// </summary>
public sealed class RolePromotionChallengeRelationalTests
{
    private const string SigningKey = "urbeat-relational-role-promotion-signing-key-32chars";

    private readonly ITestOutputHelper _output;

    public RolePromotionChallengeRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ConcurrentCreations_ShouldKeepASinglePendingRequest_AndRejectADuplicatePendingInsert()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = BuildOptions(container);
            var userId = await EnsureUserAsync(options);

            await using var firstDb = new ApplicationDbContext(options);
            await using var secondDb = new ApplicationDbContext(options);

            var firstService = CreateService(firstDb);
            var secondService = CreateService(secondDb);

            var created = await Task.WhenAll(
                firstService.CreateAsync(BuildData(userId)),
                secondService.CreateAsync(BuildData(userId)));

            await using var assertionDb = new ApplicationDbContext(options);
            var rows = await assertionDb.RolePromotionRequests
                .Where(x => x.UserId == userId)
                .ToListAsync();

            // The loser of the creation lock reuses the winner instead of superseding it, so the
            // link that was already delivered stays valid and only one row exists.
            rows.Should().HaveCount(1, "a concurrent creation must reuse the active request");
            rows.Single().ConsumedAtUtc.Should().BeNull();
            rows.Single().SupersededAtUtc.Should().BeNull();
            created.Select(x => x.RequestId).Distinct().Should().ContainSingle();
            created.Select(x => x.Token).Distinct().Should().ContainSingle();
            created.Should().OnlyContain(x => x.ReusedExisting || x.EmailDeliveryPending);

            // Exactly one token is confirmable.
            await using var claimDb = new ApplicationDbContext(options);
            var validation = await CreateService(claimDb).ValidateAndConsumeAsync(created[0].Token);
            validation.IsValid.Should().BeTrue();

            // Defense in depth: the partial unique index rejects a second pending row even if a
            // writer bypasses the service.
            assertionDb.RolePromotionRequests.Add(new RolePromotionRequest
            {
                UserId = userId,
                NormalizedEmail = "promotion@urbeat.local",
                TargetRole = UserRoleType.Seller,
                PasswordHash = "AQAAAAIAAYagAAAAEHASHVALUE",
                TokenHash = new string('F', 64),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });

            Func<Task> duplicate = async () => await assertionDb.SaveChangesAsync();
            await duplicate.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentConfirmations_ShouldAllowOnlyOneWinner()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = BuildOptions(container);
            var userId = await EnsureUserAsync(options);

            Guid requestId;
            string token;
            await using (var setupDb = new ApplicationDbContext(options))
            {
                var created = await CreateService(setupDb).CreateAsync(BuildData(userId));
                requestId = created.RequestId;
                token = created.Token;
            }

            await using var firstDb = new ApplicationDbContext(options);
            await using var secondDb = new ApplicationDbContext(options);

            var results = await Task.WhenAll(
                CreateService(firstDb).ValidateAndConsumeAsync(token),
                CreateService(secondDb).ValidateAndConsumeAsync(token));

            results.Count(result => result.IsValid).Should().Be(1);

            await using var assertionDb = new ApplicationDbContext(options);
            var request = await assertionDb.RolePromotionRequests.SingleAsync(x => x.Id == requestId);
            request.ClaimedAtUtc.Should().NotBeNull("the claim lease is held by the single winner");
            request.ConsumedAtUtc.Should().BeNull("consumption is only recorded after the role is applied");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentEmailClaims_ShouldAllowOnlyOneWinner()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = BuildOptions(container);
            var userId = await EnsureUserAsync(options);

            Guid requestId;
            await using (var setupDb = new ApplicationDbContext(options))
            {
                requestId = (await CreateService(setupDb).CreateAsync(BuildData(userId))).RequestId;
            }

            await using var firstDb = new ApplicationDbContext(options);
            await using var secondDb = new ApplicationDbContext(options);

            var claims = await Task.WhenAll(
                CreateService(firstDb).TryClaimEmailDeliveryAsync(requestId),
                CreateService(secondDb).TryClaimEmailDeliveryAsync(requestId));

            claims.Count(claim => claim is not null).Should().Be(1,
                "the persisted lease serializes duplicate/late e-mail jobs");

            // The recovery sweep must not enqueue a request whose lease is still valid.
            await using var assertionDb = new ApplicationDbContext(options);
            var pending = await CreateService(assertionDb).FindPendingEmailRequestIdsAsync(
                TimeSpan.Zero,
                RolePromotionChallengeService.EmailSendLease);
            pending.Should().NotContain(requestId);

            var request = await assertionDb.RolePromotionRequests.SingleAsync(x => x.Id == requestId);
            request.EmailSendingAtUtc.Should().NotBeNull();
            request.EmailSendAttemptId.Should().NotBeNull();
            request.EmailSentAtUtc.Should().BeNull("the send is only recorded after the provider accepts");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task MarkApplied_ShouldRejectAStaleAttemptAfterTheLeaseIsTakenOver()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = BuildOptions(container);
            var userId = await EnsureUserAsync(options);
            var time = new TestTimeProvider();

            Guid requestId;
            string token;
            await using (var setupDb = new ApplicationDbContext(options))
            {
                var created = await CreateService(setupDb, time).CreateAsync(BuildData(userId));
                requestId = created.RequestId;
                token = created.Token;
            }

            Guid firstClaim;
            await using (var firstDb = new ApplicationDbContext(options))
            {
                var first = await CreateService(firstDb, time).ValidateAndConsumeAsync(token);
                first.IsValid.Should().BeTrue();
                firstClaim = first.ClaimAttemptId!.Value;
            }

            // The first attempt stalls past the lease and another attempt takes the challenge over.
            time.UtcNow = time.UtcNow.Add(RolePromotionChallengeService.ClaimLease).AddSeconds(1);

            Guid secondClaim;
            await using (var takeoverDb = new ApplicationDbContext(options))
            {
                var takeover = await CreateService(takeoverDb, time).ValidateAndConsumeAsync(token);
                takeover.IsValid.Should().BeTrue();
                takeover.ClaimAttemptId.Should().NotBe(firstClaim);
                secondClaim = takeover.ClaimAttemptId!.Value;
            }

            // The stale attempt can no longer mark the challenge as applied.
            await using (var staleDb = new ApplicationDbContext(options))
            {
                (await CreateService(staleDb, time).MarkAppliedAsync(requestId, firstClaim)).Should().BeFalse();
            }

            await using (var assertionDb = new ApplicationDbContext(options))
            {
                var request = await assertionDb.RolePromotionRequests.SingleAsync(x => x.Id == requestId);
                request.ConsumedAtUtc.Should().BeNull();
                request.ClaimedBy.Should().Be(secondClaim);
            }

            // Only the current lease owner can close it.
            await using (var winnerDb = new ApplicationDbContext(options))
            {
                (await CreateService(winnerDb, time).MarkAppliedAsync(requestId, secondClaim)).Should().BeTrue();
            }

            await using (var finalDb = new ApplicationDbContext(options))
            {
                var request = await finalDb.RolePromotionRequests.SingleAsync(x => x.Id == requestId);
                request.ConsumedAtUtc.Should().NotBeNull();
                request.ClaimedAtUtc.Should().BeNull();
                request.ClaimedBy.Should().BeNull();
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static DbContextOptions<ApplicationDbContext> BuildOptions(PostgreSqlContainer container)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;

    private static async Task<Guid> EnsureUserAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var setupDb = new ApplicationDbContext(options);
        await setupDb.Database.EnsureCreatedAsync();

        var email = $"promotion.{Guid.NewGuid():N}@urbeat.local";
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true
        };

        setupDb.Users.Add(user);
        await setupDb.SaveChangesAsync();
        return user.Id;
    }

    private static RolePromotionChallengeService CreateService(
        ApplicationDbContext db,
        TimeProvider? timeProvider = null)
        => new(
            db,
            Options.Create(new RolePromotionChallengeOptions { SigningKey = SigningKey }),
            timeProvider ?? TimeProvider.System,
            NullLogger<RolePromotionChallengeService>.Instance);

    private static RolePromotionRequestData BuildData(Guid userId) => new()
    {
        UserId = userId,
        Email = "promotion@urbeat.local",
        TargetRole = UserRoleType.Seller,
        PasswordHash = "AQAAAAIAAYagAAAAEHASHVALUE",
        FullName = "Contratante Relacional"
    };

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
