namespace Urbeat.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public Guid OrderId { get; set; }

    public PaymentGateway Gateway { get; set; }

    public string GatewayTransactionId { get; set; } = string.Empty;

    public string? GatewayCheckoutUrl { get; set; }

    public string? ExternalReference { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    public int Attempt { get; set; } = 1;

    /// <summary>
    /// Optimistic concurrency token used to serialize concurrent webhooks that target the same
    /// payment with different statuses. A losing write raises
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, preserving the
    /// state machine and history written by the winning request.
    /// </summary>
    public Guid ConcurrencyStamp { get; set; }

    public string? RawPayload { get; set; }

    /// <summary>
    /// Mock Pix simulation metadata. Present only for payments created under
    /// <c>Payments:Provider=Mock</c>. <see cref="MockExpiresAtUtc"/> is the end of the payment
    /// window; <see cref="MockApprovalAtUtc"/> is the scheduled approval time when the selected
    /// outcome is <c>Approved</c> (null otherwise); <see cref="MockOutcome"/> stores the outcome
    /// chosen once per attempt.
    /// </summary>
    public DateTime? MockExpiresAtUtc { get; set; }

    public DateTime? MockApprovalAtUtc { get; set; }

    public string? MockOutcome { get; set; }
}
