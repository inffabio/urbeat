using Urbeat.WebApi.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Urbeat.WebApi.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var signalR = services.AddSignalR();

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

        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new ShortGuidModelBinderProvider());
        });

        services.AddHealthChecks();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.SetIsOriginAllowed(_ => true)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource("Urbeat.WebApi")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (environment.IsDevelopment())
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("Urbeat.WebApi")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (environment.IsDevelopment())
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}
