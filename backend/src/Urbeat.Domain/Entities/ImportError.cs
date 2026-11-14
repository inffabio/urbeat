namespace Urbeat.Domain.Entities;

/// <summary>
/// Rejected or flagged source row. No row is discarded without a record here. The identifier is a
/// database-generated bigint because the volume is high and the value has no business identity.
/// </summary>
public sealed class ImportError
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public ImportRun? Run { get; set; }

    public string? Uf { get; set; }

    public string SourceTable { get; set; } = string.Empty;

    public string? SourceRowId { get; set; }

    public string? Field { get; set; }

    /// <summary>Stable code, e.g. <c>INVALID_ZIP</c> or <c>COORD_CENTROID_MISMATCH</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? RawValue { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
