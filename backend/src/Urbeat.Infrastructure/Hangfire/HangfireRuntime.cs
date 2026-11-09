using System.Globalization;
using Urbeat.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Urbeat.Infrastructure.Hangfire;

public enum HangfireRole
{
    /// <summary>
    /// Legacy single-process mode: the host consumes every queue, runs the outbox and registers
    /// the recurring jobs. This remains the default when <c>Hangfire:Role</c> is not configured so
    /// existing deployments keep working until they opt into dedicated workers.
    /// </summary>
    All,

    /// <summary>
    /// HTTP API role. It keeps the Hangfire storage and dashboard and may enqueue jobs, but does
    /// not run the outbox or register recurring jobs. A Hangfire server is not started by default.
    /// </summary>
    Web,

    /// <summary>
    /// Business/infrastructure worker: outbox dispatcher, business jobs, business queue and the
    /// single recurring-job scheduler.
    /// </summary>
    Business,

    /// <summary>
    /// Communications worker: only the communications queue (WhatsApp/e-mail/notifications jobs
    /// that are enqueued directly through Hangfire).
    /// </summary>
    Communications
}

/// <summary>
/// Immutable description of the Hangfire responsibility of the current process. It is resolved
/// once from configuration and registered in DI so hosts, health checks and tests share the same
/// queue/role decision.
/// </summary>
public sealed record HangfireRuntime(
    HangfireRole Role,
    bool RunsServer,
    bool RegistersRecurringJobs,
    int WorkerCount,
    IReadOnlyList<string> Queues)
{
    public const string RoleKey = "Hangfire:Role";
    public const string RunServerKey = "Hangfire:RunServer";
    public const string WorkerCountKey = "Hangfire:WorkerCount";
    public const int DefaultWorkerCount = 2;

    /// <summary>
    /// Outbox channels owned by this host. The business worker only processes business deliveries;
    /// the communications worker only processes communications deliveries. The legacy
    /// <see cref="HangfireRole.All"/> mode keeps both in a single process for compatibility.
    /// </summary>
    public IReadOnlyList<OutboxChannel> OutboxChannels => Role switch
    {
        HangfireRole.All => [OutboxChannel.Business, OutboxChannel.Communications],
        HangfireRole.Business => [OutboxChannel.Business],
        HangfireRole.Communications => [OutboxChannel.Communications],
        _ => []
    };

    /// <summary>Whether this host runs an outbox dispatcher at all.</summary>
    public bool RunsOutbox => OutboxChannels.Count > 0;

    /// <summary>Dedicated worker processes may not run with the legacy web/all roles.</summary>
    public bool IsDedicatedWorkerRole => Role is HangfireRole.Business or HangfireRole.Communications;

    public static HangfireRuntime Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredRole = configuration[RoleKey];
        var role = HangfireRole.All;

        if (!string.IsNullOrWhiteSpace(configuredRole)
            && (!Enum.TryParse(configuredRole.Trim(), ignoreCase: true, out role)
                || !Enum.IsDefined(typeof(HangfireRole), role)))
        {
            throw new InvalidOperationException(
                $"Configuration '{RoleKey}' must be one of: {string.Join(", ", Enum.GetNames<HangfireRole>())}.");
        }

        return Create(
            role,
            ParseOptionalBoolean(configuration[RunServerKey], RunServerKey),
            ParseWorkerCount(configuration[WorkerCountKey]));
    }

    public static HangfireRuntime Create(
        HangfireRole role,
        bool? runsServer = null,
        int workerCount = DefaultWorkerCount)
    {
        if (!Enum.IsDefined(typeof(HangfireRole), role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown Hangfire role.");
        }

        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "Worker count must be greater than zero.");
        }

        return new HangfireRuntime(
            role,
            runsServer ?? role != HangfireRole.Web,
            role is HangfireRole.Business or HangfireRole.All,
            workerCount,
            QueuesFor(role));
    }

    private static IReadOnlyList<string> QueuesFor(HangfireRole role) => role switch
    {
        HangfireRole.Business => HangfireQueues.BusinessServerQueues,
        HangfireRole.Communications => HangfireQueues.CommunicationsServerQueues,
        HangfireRole.Web => HangfireQueues.DefaultServerQueues,
        _ => HangfireQueues.AllServerQueues
    };

    private static bool? ParseOptionalBoolean(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be 'true' or 'false'.");
        }

        return parsed;
    }

    private static int ParseWorkerCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultWorkerCount;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            throw new InvalidOperationException($"Configuration '{WorkerCountKey}' must be a positive integer.");
        }

        return count;
    }
}
