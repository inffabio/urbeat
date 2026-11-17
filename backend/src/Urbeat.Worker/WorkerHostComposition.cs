using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Urbeat.Application.DependencyInjection;
using Urbeat.Infrastructure.DependencyInjection;
using Urbeat.Infrastructure.Realtime;

namespace Urbeat.Worker;

/// <summary>
/// Composition of the dedicated outbox worker host. It is isolated from the entry point so tests can
/// assert the container resolves everything a handler needs — in particular the live SignalR
/// publisher and the Redis backplane used to reach the WebApi connections.
/// </summary>
public static class WorkerHostComposition
{
    public static IServiceCollection AddUrbeatWorkerHost(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddApplication();
        // Routing satisfies the authorization primitives registered by Infrastructure without
        // starting Kestrel in this worker process.
        services.AddRouting();
        services.AddInfrastructure(configuration, environment);
        services.AddInfrastructureJobs(configuration, environment);
        // The outbox handlers run here, so this host must be able to publish live SignalR events
        // into the same hubs the WebApi replicas expose (through the shared Redis backplane).
        services.AddUrbeatRealtime(configuration, environment);
        return services;
    }
}
