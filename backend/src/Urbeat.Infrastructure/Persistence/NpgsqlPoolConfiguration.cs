using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Resolves the application connection string with explicit Npgsql pool bounds.
/// <para>
/// Npgsql defaults to a pool of up to 100 physical connections <em>per process</em>. The API and
/// every worker process (plus Hangfire, which opens its own connections) would then be able to
/// exhaust PostgreSQL's default <c>max_connections</c> under load. The application therefore
/// applies conservative limits before the connection string reaches EF Core or Hangfire, so both
/// consumers are bounded by the same value and no additional connections are created silently.
/// </para>
/// <para>
/// Values are read, in order of precedence, from <c>Database:Pool:MaximumSize</c> /
/// <c>Database:Pool:MinimumSize</c>, then from the <c>DB_MAX_POOL_SIZE</c> /
/// <c>DB_MIN_POOL_SIZE</c> environment keys, then from the defaults below. A pool size written
/// explicitly in the connection string is preserved so an environment can be tuned without a
/// rebuild. The connection string is never logged or included in exception messages.
/// </para>
/// </summary>
public static class NpgsqlPoolConfiguration
{
    public const string DefaultConnectionName = "DefaultConnection";

    /// <summary>Environment-style key usable from Compose without a nested configuration path.</summary>
    public const string MaximumPoolSizeKey = "DB_MAX_POOL_SIZE";

    /// <summary>Environment-style key usable from Compose without a nested configuration path.</summary>
    public const string MinimumPoolSizeKey = "DB_MIN_POOL_SIZE";

    public const string MaximumPoolSizeSectionKey = "Database:Pool:MaximumSize";

    public const string MinimumPoolSizeSectionKey = "Database:Pool:MinimumSize";

    /// <summary>
    /// Per-process ceiling for the API and each worker. With three application processes (API plus
    /// two workers) and, in the worst case, one Npgsql pool for EF Core and another for Hangfire,
    /// four application processes (two API replicas and two workers) and, in the worst case, one
    /// Npgsql pool for EF Core and another for Hangfire, 4 x 2 x 10 = 80 connections. The OCI
    /// deployment reserves a 90-connection application budget and leaves headroom for observability
    /// and maintenance sessions below PostgreSQL's default of 100 connections.
    /// </summary>
    public const int DefaultMaximumPoolSize = 10;

    /// <summary>
    /// No connection is kept open for the sole purpose of satisfying the minimum. Npgsql already
    /// retains idle pooled connections; a positive minimum only reserves PostgreSQL backends even
    /// while the service is idle.
    /// </summary>
    public const int DefaultMinimumPoolSize = 0;

    /// <summary>
    /// Guardrail for the values configured through <see cref="MaximumPoolSizeKey"/> and
    /// <see cref="MaximumPoolSizeSectionKey"/>. An explicit value in the connection string is an
    /// operator decision and is only checked for consistency, not capped here.
    /// </summary>
    public const int MaximumAllowedPoolSize = 100;

    private const string MaximumPoolSizeKeyword = "Maximum Pool Size";
    private const string MinimumPoolSizeKeyword = "Minimum Pool Size";

    /// <summary>
    /// Returns <c>ConnectionStrings:DefaultConnection</c> normalized as an Npgsql connection string
    /// with explicit <c>Maximum Pool Size</c> and <c>Minimum Pool Size</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The connection string is missing, malformed, or the configured pool sizes are invalid.
    /// </exception>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(DefaultConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{DefaultConnectionName}' is required.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // The raw value carries credentials, so it must never be part of the message.
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{DefaultConnectionName}' is not a valid Npgsql connection string.",
                exception);
        }

        if (!builder.Pooling)
        {
            // Pool sizes are meaningless when pooling is disabled; keep the operator's setting.
            return builder.ConnectionString;
        }

        var configuredMinimum = ReadPoolSize(
            configuration,
            MinimumPoolSizeSectionKey,
            MinimumPoolSizeKey,
            DefaultMinimumPoolSize);
        var configuredMaximum = ReadPoolSize(
            configuration,
            MaximumPoolSizeSectionKey,
            MaximumPoolSizeKey,
            DefaultMaximumPoolSize);
        ValidateConfiguredSizes(configuredMinimum, configuredMaximum);

        // An explicit value in the connection string wins over the application defaults so a single
        // environment can be tuned operationally; the configuration only fills the gaps.
        // NpgsqlConnectionStringBuilder.ContainsKey reports every known keyword, so
        // ShouldSerialize is what distinguishes a value written by the operator from a default.
        if (!builder.ShouldSerialize(MaximumPoolSizeKeyword))
        {
            builder.MaxPoolSize = configuredMaximum;
        }

        if (!builder.ShouldSerialize(MinimumPoolSizeKeyword))
        {
            builder.MinPoolSize = configuredMinimum;
        }

        ValidateEffectiveSizes(builder.MinPoolSize, builder.MaxPoolSize);

        return builder.ConnectionString;
    }

    private static int ReadPoolSize(
        IConfiguration configuration,
        string sectionKey,
        string environmentKey,
        int defaultValue)
    {
        var rawValue = configuration[sectionKey];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            rawValue = configuration[environmentKey];
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException(
                $"Configuration '{sectionKey}' (or environment variable '{environmentKey}') must be an integer.");
        }

        return value;
    }

    private static void ValidateConfiguredSizes(int minimum, int maximum)
    {
        if (maximum < 1 || maximum > MaximumAllowedPoolSize)
        {
            throw new InvalidOperationException(
                $"Configuration '{MaximumPoolSizeSectionKey}' (or environment variable '{MaximumPoolSizeKey}') " +
                $"must be between 1 and {MaximumAllowedPoolSize}.");
        }

        if (minimum < 0 || minimum > maximum)
        {
            throw new InvalidOperationException(
                $"Configuration '{MinimumPoolSizeSectionKey}' (or environment variable '{MinimumPoolSizeKey}') " +
                "must be zero or greater and must not exceed the maximum pool size.");
        }
    }

    private static void ValidateEffectiveSizes(int minimum, int maximum)
    {
        if (maximum < 1)
        {
            throw new InvalidOperationException(
                $"The Npgsql '{MaximumPoolSizeKeyword}' in 'ConnectionStrings:{DefaultConnectionName}' must be at least 1.");
        }

        if (minimum < 0 || minimum > maximum)
        {
            throw new InvalidOperationException(
                $"The Npgsql '{MinimumPoolSizeKeyword}' in 'ConnectionStrings:{DefaultConnectionName}' " +
                "must be zero or greater and must not exceed the maximum pool size.");
        }
    }
}
