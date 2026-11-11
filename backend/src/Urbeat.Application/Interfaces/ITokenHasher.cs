namespace Urbeat.Application.Interfaces;

/// <summary>
/// Hashes short-lived opaque tokens (password reset, e-mail confirmation/promotion) with a fast,
/// deterministic digest. Tokens are high-entropy random values, so they do not need a memory-hard
/// password hasher: using <see cref="System.Security.Cryptography.SHA256"/> keeps verification
/// cheap while still ensuring the raw value is never stored.
/// </summary>
public interface ITokenHasher
{
    /// <summary>Returns the versioned, non-reversible digest of <paramref name="rawToken"/>.</summary>
    string Hash(string rawToken);

    /// <summary>
    /// Verifies <paramref name="rawToken"/> against a digest produced by <see cref="Hash"/> in
    /// constant time. Returns <c>false</c> for malformed digests and never throws.
    /// </summary>
    bool Verify(string rawToken, string hash);
}
