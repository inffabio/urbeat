using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// The per-role credential table relies on PostgreSQL semantics the InMemory provider cannot
/// reproduce: the unique (UserId, Role) index and enum-to-string conversion. Backfill must be
/// idempotent against the real provider and a duplicate must be rejected. Skipped when Docker
/// is unavailable, matching the other Relational tests.
/// </summary>
public sealed class UserRoleCredentialRelationalTests
{
    private readonly ITestOutputHelper _output;

    public UserRoleCredentialRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Backfill_ShouldCreateTwoCredentials_AndUniqueIndexShouldRejectDuplicate()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            var hasher = new PasswordHasher<IdentityUser<Guid>>();
            var email = "dual@urbeat.local";
            var legacyHash = hasher.HashPassword(new IdentityUser<Guid>(), "SenhaForte123");

            Guid userId;
            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var user = new IdentityUser<Guid>
                {
                    Id = Guid.CreateVersion7(),
                    UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(),
                    Email = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    PasswordHash = legacyHash
                };
                var customerRole = new IdentityRole<Guid>
                {
                    Id = Guid.CreateVersion7(),
                    Name = "Customer",
                    NormalizedName = "CUSTOMER"
                };
                var sellerRole = new IdentityRole<Guid>
                {
                    Id = Guid.CreateVersion7(),
                    Name = "Seller",
                    NormalizedName = "SELLER"
                };

                setupContext.Users.Add(user);
                setupContext.Roles.AddRange(customerRole, sellerRole);
                setupContext.UserRoles.AddRange(
                    new IdentityUserRole<Guid> { UserId = user.Id, RoleId = customerRole.Id },
                    new IdentityUserRole<Guid> { UserId = user.Id, RoleId = sellerRole.Id });
                await setupContext.SaveChangesAsync();
                userId = user.Id;
            }

            await using (var backfillContext = new ApplicationDbContext(options))
            {
                var service = new UserRoleCredentialBackfillService(
                    backfillContext,
                    NullLogger<UserRoleCredentialBackfillService>.Instance);

                (await service.BackfillAsync()).Created.Should().Be(2);
                (await service.BackfillAsync()).Created.Should().Be(0);
            }

            await using (var assertionContext = new ApplicationDbContext(options))
            {
                var credentials = await assertionContext.UserRoleCredentials
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                credentials.Should().HaveCount(2);
                credentials.Select(x => x.Role).Should().BeEquivalentTo(
                    new[] { UserRoleType.Customer, UserRoleType.Seller });
                credentials.Should().OnlyContain(x => x.PasswordHash == legacyHash);

                assertionContext.UserRoleCredentials.Add(new UserRoleCredential
                {
                    UserId = userId,
                    Role = UserRoleType.Customer,
                    PasswordHash = legacyHash
                });

                Func<Task> duplicate = async () => await assertionContext.SaveChangesAsync();
                await duplicate.Should().ThrowAsync<DbUpdateException>();
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
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
