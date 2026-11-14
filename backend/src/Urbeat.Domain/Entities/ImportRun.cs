namespace Urbeat.Domain.Entities;

/// <summary>
/// One geographic import execution for a source version and, optionally, a single UF. The record
/// is the audit anchor for staging, validation, publication and rollback; every rejected source
/// row is recorded in <see cref="ImportError"/> with this run.
/// </summary>
public sealed class ImportRun : BaseEntity
{
    public string SourceName { get; set; } = string.Empty;

    public string SourceVersion { get; set; } = string.Empty;

    /// <summary>SHA-256 of the source artifact, computed before any load.</summary>
    public string SourceChecksum { get; set; } = string.Empty;

    /// <summary>Null means a national run covering every UF.</summary>
    public string? Uf { get; set; }

    /// <summary>See <see cref="Geo.GeographicProvenance.ImportStatus"/>.</summary>
    public string Status { get; set; } = Geo.GeographicProvenance.ImportStatus.Discovered;

    public long RowsRead { get; set; }

    public long RowsStaged { get; set; }

    public long RowsPublished { get; set; }

    public long RowsRejected { get; set; }

    /// <summary>Rows intentionally skipped because the same hash was already published.</summary>
    public long RowsSkipped { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>JSON metadata: mapping version, tolerance, reject rate, locality classification counts.</summary>
    public string Metadata { get; set; } = "{}";

    /// <summary>Reason recorded for a rollback; never overwritten by a normal publication.</summary>
    public string? RollbackReason { get; set; }

    public void Finish(string status, DateTime finishedAtUtc)
    {
        Status = status;
        FinishedAtUtc = finishedAtUtc;
        MarkAsUpdated();
    }

    public bool Reconciles => RowsRead == RowsPublished + RowsRejected + RowsSkipped;
}
