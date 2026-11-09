using Hangfire;
using Microsoft.Extensions.Logging;
using Urbeat.Infrastructure.Hangfire;

namespace Urbeat.Infrastructure.Jobs;

[Queue(HangfireQueues.Business)]
public sealed class OperationalHeartbeatJob
{
    private readonly ILogger<OperationalHeartbeatJob> _logger;

    public OperationalHeartbeatJob(ILogger<OperationalHeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync()
    {
        _logger.LogInformation("Background heartbeat job executed at {ExecutedAtUtc}", DateTime.UtcNow);
        return Task.CompletedTask;
    }
}