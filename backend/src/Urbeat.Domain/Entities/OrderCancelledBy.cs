namespace Urbeat.Domain.Entities;

/// <summary>
/// Actor that cancelled an order. The server derives this value; it is never accepted from the
/// client. <see cref="Store"/> is the only producer today (seller-initiated cancellation).
/// </summary>
public enum OrderCancelledBy
{
    Store = 1,
    Customer = 2,
    System = 3
}
