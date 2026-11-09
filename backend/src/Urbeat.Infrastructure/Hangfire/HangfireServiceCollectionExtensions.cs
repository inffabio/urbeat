using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Hangfire;

/// <summary>
/// Shared Hangfire composition for every Urbeat host (API and workers). The storage is always
/// configured; the server is opt-in based on <see cref="HangfireRuntime"/>.
/// </summary>
public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddUrbeatHangfireStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHangfire(config =>
        {
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            {
                config.UseInMemoryStorage();
                return;
            }

            // The same normalized connection string used by EF Core, including the explicit pool
            // limits, so Hangfire cannot create an unbounded Npgsql pool of its own.
            var defaultConnection = NpgsqlPoolConfiguration.ResolveConnectionString(configuration);

            config.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(defaultConnection);
            });
        });

        return services;
    }

    public static IServiceCollection AddUrbeatHangfireServer(
        this IServiceCollection services,
        HangfireRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (!runtime.RunsServer)
        {
            return services;
        }

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = runtime.WorkerCount;
            options.Queues = runtime.Queues.ToArray();
        });

        return services;
    }
}
