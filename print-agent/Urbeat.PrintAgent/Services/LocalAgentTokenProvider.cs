using System.Security.Cryptography;
using System.Text;

namespace Urbeat.PrintAgent.Services;

/// <summary>
/// Holds the local API token generated on first setup. The token is persisted in the agent config
/// and required by every endpoint except the loopback health probe and the origin-restricted pair
/// endpoint.
/// </summary>
public sealed class LocalAgentTokenProvider
{
    private readonly byte[] _tokenBytes;

    public LocalAgentTokenProvider(string token)
    {
        Token = token;
        _tokenBytes = Encoding.UTF8.GetBytes(token);
    }

    public string Token { get; }

    public bool IsValid(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        return candidateBytes.Length == _tokenBytes.Length
            && CryptographicOperations.FixedTimeEquals(candidateBytes, _tokenBytes);
    }

    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
