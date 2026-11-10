using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using System.Reflection;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class CustomerOtpServiceTests
{
    [Fact]
    public async Task StartAsync_uses_store_phone_as_sender_and_persists_hash_only()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(),
            Name = "Loja",
            Slug = "loja",
            PhoneNumber = "22999990000",
            IsOpen = true
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sender = new RecordingVerificationMessageSender();
        var service = CreateService(db, sender);

        var response = await service.StartAsync(BuildStartRequest(store.Id), CancellationToken.None);

        response.VerificationId.Should().NotBeEmpty();
        sender.FromPhone.Should().Be("22999990000");
        sender.ToPhone.Should().Be("22988887777");
        sender.Code.Should().MatchRegex("^\\d{4}$");

        var verification = await db.CustomerPhoneVerifications.SingleAsync();
        verification.CodeHash.Should().NotBe(sender.Code);
        verification.CodeHash.Should().NotBeNullOrWhiteSpace();
        verification.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(45));
        var fullNameClaim = await db.UserClaims.SingleAsync(x => x.ClaimType == "FullName");
        fullNameClaim.ClaimValue.Should().Be("Maria Oliveira");

        var credential = await db.UserRoleCredentials
            .SingleAsync(x => x.Role == UserRoleType.Customer);
        credential.PasswordHash.Should().NotBeNullOrWhiteSpace();
        credential.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_rejects_wrong_code_and_tracks_attempts()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(),
            Name = "Loja",
            Slug = "loja",
            PhoneNumber = "22999990000",
            IsOpen = true
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sender = new RecordingVerificationMessageSender();
        var service = CreateService(db, sender);
        var started = await service.StartAsync(BuildStartRequest(store.Id), CancellationToken.None);

        (await db.CustomerAddresses.CountAsync()).Should().Be(0);

        var result = await service.ConfirmAsync(new ConfirmCustomerVerificationRequestDto
        {
            VerificationId = started.VerificationId,
            Code = "000000"
        }, CancellationToken.None);

        result.Response.Succeeded.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("INVALID_CODE");
        (await db.CustomerPhoneVerifications.SingleAsync()).Attempts.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmAsync_accepts_correct_code_and_returns_token_and_address()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(),
            Name = "Loja",
            Slug = "loja",
            PhoneNumber = "22999990000",
            IsOpen = true
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var sender = new RecordingVerificationMessageSender();
        var service = CreateService(db, sender);
        var started = await service.StartAsync(BuildStartRequest(store.Id), CancellationToken.None);

        var result = await service.ConfirmAsync(new ConfirmCustomerVerificationRequestDto
        {
            VerificationId = started.VerificationId,
            Code = sender.Code
        }, CancellationToken.None);

        result.Response.Succeeded.Should().BeTrue();
        result.Response.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
        result.Response.CustomerAddressId.Should().NotBeNull();
        (await db.CustomerAddresses.CountAsync()).Should().Be(1);
        (await db.CustomerPhoneVerifications.SingleAsync()).ConfirmedAtUtc.Should().NotBeNull();
        var refreshToken = await db.RefreshTokens.SingleAsync();
        refreshToken.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task CreateCustomerSession_creates_customer_and_persists_session_graph_in_one_final_unit_of_work()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(), Name = "Loja", Slug = "loja",
            PhoneNumber = "22999990000", IsOpen = true
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var service = CreateService(db, new RecordingVerificationMessageSender());
        var result = await service.CreateCustomerSessionAsync(BuildStartRequest(store.Id));

        result.Response.Succeeded.Should().BeTrue();
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.Roles.CountAsync(x => x.NormalizedName == "CUSTOMER")).Should().Be(1);
        (await db.UserRoles.CountAsync()).Should().Be(1);
        (await db.UserRoleCredentials.CountAsync(x => x.Role == UserRoleType.Customer)).Should().Be(1);
        (await db.StoreCustomers.CountAsync()).Should().Be(1);
        (await db.UserClaims.CountAsync(x => x.ClaimType == "FullName")).Should().Be(1);
        (await db.CustomerAddresses.CountAsync(x => x.IsPrimary)).Should().Be(1);
    }

    [Fact]
    public async Task CreateCustomerSession_updates_existing_claim_and_keeps_only_new_address_primary()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(), Name = "Loja", Slug = "loja",
            PhoneNumber = "22999990000", IsOpen = true
        };
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(), UserName = "maria@email.com", Email = "maria@email.com",
            NormalizedEmail = "MARIA@EMAIL.COM", NormalizedUserName = "MARIA@EMAIL.COM",
            EmailConfirmed = true, PasswordHash = "existing-hash", PhoneNumber = "22911112222"
        };
        db.Stores.Add(store);
        db.Users.Add(user);
        db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = user.Id, ClaimType = "FullName", ClaimValue = "Nome antigo"
        });
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = user.Id, Cep = "28000000", Street = "Rua antiga", Number = "1",
            Neighborhood = "Centro", City = "Campos", State = "RJ", IsPrimary = true
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new RecordingVerificationMessageSender());
        var result = await service.CreateCustomerSessionAsync(BuildStartRequest(store.Id));

        result.Response.Succeeded.Should().BeTrue();
        (await db.UserClaims.SingleAsync(x => x.UserId == user.Id && x.ClaimType == "FullName"))
            .ClaimValue.Should().Be("Maria Oliveira");
        (await db.CustomerAddresses.CountAsync(x => x.UserId == user.Id && x.IsPrimary)).Should().Be(1);
        (await db.CustomerAddresses.CountAsync(x => x.UserId == user.Id)).Should().Be(2);
        (await db.StoreCustomers.CountAsync()).Should().Be(1);
        (await db.UserRoles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateCustomerSession_uses_the_user_manager_lookup_normalizer()
    {
        await using var db = CreateDbContext();
        var store = new Store
        {
            OwnerUserId = Guid.CreateVersion7(), Name = "Loja", Slug = "loja",
            PhoneNumber = "22999990000", IsOpen = true
        };
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(), UserName = "Maria@Email.com", Email = "Maria@Email.com",
            NormalizedEmail = "configured:maria@email.com", NormalizedUserName = "configured:maria@email.com",
            EmailConfirmed = true, PasswordHash = "existing-hash"
        };
        db.Stores.Add(store);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(db, new RecordingVerificationMessageSender(), new PrefixLookupNormalizer());
        var result = await service.CreateCustomerSessionAsync(BuildStartRequest(store.Id));

        result.Response.Succeeded.Should().BeTrue();
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.Users.SingleAsync()).Id.Should().Be(user.Id);
    }

    [Fact]
    public void CustomerSession_unique_violation_detection_does_not_match_unrelated_constraints()
    {
        var exception = new DbUpdateException("unexpected", new PostgresException(
            "duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_AnotherTable_AnotherColumn"));

        var method = typeof(CustomerOtpService).GetMethod(
            "IsCustomerSessionConcurrencyViolation", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [exception]).Should().Be(false);
    }

    // The unit suite uses EF InMemory, which cannot produce PostgreSQL commit failures or
    // Identity unique-index races. Keep those provider-specific decisions covered by focused
    // classification tests; the relational integration suite remains the place for live races.
    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public void CustomerSession_serialization_detection_accepts_direct_postgres_exception(string sqlState)
    {
        var exception = new PostgresException("transaction conflict", "ERROR", "ERROR", sqlState);

        var method = typeof(CustomerOtpService).GetMethod(
            "IsSerializationFailure", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [exception]).Should().Be(true);
    }

    [Theory]
    [InlineData("UserNameIndex")]
    [InlineData("EmailIndex")]
    public void CustomerSession_unique_violation_detection_accepts_identity_user_indexes(string constraintName)
    {
        var exception = new DbUpdateException("unexpected", new PostgresException(
            "duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName));

        var method = typeof(CustomerOtpService).GetMethod(
            "IsCustomerSessionConcurrencyViolation", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [exception]).Should().Be(true);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CustomerOtpService CreateService(
        ApplicationDbContext db,
        RecordingVerificationMessageSender sender,
        ILookupNormalizer? lookupNormalizer = null)
    {
        var userStore = new Mock<IUserStore<IdentityUser<Guid>>>();
        var userEmailStore = userStore.As<IUserEmailStore<IdentityUser<Guid>>>();
        var userPasswordStore = userStore.As<IUserPasswordStore<IdentityUser<Guid>>>();
        userPasswordStore.Setup(x => x.SetPasswordHashAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, string? hash, CancellationToken _) => user.PasswordHash = hash)
            .Returns(Task.CompletedTask);
        userPasswordStore.Setup(x => x.GetPasswordHashAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.PasswordHash);
        userPasswordStore.Setup(x => x.HasPasswordAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.PasswordHash is not null);
        userEmailStore.Setup(x => x.GetEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.Email);
        userEmailStore.Setup(x => x.SetEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, string? email, CancellationToken _) => user.Email = email)
            .Returns(Task.CompletedTask);
        userEmailStore.Setup(x => x.GetNormalizedEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.NormalizedEmail);
        userEmailStore.Setup(x => x.SetNormalizedEmailAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, string? email, CancellationToken _) => user.NormalizedEmail = email)
            .Returns(Task.CompletedTask);
        userStore.Setup(x => x.GetUserIdAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.Id.ToString());
        userStore.Setup(x => x.GetUserNameAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.UserName);
        userStore.Setup(x => x.SetUserNameAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, string? name, CancellationToken _) => user.UserName = name)
            .Returns(Task.CompletedTask);
        userStore.Setup(x => x.GetNormalizedUserNameAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser<Guid> user, CancellationToken _) => user.NormalizedUserName);
        userStore.Setup(x => x.SetNormalizedUserNameAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, string? name, CancellationToken _) => user.NormalizedUserName = name)
            .Returns(Task.CompletedTask);
        userStore.Setup(x => x.CreateAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityUser<Guid> user, CancellationToken _) => db.Users.Add(user))
            .ReturnsAsync(IdentityResult.Success);
        userStore.Setup(x => x.UpdateAsync(It.IsAny<IdentityUser<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        userStore.Setup(x => x.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => db.Users.SingleOrDefault(x => x.Id.ToString() == id));
        userEmailStore.Setup(x => x.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string email, CancellationToken _) => db.Users.SingleOrDefault(x => x.NormalizedEmail == email));

        var userManager = new UserManager<IdentityUser<Guid>>(
            userStore.Object,
            null,
            new PasswordHasher<IdentityUser<Guid>>(),
            Array.Empty<IUserValidator<IdentityUser<Guid>>>(),
            Array.Empty<IPasswordValidator<IdentityUser<Guid>>>(),
            lookupNormalizer ?? new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<IdentityUser<Guid>>>.Instance);

        var tokenService = new Mock<IJwtTokenService>();
        tokenService.Setup(x => x.GenerateToken(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(new AuthTokenPairDto
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            });

        return new CustomerOtpService(db, userManager, tokenService.Object, sender, NullLogger<CustomerOtpService>.Instance);
    }

    private static StartCustomerVerificationRequestDto BuildStartRequest(Guid storeId) => new()
    {
        StoreId = storeId,
        Customer = new CustomerVerificationCustomerDto
        {
            FullName = "Maria Oliveira",
            Email = "maria@email.com",
            PhoneNumber = "(22) 98888-7777"
        },
        Address = new CustomerVerificationAddressDto
        {
            Cep = "28000000",
            Street = "Rua Principal",
            Number = "123",
            Neighborhood = "Centro",
            City = "Campos",
            State = "RJ"
        }
    };

    private sealed class RecordingVerificationMessageSender : ICustomerVerificationMessageSender
    {
        public CustomerVerificationChannel Channel => CustomerVerificationChannel.Sms;

        public string? FromPhone { get; private set; }
        public string? ToPhone { get; private set; }
        public string? Code { get; private set; }

        public Task SendOtpAsync(string fromPhone, string toPhone, string code, CancellationToken cancellationToken)
        {
            FromPhone = fromPhone;
            ToPhone = toPhone;
            Code = code;
            return Task.CompletedTask;
        }
    }

    private sealed class PrefixLookupNormalizer : ILookupNormalizer
    {
        public string? NormalizeEmail(string? email) => email is null ? null : $"configured:{email.ToLowerInvariant()}";

        public string? NormalizeName(string? name) => name is null ? null : $"configured:{name.ToLowerInvariant()}";
    }
}
