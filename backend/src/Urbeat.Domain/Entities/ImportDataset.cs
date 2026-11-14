namespace Urbeat.Domain.Entities;

/// <summary>
/// Version pointer per UF. Publication activates a dataset; rollback deactivates it and reactivates
/// the previous version without deleting rows. A partial (single-UF) load can therefore coexist
/// with the previous version until the national load is validated.
/// </summary>
public sealed class ImportDataset : BaseEntity
{
    public string SourceVersion { get; set; } = string.Empty;

    public string Uf { get; set; } = string.Empty;

    public Guid RunId { get; set; }

    public ImportRun? Run { get; set; }

    public bool IsActive { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }

    public DateTime? DeactivatedAtUtc { get; set; }
}
