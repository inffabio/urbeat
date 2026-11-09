using Urbeat.Application.DependencyInjection;
using Urbeat.Infrastructure.DependencyInjection;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.DependencyInjection;
using Urbeat.WebApi.Infrastructure;
using Urbeat.WebApi.Middlewares;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logsConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.UrbeatLogs.json");
if (File.Exists(logsConfigPath))
{
    builder.Configuration.AddJsonFile(logsConfigPath, optional: true, reloadOnChange: true);
}

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructureJobs(builder.Configuration, builder.Environment);
builder.Services.AddWebApi(builder.Configuration, builder.Environment);

var app = builder.Build();

var hangfireRuntime = app.Services.GetRequiredService<HangfireRuntime>();

using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    // The SignalR Redis backplane decision is logged without the connection string, so operators can
    // observe whether the backplane is active (or silently disabled by a missing explicit flag)
    // without ever exposing the Redis secret. Actual connection failures are logged by the Redis
    // client at runtime.
    var signalRRedisBackplaneEnabled = SignalRRedisBackplane.ShouldEnable(builder.Configuration, builder.Environment);
    startupLogger.LogInformation(
        "SignalR Redis backplane {BackplaneState}.",
        signalRRedisBackplaneEnabled ? "enabled" : "disabled");

    // Operational role is logged without connection strings so web and worker containers can be
    // told apart in logs. 'All' keeps the legacy single-process behavior when Hangfire:Role is
    // not configured.
    startupLogger.LogInformation(
        "Hangfire role {Role}; server {ServerState}; queues {Queues}; recurring scheduler {RecurringState}; outbox {OutboxState}.",
        hangfireRuntime.Role,
        hangfireRuntime.RunsServer ? "enabled" : "disabled",
        string.Join(",", hangfireRuntime.Queues),
        hangfireRuntime.RegistersRecurringJobs ? "enabled" : "disabled",
        hangfireRuntime.RunsOutbox ? "local" : "remote");

    if (hangfireRuntime.Role == HangfireRole.All && !builder.Environment.IsDevelopment())
    {
        startupLogger.LogWarning(
            "Hangfire is running in legacy 'All' mode. Configure Hangfire:Role=Web on the API and run the dedicated workers to split job processing.");
    }

    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Applying migrations is mandatory for a relational data store: a database that cannot be
    // migrated must abort startup rather than continue against a stale or missing schema. This is
    // deliberately outside the seeder try/catch below so a migration failure fails startup. The
    // non-relational provider (InMemory in the test harness) skips migrations entirely and the
    // test harness seeds its own reference data.
    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();

        try
        {
            var billingPlanSeeder = scope.ServiceProvider.GetRequiredService<BillingPlanSeeder>();
            await billingPlanSeeder.SeedAsync();

            var cuisineSeeder = scope.ServiceProvider.GetRequiredService<CuisineTypeSeeder>();
            await cuisineSeeder.SeedAsync();

            var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminUserSeeder>();
            await adminSeeder.SeedAsync();

            // DemoDataSeeder disabled — use only when needed for development
            // var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
            // await demoSeeder.SeedAsync();

            var systemParamSeeder = scope.ServiceProvider.GetRequiredService<SystemParameterSeeder>();
            await systemParamSeeder.SeedAsync();

            var landingPageSeeder = scope.ServiceProvider.GetRequiredService<LandingPageSeeder>();
            await landingPageSeeder.SeedAsync();

            var demoSubscriptionChargeSeeder = scope.ServiceProvider.GetRequiredService<DemoSubscriptionChargeSeeder>();
            await demoSubscriptionChargeSeeder.SeedAsync();
        }
        catch (Exception exception)
        {
            startupLogger.LogWarning(exception, "Seeding skipped because the data store is unavailable.");
        }

        // The per-role credential backfill is part of schema/consistency migration, not optional
        // demo data: it runs after seeders and outside the catch above so a failure aborts startup
        // instead of silently running without Customer/Seller credentials. Existing rows are never
        // overwritten, and users without a legacy hash are logged and preserved.
        var credentialBackfill = scope.ServiceProvider.GetRequiredService<UserRoleCredentialBackfillService>();
        await credentialBackfill.BackfillAsync();
    }
}

// Atrás de proxy (nginx) — confiar apenas nos cabeçalhos X-Forwarded-* de proxies conhecidos.
// KnownProxies/KnownNetworks são lidos da configuração; uma configuração vazia não confia em
// nenhuma origem externa, impedindo o spoofing de X-Forwarded-For para burlar o rate limiting.
app.UseForwardedHeaders(ForwardedHeadersConfiguration.Create(builder.Configuration));

app.UseSwagger();
app.UseSwaggerUI();

// Hangfire dashboard protegido por Basic Auth (credenciais via config)
var hangfireUser = builder.Configuration["Hangfire:DashboardUser"] ?? "admin";
var hangfirePass = builder.Configuration["Hangfire:DashboardPassword"] ?? "urbeat-hangfire";
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireBasicAuthFilter(hangfireUser, hangfirePass) }
});

app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseMetricServer();
app.UseHttpMetrics();
app.UseSerilogRequestLogging(options =>
{
    // The public route token is a bearer secret. The default request log writes the path,
    // so it must be replaced with a stable placeholder for that endpoint.
    options.MessageTemplate =
        "HTTP {RequestMethod} {SanitizedRequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        diagnosticContext.Set(
            "SanitizedRequestPath",
            path.StartsWith("/api/public/delivery-routes/", StringComparison.OrdinalIgnoreCase)
                ? "/api/public/delivery-routes/{token}"
                : path.StartsWith("/api/public/deliveries/", StringComparison.OrdinalIgnoreCase)
                    ? "/api/public/deliveries/{token}"
                    : path);
    };
});
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// HTTPS redirection só em Development quando rodando standalone
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.MapHub<Urbeat.WebApi.Hubs.SellerNotificationHub>("/hubs/seller-notifications");
app.MapHub<Urbeat.WebApi.Hubs.CustomerNotificationHub>("/hubs/customer-notifications");

// Only the business role (or the legacy 'All' role) registers recurring jobs. In the split
// deployment the API runs as 'Web' and the business worker is the single scheduler.
if (HangfireRecurringJobs.TryRegisterBusinessJobs(
        app.Services.GetRequiredService<IRecurringJobManager>(),
        hangfireRuntime.Role))
{
    app.Logger.LogInformation("Hangfire recurring business jobs registered.");
}

app.Run();

public partial class Program;

public static class BusinessMetrics
{
    public static readonly Counter OrdersCreated = Metrics.CreateCounter("urbeat_orders_created_total", "Total de pedidos criados");
    public static readonly Counter PaymentFailures = Metrics.CreateCounter("urbeat_payment_failures_total", "Total de falhas de pagamento");
    public static readonly Counter NewUsers = Metrics.CreateCounter("urbeat_new_users_total", "Novos usuários cadastrados");
    public static readonly Counter NewStores = Metrics.CreateCounter("urbeat_new_stores_total", "Novas lojas cadastradas");
    public static readonly Counter ProductsUpdated = Metrics.CreateCounter("urbeat_products_updated_total", "Produtos cadastrados/atualizados");
}
