namespace Urbeat.Domain.Entities;

/// <summary>
/// Store-scoped motoboy record. The current operational flow does not require an account,
/// password or e-mail: the row only stores the contact data the seller typed for that store.
/// <see cref="UserId"/> and the e-mail fields remain for compatibility with delivery drivers
/// created by the legacy invite flow; new records keep them null.
/// Store-scoped access is modelled by <see cref="StoreDeliveryDriver"/> so data from one store
/// can never leak into another.
/// </summary>
public sealed class DeliveryDriver : BaseEntity
{
    public Guid? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Phone used to open the WhatsApp route link. Null falls back to the main phone.</summary>
    public string? WhatsAppPhoneNumber { get; set; }
}
