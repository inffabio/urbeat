using Hangfire;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Operations;

/// <summary>
/// Reads live outbox worker presence from Hangfire's monitoring API and from the in-process
/// liveness marker. Only role, queue names, timestamps and derived status are returned; server
/// names, machine names, connection data and job payloads are never exposed.
/// </summary>
public sealed class OutboxWorkerProbe
{
    private readonly HangfireRuntime _runtime;
    private readonly IOutboxWorkerLiveness _liveness;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxWorkerProbe> _logger;

    public OutboxWorkerProbe(
        HangfireRuntime runtime,
        IOutboxWorkerLiveness liveness,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxWorkerProbe> logger)
    {
        _runtime = runtime;
        _liveness = liveness;
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger;
    }

    public OutboxWorkerMonitoring Read()
    {
        var now = _timeProvider.GetUtcNow();
        var workers = new List<OutboxWorkerStatus>();
        var available = true;

        try
        {
            var monitoring = JobStorage.Current.GetMonitoringApi();
            foreach (var server in monitoring.Servers())
            {
                var queues = (server.Queues ?? []).ToArray();
                var role = RoleFor(queues);
                var heartbeat = ToUtc(server.Heartbeat);
                workers.Add(BuildWorker(role, queues, heartbeat, "hangfire", now));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The monitoring API can be unavailable while the rest of the application is healthy.
            // Log only the error type so no connection string or storage detail reaches logs.
            available = false;
            _logger.LogWarning(
                "Outbox worker monitoring unavailable ({ErrorType}).",
                ex.GetType().Name);
        }

        if (_runtime.RunsOutbox && !workers.Any(worker => CoversRole(worker.Role, _runtime.Role.ToString())))
        {
            var localHeartbeat = _liveness.LastHeartbeatUtc;
            workers.Add(BuildWorker(
                _runtime.Role.ToString(),
                _runtime.Queues,
                localHeartbeat,
                "process",
                now));
        }

        workers.Sort((left, right) =>
        {
            var roleOrder = string.Compare(left.Role, right.Role, StringComparison.Ordinal);
            return roleOrder != 0 ? roleOrder : string.Compare(left.Source, right.Source, StringComparison.Ordinal);
        });

        if (!available)
        {
            return new OutboxWorkerMonitoring(
                Available: false,
                Status: OperationsStatus.Degraded,
                Message: "Monitoramento do Hangfire indisponível.",
                Workers: workers);
        }

        var expectedRoles = ExpectedRoles();
        var missingRoles = expectedRoles
            .Where(role => !workers.Any(worker =>
                worker.Status == OperationsStatus.Healthy && CoversRole(worker.Role, role)))
            .ToList();

        if (missingRoles.Count > 0)
        {
            var message = workers.Count == 0
                ? "Nenhum worker de outbox visível no Hangfire."
                : $"Sem heartbeat recente para: {string.Join(", ", missingRoles)}.";

            return new OutboxWorkerMonitoring(
                Available: true,
                Status: OperationsStatus.Unhealthy,
                Message: message,
                Workers: workers);
        }

        return new OutboxWorkerMonitoring(
            Available: true,
            Status: OperationsStatus.Healthy,
            Message: null,
            Workers: workers);
    }

    private OutboxWorkerStatus BuildWorker(
        string role,
        IReadOnlyList<string> queues,
        DateTimeOffset? heartbeat,
        string source,
        DateTimeOffset now)
    {
        long? ageSeconds = heartbeat is null
            ? null
            : Math.Max(0, (long)Math.Floor((now - heartbeat.Value).TotalSeconds));

        var status = heartbeat is null || now - heartbeat.Value > _options.WorkerStaleAfter
            ? OperationsStatus.Unhealthy
            : OperationsStatus.Healthy;

        return new OutboxWorkerStatus(
            Role: role,
            Queues: queues,
            LastHeartbeatUtc: heartbeat?.UtcDateTime,
            HeartbeatAgeSeconds: ageSeconds,
            Status: status,
            Source: source);
    }

    private IReadOnlyList<string> ExpectedRoles() => _runtime.Role switch
    {
        HangfireRole.Web => [HangfireRole.Business.ToString(), HangfireRole.Communications.ToString()],
        HangfireRole.All => [HangfireRole.All.ToString()],
        HangfireRole.Business => [HangfireRole.Business.ToString()],
        HangfireRole.Communications => [HangfireRole.Communications.ToString()],
        _ => []
    };

    private static string RoleFor(IReadOnlyCollection<string> queues)
    {
        var hasBusiness = queues.Contains(HangfireQueues.Business, StringComparer.OrdinalIgnoreCase);
        var hasCommunications = queues.Contains(HangfireQueues.Communications, StringComparer.OrdinalIgnoreCase);

        if (hasBusiness && hasCommunications) return HangfireRole.All.ToString();
        if (hasBusiness) return HangfireRole.Business.ToString();
        if (hasCommunications) return HangfireRole.Communications.ToString();
        return HangfireRole.Web.ToString();
    }

    private static bool CoversRole(string workerRole, string expectedRole) => expectedRole switch
    {
        nameof(HangfireRole.All) => workerRole == nameof(HangfireRole.All),
        nameof(HangfireRole.Business) => workerRole is nameof(HangfireRole.Business) or nameof(HangfireRole.All),
        nameof(HangfireRole.Communications) => workerRole is nameof(HangfireRole.Communications) or nameof(HangfireRole.All),
        _ => false
    };

    private static DateTimeOffset? ToUtc(DateTime? value) =>
        value is null
            ? null
            : new DateTimeOffset(
                value.Value.Kind == DateTimeKind.Utc
                    ? value.Value
                    : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
                TimeSpan.Zero);
}
