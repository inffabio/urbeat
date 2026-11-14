namespace Urbeat.Domain.Entities;

/// <summary>
/// Zip code associated with a <see cref="DeliveryStreet"/>. A logradouro may have 0..N CEPs and a
/// CEP may span several logradouros, so the pair is the key and <see cref="IsPrimary"/> only marks
/// the preferred value for display (it never restricts the search).
/// </summary>
public sealed class DeliveryStreetZipCode
{
    public Guid StreetId { get; set; }

    public DeliveryStreet? Street { get; set; }

    /// <summary>Exactly eight digits; validated by a database check constraint.</summary>
    public string ZipCode { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public string Source { get; set; } = "cnefe";

    public string SourceVersion { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
