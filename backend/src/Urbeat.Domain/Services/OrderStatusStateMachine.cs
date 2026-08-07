using Urbeat.Domain.Entities;

namespace Urbeat.Domain.Services;

public static class OrderStatusStateMachine
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Created] = [OrderStatus.Received, OrderStatus.Cancelled],
            [OrderStatus.PendingPayment] = [OrderStatus.Received, OrderStatus.Cancelled],
            [OrderStatus.Received] = [OrderStatus.Preparing, OrderStatus.Cancelled],
            [OrderStatus.Preparing] = [OrderStatus.Ready, OrderStatus.Cancelled],
            [OrderStatus.Ready] = [OrderStatus.OnDelivery, OrderStatus.Delivered, OrderStatus.Cancelled],
            [OrderStatus.OnDelivery] = [OrderStatus.Delivered, OrderStatus.Cancelled],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

    public static IReadOnlyCollection<OrderStatus> GetNextStatuses(OrderStatus current)
    {
        return Transitions.TryGetValue(current, out var statuses) ? statuses : [];
    }

    public static bool CanTransition(OrderStatus current, OrderStatus next)
    {
        return GetNextStatuses(current).Contains(next);
    }
}
