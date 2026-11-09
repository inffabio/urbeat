using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Urbeat.Application.DependencyInjection;
using Urbeat.Infrastructure.DependencyInjection;
using Urbeat.Infrastructure.Hangfire;

// Resolve appsettings relative to the published worker directory regardless of the container
// working directory (the shared backend image keeps WORKDIR=/app).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var logsConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.UrbeatLogs.json");
if (File.Exists(logsConfigPath))
{
    builder.Configuration.AddJsonFile(logsConfigPath, optional: true, reloadOnChange: true);
}

builder.Services.AddSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(builder.Configuration);
});

// Dedicated workers must opt in explicitly. An unset role would otherwise fall back to the legacy
// 'All' behavior and duplicate processing alongside the WebApi host.
if (string.IsNullOrWhiteSpace(builder.Configuration[HangfireRuntime.RoleKey]))
{
    throw new InvalidOperationException(
        $"Configuration '{HangfireRuntime.RoleKey}' is required for Urbeat.Worker. Use 'Business' or 'Communications'.");
}

var hangfireRuntime = HangfireRuntime.Resolve(builder.Configuration);
if (!hangfireRuntime.IsDedicatedWorkerRole)
{
    throw new InvalidOperationException(
        $"Urbeat.Worker requires '{HangfireRuntime.RoleKey}' to be 'Business' or 'Communications'; " +
        $"'{hangfireRuntime.Role}' is reserved for the WebApi host.");
}

builder.Services.AddApplication();
// AddInfrastructure also wires the authorization primitives used by the API. Routing satisfies
// their EndpointDataSource dependency without starting Kestrel in this worker process.
builder.Services.AddRouting();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructureJobs(builder.Configuration, builder.Environment);

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Urbeat.Worker");

logger.LogInformation(
    "Urbeat worker starting | Role={Role} | Queues={Queues} | Server={ServerState} | Outbox={OutboxState} | RecurringScheduler={RecurringState}.",
    hangfireRuntime.Role,
    string.Join(",", hangfireRuntime.Queues),
    hangfireRuntime.RunsServer ? "enabled" : "disabled",
    hangfireRuntime.RunsOutbox ? "local" : "remote",
    hangfireRuntime.RegistersRecurringJobs ? "enabled" : "disabled");

if (HangfireRecurringJobs.TryRegisterBusinessJobs(
        host.Services.GetRequiredService<IRecurringJobManager>(),
        hangfireRuntime.Role))
{
    logger.LogInformation("Hangfire recurring business jobs registered.");
}

await host.RunAsync();
