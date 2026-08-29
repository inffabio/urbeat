using FluentAssertions;
using Urbeat.Domain.Security;

namespace Urbeat.UnitTests.Domain;

public sealed class RefreshTokenHasherTests
{
    [Fact]
    public void Hash_ShouldReturnDeterministic64CharHexDigest_ThatDiffersFromRawToken()
    {
        const string raw = "bm90LWEtcmVhbC10b2tlbg==";

        var first = RefreshTokenHasher.Hash(raw);
        var second = RefreshTokenHasher.Hash(raw);

        first.Should().Be(second);
        first.Should().HaveLength(64);
        first.Should().NotBe(raw);
        first.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void Hash_ShouldDiffer_ForDifferentTokens()
    {
        RefreshTokenHasher.Hash("token-a").Should().NotBe(RefreshTokenHasher.Hash("token-b"));
    }
}
