using Urbeat.Infrastructure.Outbox;
using Urbeat.WebApi.Health;
using Urbeat.WebApi.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Threading.RateLimiting;

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

        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>(OutboxHealthCheck.Name, tags: new[] { "outbox" });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(options =>
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            // Development/test fallback for the local Angular dev server, Ionic/Capacitor shells and
            // the local HTTP stack. Production origins must be provided explicitly through
            // configuration; an empty list fails closed (no cross-origin browser access) instead of
            // reflecting arbitrary origins.
            if (allowedOrigins.Length == 0 && environment.IsDevelopment())
            {
                allowedOrigins =
                [
                    "http://localhost:4200",
                    "http://localhost:8100",
                    "capacitor://localhost",
                    "http://localhost"
                ];
            }

            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        var rateLimitingOptions = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.AuthWindowSeconds),
                    QueueLimit = 0
                }));

            options.AddPolicy("password-recovery", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.PasswordRecoveryPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.PasswordRecoveryWindowSeconds),
                    QueueLimit = 0
                }));
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
                    .AddMeter(OutboxMetrics.MeterName)
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
