namespace Urbeat.Domain.Entities;

/// <summary>
/// Global delivery driver identity, shared by every store that invites the same e-mail.
/// The identity row exists before the account is confirmed: <see cref="UserId"/> is filled
/// when the invite is accepted and the linked Identity account is created or proven.
/// Store-scoped access is modelled separately by <see cref="StoreDeliveryDriver"/> so data
/// from one store can never leak into another.
/// </summary>
public sealed class DeliveryDriver : BaseEntity
{
    public Guid? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}
