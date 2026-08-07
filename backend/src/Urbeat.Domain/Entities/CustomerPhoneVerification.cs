namespace Urbeat.Domain.Entities;

public sealed class CustomerPhoneVerification : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid StoreId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public string PendingCep { get; set; } = string.Empty;

    public string PendingStreet { get; set; } = string.Empty;

    public string PendingNumber { get; set; } = string.Empty;

    public string? PendingComplement { get; set; }

    public string PendingNeighborhood { get; set; } = string.Empty;

    public string PendingCity { get; set; } = string.Empty;

    public string PendingState { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime ResendAvailableAtUtc { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTime? ConfirmedAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }
}
