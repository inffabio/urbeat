using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Composite <see cref="IPasswordHasher{TUser}"/> for user credentials.
/// </summary>
/// <remarks>
/// <para>
/// New hashes are Argon2id in a versioned, self-describing format
/// (<c>$urbeat-argon2id$v=1$m=...,t=...,p=...$salt$subkey</c>). The salt is random per hash and no
/// pepper is used, so the stored value is enough to verify the password and no secret has to be
/// added to configuration, the OCI Vault or <c>.env</c>.
/// </para>
/// <para>
/// Hashes produced by the legacy ASP.NET Identity <see cref="PasswordHasher{TUser}"/> (PBKDF2) are
/// still accepted and reported as <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>, so
/// the existing call sites migrate them to Argon2id right after a successful login without a
/// password reset. A successful Argon2id verification also asks for a rehash when the stored
/// parameters are below the current configuration.
/// </para>
/// <para>
/// Malformed, truncated or out-of-bounds hashes fail closed without throwing. Memory, iteration
/// and parallelism values read from a stored hash are validated against the configured bounds
/// before Argon2id runs, so a forged hash cannot force an arbitrary allocation.
/// </para>
/// <para>
/// This hasher is only for user passwords. Tokens (confirmation, promotion, reset) use
/// <see cref="TokenHasher"/> and delivery PINs keep their own dedicated hasher.
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher<IdentityUser<Guid>>
{
    private const string Prefix = "$urbeat-argon2id$";
    private const string AlgorithmSegment = "urbeat-argon2id";

    // Version of this repository's encoding. The Argon2 algorithm itself is version 1.3 (v=19),
    // fixed by the Konscious implementation, so it does not need to be encoded in the string.
    private const int FormatVersion = 1;

    // A well-formed hash is ~120 chars. Limiting the input keeps a forged/oversized value from
    // allocating or being parsed repeatedly.
    private const int MaximumEncodedLength = 1024;

    private const string MemoryKey = "m=";
    private const string IterationsKey = "t=";
    private const string ParallelismKey = "p=";
    private const string VersionKey = "v=";

    private static readonly char[] Base64UrlCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray();

    private readonly PasswordHasher<IdentityUser<Guid>> _legacyHasher = new();
    private readonly Argon2idPasswordHasherOptions _options;

    public Argon2idPasswordHasher(IOptions<Argon2idPasswordHasherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string HashPassword(IdentityUser<Guid> user, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(_options.SaltSizeBytes);
        var subkey = Derive(
            password,
            salt,
            _options.MemorySizeKb,
            _options.Iterations,
            _options.DegreeOfParallelism,
            _options.HashSizeBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${AlgorithmSegment}${VersionKey}{FormatVersion}${MemoryKey}{_options.MemorySizeKb},t={_options.Iterations},p={_options.DegreeOfParallelism}${Base64UrlEncode(salt)}${Base64UrlEncode(subkey)}");
    }

    public PasswordVerificationResult VerifyHashedPassword(
        IdentityUser<Guid> user,
        string hashedPassword,
        string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword)
            || string.IsNullOrEmpty(providedPassword)
            || hashedPassword.Length > MaximumEncodedLength)
        {
            return PasswordVerificationResult.Failed;
        }

        return hashedPassword.StartsWith(Prefix, StringComparison.Ordinal)
            ? VerifyArgon2id(hashedPassword, providedPassword)
            : VerifyLegacy(user, hashedPassword, providedPassword);
    }

    private PasswordVerificationResult VerifyArgon2id(string hashedPassword, string providedPassword)
    {
        if (!TryParse(hashedPassword, out var memorySizeKb, out var iterations, out var parallelism, out var salt, out var expectedSubkey))
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] actualSubkey;
        try
        {
            actualSubkey = Derive(providedPassword, salt, memorySizeKb, iterations, parallelism, expectedSubkey.Length);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Invalid parameters that slipped through parsing must fail closed, never surface details.
            return PasswordVerificationResult.Failed;
        }

        if (!CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey))
        {
            return PasswordVerificationResult.Failed;
        }

        return IsBelowCurrentParameters(memorySizeKb, iterations, parallelism, expectedSubkey.Length)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private PasswordVerificationResult VerifyLegacy(
        IdentityUser<Guid> user,
        string hashedPassword,
        string providedPassword)
    {
        PasswordVerificationResult result;
        try
        {
            result = _legacyHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return PasswordVerificationResult.Failed;
        }

        return result == PasswordVerificationResult.Failed
            ? PasswordVerificationResult.Failed
            : PasswordVerificationResult.SuccessRehashNeeded;
    }

    private bool TryParse(
        string hashedPassword,
        out int memorySizeKb,
        out int iterations,
        out int parallelism,
        out byte[] salt,
        out byte[] subkey)
    {
        memorySizeKb = 0;
        iterations = 0;
        parallelism = 0;
        salt = [];
        subkey = [];

        // Empty leading segment + algorithm + version + params + salt + subkey.
        var segments = hashedPassword.Split('$');
        if (segments.Length != 6
            || segments[0].Length != 0
            || !string.Equals(segments[1], AlgorithmSegment, StringComparison.Ordinal)
            || !TryParseVersion(segments[2])
            || !TryParseParameters(segments[3], out memorySizeKb, out iterations, out parallelism))
        {
            return false;
        }

        if (!TryBase64UrlDecode(segments[4], Argon2idPasswordHasherOptions.MinimumSaltSizeBytes, Argon2idPasswordHasherOptions.MaximumSaltSizeBytes, out salt)
            || !TryBase64UrlDecode(segments[5], Argon2idPasswordHasherOptions.MinimumHashSizeBytes, Argon2idPasswordHasherOptions.MaximumHashSizeBytes, out subkey))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseVersion(string segment)
    {
        if (!segment.StartsWith(VersionKey, StringComparison.Ordinal))
        {
            return false;
        }

        return TryParsePositiveInt(segment[VersionKey.Length..], out var version)
               && version == FormatVersion;
    }

    private static bool TryParseParameters(
        string segment,
        out int memorySizeKb,
        out int iterations,
        out int parallelism)
    {
        memorySizeKb = 0;
        iterations = 0;
        parallelism = 0;

        // The writer always emits m,t,p in this order. A different order or extra keys is rejected
        // instead of guessed, so two implementations can never disagree about the parameters.
        var parts = segment.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        return TryParseKeyedPositiveInt(parts[0], MemoryKey, out memorySizeKb)
               && TryParseKeyedPositiveInt(parts[1], IterationsKey, out iterations)
               && TryParseKeyedPositiveInt(parts[2], ParallelismKey, out parallelism)
               && IsWithinBounds(memorySizeKb, iterations, parallelism);
    }

    private static bool IsWithinBounds(int memorySizeKb, int iterations, int parallelism)
        => memorySizeKb is >= Argon2idPasswordHasherOptions.MinimumMemorySizeKb
               and <= Argon2idPasswordHasherOptions.MaximumMemorySizeKb
           && iterations is >= Argon2idPasswordHasherOptions.MinimumIterations
               and <= Argon2idPasswordHasherOptions.MaximumIterations
           && parallelism is >= Argon2idPasswordHasherOptions.MinimumDegreeOfParallelism
               and <= Argon2idPasswordHasherOptions.MaximumDegreeOfParallelism
           && memorySizeKb >= 8L * parallelism;

    private static bool TryParseKeyedPositiveInt(string segment, string key, out int value)
    {
        value = 0;
        return segment.StartsWith(key, StringComparison.Ordinal)
               && TryParsePositiveInt(segment[key.Length..], out value);
    }

    private static bool TryParsePositiveInt(string value, out int parsed)
    {
        // NumberStyles.None rejects signs, whitespace and thousands separators; a negative or
        // decorated value must never be accepted as a cost parameter.
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
               && parsed > 0;
    }

    private bool IsBelowCurrentParameters(int memorySizeKb, int iterations, int parallelism, int subkeyLength)
        => memorySizeKb < _options.MemorySizeKb
           || iterations < _options.Iterations
           || parallelism < _options.DegreeOfParallelism
           || subkeyLength < _options.HashSizeBytes;

    private static byte[] Derive(
        string password,
        byte[] salt,
        int memorySizeKb,
        int iterations,
        int parallelism,
        int subkeyLength)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memorySizeKb,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };

            return argon2.GetBytes(subkeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, int minimumLength, int maximumLength, out byte[] bytes)
    {
        bytes = [];

        if (value.Length == 0 || value.Length > maximumLength * 4)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (Array.IndexOf(Base64UrlCharacters, character) < 0)
            {
                return false;
            }
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            _ => normalized
        };

        try
        {
            bytes = Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return false;
        }

        return bytes.Length >= minimumLength && bytes.Length <= maximumLength;
    }
}
