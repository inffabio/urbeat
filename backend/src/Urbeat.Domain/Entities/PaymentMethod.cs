namespace Urbeat.Domain.Entities;

/// <summary>
/// Payment method selected by the customer at checkout.
///
/// Values are persisted as integers and must never be renumbered. The online values
/// (<see cref="PixOnline"/> and <see cref="CardOnline"/>) are kept so orders created before the
/// storefront stopped offering online Pix keep their historical contract and in-flight gateway
/// transactions (links, webhooks and retries) continue to work. New checkouts are rejected for
/// those online methods by the authoritative checkout policy.
///
/// The pay-on-delivery values are:
/// <list type="bullet">
/// <item><see cref="CashOnDelivery"/>: cash paid to the seller/motoboy.</item>
/// <item><see cref="CardOnDelivery"/>: legacy generic card on delivery. Kept for old orders only.</item>
/// <item><see cref="PixOnDelivery"/>: Pix paid on the card machine at handover.</item>
/// <item><see cref="DebitOnDelivery"/>: debit card on the card machine at handover.</item>
/// <item><see cref="CreditOnDelivery"/>: credit card on the card machine at handover.</item>
/// </list>
/// The card machine identifier is never collected, stored or displayed: Urbeat only records that
/// the payment happened on the machine, never a bank/acquiring proof or terminal serial.
/// </summary>
public enum PaymentMethod
{
    PixOnline = 1,
    CardOnline = 2,
    CashOnDelivery = 3,
    CardOnDelivery = 4,
    PixOnDelivery = 5,
    DebitOnDelivery = 6,
    CreditOnDelivery = 7
}
