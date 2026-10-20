namespace Urbeat.Domain.Entities;

/// <summary>
/// Durable server-side record of an order acceptance that requires local printing. The dashboard
/// reads pending jobs from this table after a reload/reconnect, so recovery does not depend on a
/// browser-local watermark. <see cref="Cursor"/> is a database-generated, monotonically increasing
/// pagination cursor.
/// </summary>
public sealed class OrderPrintJob
{
    public long Cursor { get; set; }

    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public Guid SellerUserId { get; set; }

    public string Code { get; set; } = string.Empty;

    public int AcceptanceSequence { get; set; }

    public DateTime AcceptedAtUtc { get; set; }

    public OrderPrintJobStatus Status { get; set; } = OrderPrintJobStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public string? LastOperationKey { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}

public enum OrderPrintJobStatus
{
    Pending = 0,
    Completed = 1,

    /// <summary>
    /// The local agent crashed around the spooler call. Physical printing may or may not have
    /// happened; this state must not be retried automatically.
    /// </summary>
    Ambiguous = 2
}
