using System.Security.Cryptography;
using System.Text;

namespace Urbeat.Domain.Security;

/// <summary>
/// Produces a one-way hash for refresh tokens so only the digest is ever persisted.
/// </summary>
public static class RefreshTokenHasher
{
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
