using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Npgsql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Urbeat.UnitTests.WebApi;

public sealed class CustomerControllerTests
{
    [Fact]
    public async Task GetCurrentCustomer_ShouldReturnProfileWithPrimaryAddress()
    {
        await using var db = CreateDbContext();
        var userId = Guid.CreateVersion7();
        db.Users.Add(new IdentityUser<Guid>
        {
            Id = userId,
            Email = "maria@email.com",
            UserName = "maria@email.com",
            PhoneNumber = "22999999999"
        });
        db.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = userId,
            ClaimType = "FullName",
            ClaimValue = "Maria Oliveira"
        });
        var address = new CustomerAddress
        {
            UserId = userId,
            Cep = "28000000",
            Street = "Rua Principal",
            Number = "123",
            Neighborhood = "Centro",
            City = "Campos",
            State = "RJ",
            IsPrimary = true
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var method = typeof(CustomerController).GetMethod("GetCurrentCustomer", BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();

        var controller = Activator.CreateInstance(typeof(CustomerController), db).Should().BeOfType<CustomerController>().Subject;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "test"))
            }
        };

        var task = method!.Invoke(controller, [CancellationToken.None]).Should().BeAssignableTo<Task<IActionResult>>().Subject;
        var result = await task;

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            FullName = "Maria Oliveira",
            Email = "maria@email.com",
            PhoneNumber = "22999999999",
            PrimaryAddressId = address.Id
        });
    }

    [Fact]
    public async Task UpdateCurrentCustomer_ShouldPersistProfileAndNameClaim()
    {
        await using var db = CreateDbContext();
        var userId = SeedUser(db, "maria@email.com", "22999999999", "Maria Oliveira");

        var controller = CreateController(db, userId);
        var result = await controller.UpdateCurrentCustomer(
            new UpdateCustomerProfileRequestDto
            {
                FullName = "Maria Atualizada",
                Email = "maria.nova@email.com",
                PhoneNumber = "22988887777"
            },
            CreateUserManager(db),
            new UpdateCustomerProfileRequestDtoValidator(),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            FullName = "Maria Atualizada",
            Email = "maria.nova@email.com",
            PhoneNumber = "22988887777",
            PrimaryAddressId = (Guid?)null
        });

        var persisted = await db.Users.SingleAsync();
        persisted.Email.Should().Be("maria.nova@email.com");
        persisted.UserName.Should().Be("maria.nova@email.com");
        persisted.NormalizedEmail.Should().Be("MARIA.NOVA@EMAIL.COM");
        persisted.NormalizedUserName.Should().Be("MARIA.NOVA@EMAIL.COM");
        persisted.PhoneNumber.Should().Be("22988887777");

        var claim = await db.UserClaims.SingleAsync(x => x.UserId == userId && x.ClaimType == "FullName");
        claim.ClaimValue.Should().Be("Maria Atualizada");
    }

    [Fact]
    public async Task UpdateCurrentCustomer_ShouldCreateFullNameClaim_WhenAbsent()
    {
        await using var db = CreateDbContext();
        var userId = SeedUser(db, "maria@email.com", "22999999999", fullName: null);

        var controller = CreateController(db, userId);
        var result = await controller.UpdateCurrentCustomer(
            new UpdateCustomerProfileRequestDto
            {
                FullName = "Maria Oliveira",
                Email = "maria@email.com",
                PhoneNumber = "22999999999"
            },
            CreateUserManager(db),
            new UpdateCustomerProfileRequestDtoValidator(),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var claim = await db.UserClaims.SingleAsync(x => x.UserId == userId && x.ClaimType == "FullName");
        claim.ClaimValue.Should().Be("Maria Oliveira");
    }

    [Fact]
    public async Task UpdateCurrentCustomer_ShouldReturnBadRequest_WhenInvalid()
    {
        await using var db = CreateDbContext();
        var userId = SeedUser(db, "maria@email.com", "22999999999", "Maria Oliveira");

        var controller = CreateController(db, userId);
        var result = await controller.UpdateCurrentCustomer(
            new UpdateCustomerProfileRequestDto
            {
                FullName = "Ma",
                Email = "invalido",
                PhoneNumber = "123"
            },
            CreateUserManager(db),
            new UpdateCustomerProfileRequestDtoValidator(),
            CancellationToken.None);

        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UpdateCurrentCustomer_ShouldReturnConflict_WhenEmailInUseByAnotherUser()
    {
        await using var db = CreateDbContext();
        var userId = SeedUser(db, "maria@email.com", "22999999999", "Maria Oliveira");
        SeedUser(db, "joao@email.com", "22988887777", "Joao Silva");

        var controller = CreateController(db, userId);
        var result = await controller.UpdateCurrentCustomer(
            new UpdateCustomerProfileRequestDto
            {
                FullName = "Maria Oliveira",
                Email = "joao@email.com",
                PhoneNumber = "22999999999"
            },
            CreateUserManager(db),
            new UpdateCustomerProfileRequestDtoValidator(),
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    // E-mail uniqueness is enforced at the database level by the unique UserNameIndex on
    // NormalizedUserName: the application always mirrors the e-mail into UserName, so even a
    // race between two concurrent updates cannot persist a duplicate e-mail. NormalizedEmail
    // only carries a non-unique EmailIndex, which is why no dedicated migration is required.
    [Fact]
    public void IdentityModel_ShouldEnforceUniqueUserName_GuardingEmailUniqueness()
    {
        using var db = CreateDbContext();

        var entityType = db.Model.FindEntityType(typeof(IdentityUser<Guid>));
        entityType.Should().NotBeNull();

        var userNameIndex = entityType!.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(IdentityUser<Guid>.NormalizedUserName)));
        userNameIndex.IsUnique.Should().BeTrue();

        var emailIndex = entityType.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(IdentityUser<Guid>.NormalizedEmail)));
        emailIndex.IsUnique.Should().BeFalse();
    }

    // The race on e-mail/username is only observable under a relational provider; InMemory does
    // not enforce the unique UserNameIndex. These tests therefore exercise the identification
    // helper directly to prove a real unique_violation maps to a conflict while a generic
    // DbUpdateException does not.
    [Fact]
    public void IsUniqueViolation_ShouldDetectPostgresUniqueViolation()
    {
        var exception = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException(
                "duplicate key value violates unique constraint \"UserNameIndex\"",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation));

        InvokeIsUniqueViolation(exception).Should().BeTrue();
    }

    [Fact]
    public void IsUniqueViolation_ShouldNotTreatGenericDbUpdateExceptionAsConflict()
    {
        var exception = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("connection lost"));

        InvokeIsUniqueViolation(exception).Should().BeFalse();
    }

    private static bool InvokeIsUniqueViolation(DbUpdateException exception)
    {
        var method = typeof(CustomerController).GetMethod("IsUniqueViolation", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, [exception])!;
    }

    private static CustomerController CreateController(ApplicationDbContext db, Guid userId)
    {
        var controller = new CustomerController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "test"))
            }
        };
        return controller;
    }

    private static UserManager<IdentityUser<Guid>> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<IdentityUser<Guid>, IdentityRole<Guid>, ApplicationDbContext, Guid>(db);
        return new UserManager<IdentityUser<Guid>>(
            store,
            null!,
            new PasswordHasher<IdentityUser<Guid>>(),
            Array.Empty<IUserValidator<IdentityUser<Guid>>>(),
            Array.Empty<IPasswordValidator<IdentityUser<Guid>>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<IdentityUser<Guid>>>.Instance);
    }

    private static Guid SeedUser(ApplicationDbContext db, string email, string phone, string? fullName)
    {
        var userId = Guid.CreateVersion7();
        db.Users.Add(new IdentityUser<Guid>
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            PhoneNumber = phone,
            EmailConfirmed = true
        });

        if (fullName is not null)
        {
            db.UserClaims.Add(new IdentityUserClaim<Guid>
            {
                UserId = userId,
                ClaimType = "FullName",
                ClaimValue = fullName
            });
        }

        db.SaveChanges();
        return userId;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
