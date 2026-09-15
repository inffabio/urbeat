using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Regression coverage for the shared seller-registration helper. Integration test classes share a
/// single in-memory database (via <see cref="TestWebApplicationFactory"/> as a class fixture), so
/// reusing a base FullName across registrations must not surface as a misleading e-mail
/// confirmation lookup failure.
/// </summary>
public sealed class TestAuthHelpersTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TestAuthHelpersTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterSellerAsync_ShouldSucceed_WhenBaseFullNameIsReusedAcrossRegistrations()
    {
        const string baseFullName = "Seller Duplicado";
        const string password = "SenhaForte123";

        var firstClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var firstEmail = $"helper.first.{Guid.NewGuid():N}@urbeat.local";
        await _factory.RegisterSellerAsync(firstClient, firstEmail, password, baseFullName, "11981112222");

        var secondClient = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var secondEmail = $"helper.second.{Guid.NewGuid():N}@urbeat.local";
        await _factory.RegisterSellerAsync(secondClient, secondEmail, password, baseFullName, "11981112222");

        var firstLogin = await firstClient.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = firstEmail,
            Password = password
        });
        firstLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondLogin = await secondClient.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = secondEmail,
            Password = password
        });
        secondLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
