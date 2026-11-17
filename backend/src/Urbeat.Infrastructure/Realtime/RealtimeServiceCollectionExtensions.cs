using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Realtime;

/// <summary>
/// Shared realtime composition used by every host that must publish live SignalR events: the WebApi
/// (which maps the hubs) and the outbox worker (which runs the handlers). Registering it in both
/// hosts is what makes <c>Clients.User(...)</c> reach the connections held by the WebApi replicas
/// through the Redis backplane.
/// </summary>
public static class RealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddUrbeatRealtime(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var signalR = services.AddSignalR();

        // The typed publisher is transport-agnostic (IHubContext only), so it is safe to register in
        // a host that does not map hubs — it simply publishes into the shared backplane.
        services.AddScoped<ILiveNotificationPublisher, SignalRLiveNotificationPublisher>();

        if (SignalRRedisBackplane.ShouldEnable(configuration, environment))
        {
            // Uses the same Redis connection string as the cache services. The value is read from
            // configuration and passed directly to the backplane — it is never logged or printed.
            // Connection failures are surfaced through the standard Redis client logging without
            // exposing the connection string (or its embedded password) in the log output.
            signalR.AddStackExchangeRedis(
                configuration[SignalRRedisBackplane.ConnectionStringKey]!,
                options => options.Configuration.AbortOnConnectFail = false);
        }

        return services;
    }
}
