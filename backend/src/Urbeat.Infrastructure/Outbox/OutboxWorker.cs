using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Outbox;

public sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly HangfireRuntime _hangfireRuntime;
    private readonly IOutboxWorkerLiveness _liveness;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        HangfireRuntime hangfireRuntime,
        IOutboxWorkerLiveness liveness,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _hangfireRuntime = hangfireRuntime;
        _liveness = liveness;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channels = _hangfireRuntime.OutboxChannels;

        if (channels.Count == 0)
        {
            _logger.LogInformation(
                "Outbox worker not started for role {Role}; this host does not process outbox deliveries.",
                _hangfireRuntime.Role);
            return;
        }

        // Fail fast when a handler has no channel classification or a dependency is missing. The
        // scoped dispatcher is discarded; each poll iteration still creates its own scope.
        using (var validationScope = _scopeFactory.CreateScope())
        {
            _ = validationScope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        }

        _logger.LogInformation(
            "Outbox worker started | Role={Role} | Channels={Channels}.",
            _hangfireRuntime.Role,
            string.Join(",", channels));
        _liveness.RecordHeartbeat(DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

                foreach (var channel in channels)
                {
                    await dispatcher.DispatchBatchAsync(channel, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox worker iteration failed.");
            }
            finally
            {
                _liveness.RecordHeartbeat(DateTimeOffset.UtcNow);
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox worker stopped.");
    }
}
