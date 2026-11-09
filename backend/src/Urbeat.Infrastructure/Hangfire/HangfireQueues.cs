namespace Urbeat.Infrastructure.Hangfire;

/// <summary>
/// Names of the Hangfire queues used by the Urbeat hosts.
/// </summary>
/// <remarks>
/// Jobs are routed by <c>[Queue]</c> attributes; the API only enqueues, while each worker host
/// listens to the queues of its role. <see cref="Default"/> is kept for compatibility with jobs
/// that were enqueued before queue separation and for ad-hoc jobs that do not declare a queue.
/// </remarks>
public static class HangfireQueues
{
    public const string Default = "default";
    public const string Business = "business";
    public const string Communications = "communications";

    public static readonly IReadOnlyList<string> AllServerQueues =
    [
        Business,
        Communications,
        Default
    ];

    public static readonly IReadOnlyList<string> BusinessServerQueues =
    [
        Business,
        Default
    ];

    public static readonly IReadOnlyList<string> CommunicationsServerQueues =
    [
        Communications
    ];

    public static readonly IReadOnlyList<string> DefaultServerQueues =
    [
        Default
    ];
}
