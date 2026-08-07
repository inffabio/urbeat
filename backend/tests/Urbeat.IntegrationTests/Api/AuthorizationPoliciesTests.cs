using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Urbeat.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Urbeat.IntegrationTests.Api;

public sealed class AuthorizationPoliciesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Issuer = "urbeat";
    private const string Audience = "urbeat-api";
    private const string Secret = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";

    private readonly TestWebApplicationFactory _factory;

    public AuthorizationPoliciesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/admin/dashboard", "Admin")]
    [InlineData("/api/seller/panel", "Seller")]
    [InlineData("/api/customer/home", "Customer")]
    public async Task Endpoint_ShouldReturnOk_WhenTokenHasRequiredRole(string url, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken(role));

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/admin/dashboard", "Seller")]
    [InlineData("/api/seller/panel", "Customer")]
    [InlineData("/api/customer/home", "Admin")]
    public async Task Endpoint_ShouldReturnForbidden_WhenTokenRoleIsDifferent(string url, string wrongRole)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildToken(wrongRole));

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/seller/panel")]
    [InlineData("/api/customer/home")]
    public async Task Endpoint_ShouldReturnUnauthorized_WhenNoTokenIsProvided(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string BuildToken(string role)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "integration@urbeat.local"),
            new(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
