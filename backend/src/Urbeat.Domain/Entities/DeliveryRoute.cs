namespace Urbeat.Domain.Entities;

/// <summary>
/// Temporary delivery route (batch) shared with a motoboy through a public tokenized link.
/// The route belongs to a single store and a single store-scoped driver; it never references
/// orders from another store or another driver. The raw access token is never persisted —
/// only its SHA-256 hash. Revoking or expiring the route invalidates the public link.
/// </summary>
public sealed class DeliveryRoute : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid DeliveryDriverId { get; set; }

    public Guid StoreDeliveryDriverId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Route updates and public delivery confirmations advance it,
    /// so two requests cannot reorder the route or confirm the same stop concurrently.
    /// </summary>
    public int ConcurrencyVersion { get; set; }

    public ICollection<DeliveryRouteOrder> Orders { get; set; } = new List<DeliveryRouteOrder>();

    public void AdvanceConcurrency()
    {
        ConcurrencyVersion += 1;
    }

    public bool IsActive(DateTime utcNow) =>
        RevokedAtUtc is null && CompletedAtUtc is null && ExpiresAtUtc > utcNow;
}

public enum DeliveryRouteStatus
{
    Active = 1,
    Completed = 2,
    Revoked = 3,
    Expired = 4
}
