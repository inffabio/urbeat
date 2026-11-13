namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Operational bounds for the Cloudinary orphan maintenance flow. The defaults are deliberately
/// conservative: nothing younger than one day is deleted, because a seller may be between the
/// upload and the form save, and a single call never scans or destroys an unbounded set.
/// </summary>
public sealed class CloudinaryMaintenanceOptions
{
    public const string SectionName = "CloudinaryMaintenance";

    public const int DefaultMinimumAssetAgeMinutes = 1440;
    public const int DefaultMaxAssetsPerScan = 500;
    public const int DefaultMaxDeletionsPerRun = 100;
    public const int HardMaxAssetsPerScan = 2000;
    public const int HardMaxDeletionsPerRun = 500;

    /// <summary>Minimum age in minutes before any asset (including <c>stores/_pending/</c>) is deletable.</summary>
    public int MinimumAssetAgeMinutes { get; set; } = DefaultMinimumAssetAgeMinutes;

    /// <summary>Maximum number of Cloudinary resources inspected by one scan.</summary>
    public int MaxAssetsPerScan { get; set; } = DefaultMaxAssetsPerScan;

    /// <summary>Maximum number of assets destroyed by one cleanup call.</summary>
    public int MaxDeletionsPerRun { get; set; } = DefaultMaxDeletionsPerRun;

    public int EffectiveMinimumAssetAgeMinutes => MinimumAssetAgeMinutes >= 0
        ? MinimumAssetAgeMinutes
        : DefaultMinimumAssetAgeMinutes;

    public int EffectiveMaxAssetsPerScan => MaxAssetsPerScan is > 0 and <= HardMaxAssetsPerScan
        ? MaxAssetsPerScan
        : DefaultMaxAssetsPerScan;

    public int EffectiveMaxDeletionsPerRun => MaxDeletionsPerRun is > 0 and <= HardMaxDeletionsPerRun
        ? MaxDeletionsPerRun
        : DefaultMaxDeletionsPerRun;
}
