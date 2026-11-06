using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Identity;

public sealed class UserRoleCredentialBackfillServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly UserRoleCredentialBackfillService _sut;

    public UserRoleCredentialBackfillServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-credential-backfill-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new UserRoleCredentialBackfillService(_db, NullLogger<UserRoleCredentialBackfillService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Backfill_ShouldCreateOneCredentialPerRole_AndBeIdempotent()
    {
        var hasher = new PasswordHasher<IdentityUser<Guid>>();
        var customer = CreateUser("customer@urbeat.local", "CustomerPassword123", hasher);
        var seller = CreateUser("seller@urbeat.local", "SellerPassword123", hasher);
        var dual = CreateUser("dual@urbeat.local", "DualPassword123", hasher);

        AddUserWithRoles(customer, "Customer");
        AddUserWithRoles(seller, "Seller");
        AddUserWithRoles(dual, "Customer", "Seller");
        await _db.SaveChangesAsync();

        var firstRun = await _sut.BackfillAsync();

        firstRun.Created.Should().Be(4);
        firstRun.SkippedMissingPassword.Should().Be(0);
        (await _db.UserRoleCredentials.CountAsync()).Should().Be(4);

        var customerCredential = await _db.UserRoleCredentials
            .SingleAsync(x => x.UserId == customer.Id && x.Role == UserRoleType.Customer);
        customerCredential.PasswordHash.Should().Be(customer.PasswordHash);

        var dualCredentials = await _db.UserRoleCredentials
            .Where(x => x.UserId == dual.Id)
            .ToListAsync();
        dualCredentials.Should().HaveCount(2);
        dualCredentials.Should().OnlyContain(x => x.PasswordHash == dual.PasswordHash);

        // A second startup must never duplicate or overwrite anything.
        var secondRun = await _sut.BackfillAsync();
        secondRun.Created.Should().Be(0);
        secondRun.SkippedExisting.Should().Be(4);
        (await _db.UserRoleCredentials.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Backfill_ShouldNotOverwriteAnExistingCredential()
    {
        var hasher = new PasswordHasher<IdentityUser<Guid>>();
        var user = CreateUser("existing@urbeat.local", "OldPassword123", hasher);
        AddUserWithRoles(user, "Customer");
        await _db.SaveChangesAsync();

        await _sut.BackfillAsync();

        var credential = await _db.UserRoleCredentials.SingleAsync();
        credential.PasswordHash = "custom-already-migrated-hash";
        await _db.SaveChangesAsync();

        var result = await _sut.BackfillAsync();

        result.Created.Should().Be(0);
        (await _db.UserRoleCredentials.SingleAsync()).PasswordHash.Should().Be("custom-already-migrated-hash");
    }

    [Fact]
    public async Task Backfill_ShouldSkipUserWithoutLegacyPassword_AndKeepTheAccount()
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "no-hash@urbeat.local",
            NormalizedUserName = "NO-HASH@URBEAT.LOCAL",
            Email = "no-hash@urbeat.local",
            NormalizedEmail = "NO-HASH@URBEAT.LOCAL"
        };
        AddUserWithRoles(user, "Customer");
        await _db.SaveChangesAsync();

        var result = await _sut.BackfillAsync();

        result.Created.Should().Be(0);
        result.SkippedMissingPassword.Should().Be(1);
        (await _db.UserRoleCredentials.AnyAsync()).Should().BeFalse();
        (await _db.Users.AnyAsync(x => x.Id == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_ShouldIgnoreAdminAndDeliveryDriverRoles()
    {
        var hasher = new PasswordHasher<IdentityUser<Guid>>();
        var admin = CreateUser("admin-only@urbeat.local", "AdminPassword123", hasher);
        AddUserWithRoles(admin, "Admin", "DeliveryDriver");
        await _db.SaveChangesAsync();

        var result = await _sut.BackfillAsync();

        result.Created.Should().Be(0);
        (await _db.UserRoleCredentials.AnyAsync()).Should().BeFalse();
    }

    private static IdentityUser<Guid> CreateUser(
        string email,
        string password,
        PasswordHasher<IdentityUser<Guid>> hasher)
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private void AddUserWithRoles(IdentityUser<Guid> user, params string[] roleNames)
    {
        _db.Users.Add(user);

        foreach (var roleName in roleNames)
        {
            var normalized = roleName.ToUpperInvariant();
            var role = _db.Roles.Local.SingleOrDefault(x => x.NormalizedName == normalized);
            if (role is null)
            {
                role = new IdentityRole<Guid>
                {
                    Id = Guid.CreateVersion7(),
                    Name = roleName,
                    NormalizedName = normalized
                };
                _db.Roles.Add(role);
            }

            _db.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }
    }
}
