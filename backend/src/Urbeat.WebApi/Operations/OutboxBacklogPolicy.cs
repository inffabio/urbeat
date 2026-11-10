using Urbeat.Domain.Entities;

namespace Urbeat.WebApi.Operations;

/// <summary>
/// Canonical definition of processable outbox backlog. A row is backlog while it is pending or
/// being processed; failed and completed rows are not backlog. Health and the admin projection use
/// this same policy so their status cannot diverge.
/// </summary>
public static class OutboxBacklogPolicy
{
    public static bool IsBacklog(OutboxMessageStatus status) =>
        status is OutboxMessageStatus.Pending or OutboxMessageStatus.Processing;

    public static bool IsBacklog(OutboxHandlerDeliveryStatus status) =>
        status is OutboxHandlerDeliveryStatus.Pending or OutboxHandlerDeliveryStatus.Processing;

    public static int Count(int pending, int processing) => pending + processing;
}
