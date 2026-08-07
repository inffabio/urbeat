using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Jobs;

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