using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            var defaultConnection = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(defaultConnection))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is required for the Hangfire PostgreSQL storage.");
            }

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
