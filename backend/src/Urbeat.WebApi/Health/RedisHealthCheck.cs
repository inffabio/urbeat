using Urbeat.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Urbeat.WebApi.Health;

/// <summary>
/// Redis reachability probe for the administrative operations panel. It uses the shared DI
/// multiplexer with a bounded ping timeout and reports only healthy/degraded/unhealthy, never the
/// endpoint or credentials. The check is tagged <c>operations</c> and is not part of the public health
/// endpoint contract.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    public const string Name = "redis";

    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(
        IServiceProvider services,
        ILogger<RedisHealthCheck> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve inside the guarded block: a failed lazy multiplexer initialization is a
            // dependency failure, not an exception that should escape the admin projection.
            var connection = _services.GetService<IConnectionMultiplexer>();
            if (connection is null)
            {
                return HealthCheckResult.Degraded("Redis não configurado.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await connection.GetDatabase().PingAsync().WaitAsync(PingTimeout, cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy("Redis reachable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Redis exceptions may include the endpoint; log only the type.
            _logger.LogWarning(
                "Redis health probe failed ({ErrorType}).",
                ex.GetType().Name);

            return HealthCheckResult.Unhealthy("Redis unreachable.");
        }
    }
}
