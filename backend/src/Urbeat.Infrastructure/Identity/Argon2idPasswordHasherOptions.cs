using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Operational parameters for the Argon2id password hasher. Every value here is non-secret and may
/// live in regular configuration files; there is intentionally <b>no pepper</b> (and therefore no
/// new Vault/secret entry). Defaults follow the approved profile: Argon2id v19, ~64 MiB of memory,
/// 3 iterations, parallelism 2, 16-byte salt and 32-byte subkey.
/// </summary>
/// <remarks>
/// The bounds exist to keep a misconfiguration from exhausting memory or CPU during login/registration
/// (DoS through configuration). Test and development environments may explicitly use the minimum
/// values; production must keep the defaults.
/// </remarks>
public sealed class Argon2idPasswordHasherOptions
{
    public const string SectionName = "PasswordHashing:Argon2id";

    public const int MinimumMemorySizeKb = 8 * 1024;
    public const int MaximumMemorySizeKb = 256 * 1024;
    public const int MinimumIterations = 1;
    public const int MaximumIterations = 10;
    public const int MinimumDegreeOfParallelism = 1;
    public const int MaximumDegreeOfParallelism = 8;
    public const int MinimumSaltSizeBytes = 16;
    public const int MaximumSaltSizeBytes = 64;
    public const int MinimumHashSizeBytes = 16;
    public const int MaximumHashSizeBytes = 64;

    /// <summary>Memory cost in kibibytes (1 KiB = 1024 bytes).</summary>
    public int MemorySizeKb { get; set; } = 65_536;

    /// <summary>Time cost (iterations).</summary>
    public int Iterations { get; set; } = 3;

    /// <summary>Number of parallel lanes.</summary>
    public int DegreeOfParallelism { get; set; } = 2;

    /// <summary>Random salt size in bytes. A fresh salt is generated for every hash.</summary>
    public int SaltSizeBytes { get; set; } = 16;

    /// <summary>Derived subkey size in bytes.</summary>
    public int HashSizeBytes { get; set; } = 32;

    /// <summary>
    /// Validates the configured values against the supported bounds. Messages never contain secret
    /// material (there is none), so they are safe to log and to surface as a startup error.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        ValidateRange(failures, nameof(MemorySizeKb), MemorySizeKb, MinimumMemorySizeKb, MaximumMemorySizeKb);
        ValidateRange(failures, nameof(Iterations), Iterations, MinimumIterations, MaximumIterations);
        ValidateRange(failures, nameof(DegreeOfParallelism), DegreeOfParallelism, MinimumDegreeOfParallelism, MaximumDegreeOfParallelism);
        ValidateRange(failures, nameof(SaltSizeBytes), SaltSizeBytes, MinimumSaltSizeBytes, MaximumSaltSizeBytes);
        ValidateRange(failures, nameof(HashSizeBytes), HashSizeBytes, MinimumHashSizeBytes, MaximumHashSizeBytes);

        // Argon2 requires at least 8 KiB of memory per lane; smaller values are invalid and would
        // throw inside the library, so they are rejected here instead.
        if (DegreeOfParallelism >= MinimumDegreeOfParallelism
            && MemorySizeKb < 8L * DegreeOfParallelism)
        {
            failures.Add(
                $"{SectionName}:MemorySizeKb must be at least 8 KiB per parallel lane "
                + $"(>= {8 * DegreeOfParallelism} for DegreeOfParallelism={DegreeOfParallelism}).");
        }

        return failures;
    }

    private static void ValidateRange(
        ICollection<string> failures,
        string property,
        int value,
        int minimum,
        int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{SectionName}:{property} must be between {minimum} and {maximum} (received {value}).");
        }
    }
}

/// <summary>
/// Wires <see cref="Argon2idPasswordHasherOptions.Validate"/> into the options pipeline with
/// <c>ValidateOnStart</c>, so an out-of-bounds cost fails the host at startup instead of during the
/// first login.
/// </summary>
public sealed class Argon2idPasswordHasherOptionsValidator
    : IValidateOptions<Argon2idPasswordHasherOptions>
{
    public ValidateOptionsResult Validate(string? name, Argon2idPasswordHasherOptions options)
    {
        var failures = options.Validate();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
