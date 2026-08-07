using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
