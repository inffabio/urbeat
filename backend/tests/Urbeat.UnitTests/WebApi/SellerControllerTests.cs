using System.Security.Claims;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Urbeat.UnitTests.WebApi;

public sealed class SellerControllerTests
{
    [Fact]
    public async Task UpdateProfile_ShouldReplaceFullNameClaim_WhenItAlreadyExists()
    {
        await using var db = CreateDbContext();
        var userId = SeedSeller(db, "vendedor@email.com", "Vendedor Antigo");

        var controller = CreateController(userId);
        var result = await controller.UpdateProfile(
            new UpdateSellerProfileRequestDto { FullName = "Contratante Atualizado" },
            CreateUserManager(db),
            new UpdateSellerProfileRequestDtoValidator(),
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            fullName = "Contratante Atualizado",
            phoneNumber = "11999999999",
            email = "vendedor@email.com",
        }, options => options.ExcludingMissingMembers());

        var claim = await db.UserClaims.SingleAsync(x => x.UserId == userId && x.ClaimType == "FullName");
        claim.ClaimValue.Should().Be("Contratante Atualizado");
    }

    [Fact]
    public async Task UpdateProfile_ShouldCreateFullNameClaim_WhenAbsent()
    {
        await using var db = CreateDbContext();
        var userId = SeedSeller(db, "vendedor@email.com", fullName: null);

        var controller = CreateController(userId);
        var result = await controller.UpdateProfile(
            new UpdateSellerProfileRequestDto { FullName = "Novo Contratante" },
            CreateUserManager(db),
            new UpdateSellerProfileRequestDtoValidator(),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var claim = await db.UserClaims.SingleAsync(x => x.UserId == userId && x.ClaimType == "FullName");
        claim.ClaimValue.Should().Be("Novo Contratante");
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturnBadRequest_WhenNameIsEmptyOrOversized()
    {
        await using var db = CreateDbContext();
        var userId = SeedSeller(db, "vendedor@email.com", "Contratante");
        var controller = CreateController(userId);
        var userManager = CreateUserManager(db);
        var validator = new UpdateSellerProfileRequestDtoValidator();

        var empty = await controller.UpdateProfile(
            new UpdateSellerProfileRequestDto { FullName = "   " },
            userManager,
            validator,
            CancellationToken.None);

        var oversized = await controller.UpdateProfile(
            new UpdateSellerProfileRequestDto { FullName = new string('a', 121) },
            userManager,
            validator,
            CancellationToken.None);

        empty.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        oversized.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var claim = await db.UserClaims.SingleAsync(x => x.UserId == userId && x.ClaimType == "FullName");
        claim.ClaimValue.Should().Be("Contratante");
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturnUnauthorized_WhenCurrentUserIsMissing()
    {
        await using var db = CreateDbContext();
        var controller = new SellerController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await controller.UpdateProfile(
            new UpdateSellerProfileRequestDto { FullName = "Contratante Valido" },
            CreateUserManager(db),
            new UpdateSellerProfileRequestDtoValidator(),
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    private static SellerController CreateController(Guid userId)
    {
        return new SellerController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                    ], "test"))
                }
            }
        };
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

    private static Guid SeedSeller(ApplicationDbContext db, string email, string? fullName)
    {
        var userId = Guid.CreateVersion7();
        db.Users.Add(new IdentityUser<Guid>
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            PhoneNumber = "11999999999",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
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
