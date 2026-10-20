using FluentAssertions;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class LocalAgentTokenProviderTests
{
    [Fact]
    public void Generate_creates_a_long_random_token()
    {
        var first = LocalAgentTokenProvider.Generate();
        var second = LocalAgentTokenProvider.Generate();

        first.Should().HaveLength(64);
        first.Should().NotBe(second);
    }

    [Fact]
    public void IsValid_requires_the_exact_token()
    {
        var token = LocalAgentTokenProvider.Generate();
        var provider = new LocalAgentTokenProvider(token);

        provider.IsValid(token).Should().BeTrue();
        provider.IsValid(token.ToUpperInvariant()).Should().BeFalse();
        provider.IsValid(token + "x").Should().BeFalse();
        provider.IsValid(null).Should().BeFalse();
        provider.IsValid(string.Empty).Should().BeFalse();
    }
}
