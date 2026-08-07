namespace Urbeat.Domain.Entities;

public enum OrderStatus
{
    Created = 1,
    PendingPayment = 2,
    Received = 3,
    Preparing = 4,
    Ready = 5,
    OnDelivery = 6,
    Delivered = 7,
    Cancelled = 8
}
