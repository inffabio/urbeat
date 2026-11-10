using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Urbeat.WebApi.Health;

/// <summary>
/// Minimal PostgreSQL reachability probe for the administrative operations panel. It is tagged
/// <c>operations</c>, so it is not part of the legacy <c>/health</c> or <c>/health/ready</c>
/// contracts, and it never returns the connection string or an exception message.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    public const string Name = "postgresql";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresHealthCheck> _logger;

    public PostgresHealthCheck(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgresHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("PostgreSQL reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL unreachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log only the type: provider exceptions may embed the connection string.
            _logger.LogWarning(
                "PostgreSQL health probe failed ({ErrorType}).",
                ex.GetType().Name);

            return HealthCheckResult.Unhealthy("PostgreSQL unreachable.");
        }
    }
}
