using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Services;
using Urbeat.WebApi.Health;
using Urbeat.WebApi.Infrastructure;
using Urbeat.WebApi.Operations;
using Urbeat.WebApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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

        // The typed publisher lives in WebApi because it owns the hub contexts; Infrastructure only
        // depends on the transport-free ILiveNotificationPublisher abstraction.
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

        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new ShortGuidModelBinderProvider());
        });

        // The outbox check is exposed by the legacy /health endpoint and by /health/ready. It keeps
        // the authoritative backlog/failure/liveness signals but reuses its aggregate database
        // snapshot for a short window so a burst of probes cannot multiply count queries.
        services.AddSingleton<OutboxHealthCountsCache>();
        services.AddScoped<OutboxOperationsSnapshotReader>();
        services.AddSingleton<OutboxOperationsSnapshotCache>();
        services.AddSingleton<OutboxWorkerProbe>();
        services.AddScoped<OutboxOperationsService>();

        // PostgreSQL and Redis probes back the admin operations panel only. They are tagged
        // 'operations' so the legacy /health and /health/ready contracts keep their existing scope.
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck>(OutboxHealthCheck.Name, tags: new[] { "outbox", "ready" })
            .AddCheck<PostgresHealthCheck>(
                PostgresHealthCheck.Name,
                tags: new[] { "operations" },
                timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RedisHealthCheck>(
                RedisHealthCheck.Name,
                tags: new[] { "operations" },
                timeout: TimeSpan.FromSeconds(3));

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

        // The promotion policy is resolved at request time so test/ops overrides that are added to
        // the configuration after this method runs (WebApplicationFactory) still take effect.
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        // The resend bucket is the authoritative protection for the public resend endpoint; the
        // client cooldown hint is independent from it, so only its own limit/window are validated.
        services.AddOptions<RateLimitingOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth", httpContext =>
            {
                var current = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
                    .CurrentValue;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = current.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(current.AuthWindowSeconds),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy("password-recovery", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.PasswordRecoveryPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.PasswordRecoveryWindowSeconds),
                    QueueLimit = 0
                }));

            // Dedicated bucket for the break-glass admin recovery: a legitimate operation is one
            // request, and repeated attempts must not be usable as a key oracle.
            options.AddPolicy("admin-recovery", httpContext =>
            {
                var current = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
                    .CurrentValue;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = current.AdminRecoveryPermitLimit,
                        Window = TimeSpan.FromSeconds(current.AdminRecoveryWindowSeconds),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy("role-promotion", httpContext =>
            {
                var current = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
                    .CurrentValue;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = current.RolePromotionPermitLimit,
                        Window = TimeSpan.FromSeconds(current.RolePromotionWindowSeconds),
                        QueueLimit = 0
                    });
            });

            // Resend has its own bucket: sharing "auth" would let a resend attempt exhaust (or be
            // exhausted by) login/registration traffic, and the frontend cooldown is only a hint.
            options.AddPolicy("email-resend", httpContext =>
            {
                var current = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
                    .CurrentValue;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = current.EmailResendPermitLimit,
                        Window = TimeSpan.FromSeconds(current.EmailResendWindowSeconds),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy("public-route", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.PublicRoutePermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.PublicRouteWindowSeconds),
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
