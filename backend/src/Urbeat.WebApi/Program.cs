using Urbeat.Application.DependencyInjection;
using Urbeat.Infrastructure.DependencyInjection;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.WebApi.DependencyInjection;
using Urbeat.WebApi.Infrastructure;
using Urbeat.WebApi.Middlewares;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddWebApi(builder.Environment);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

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
    }
    catch (Exception exception)
    {
        startupLogger.LogWarning(exception, "Admin seeding skipped because the data store is unavailable.");
    }
}

// Atrás de proxy (nginx) — confiar nos cabeçalhos X-Forwarded-*
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // KnownNetworks/KnownProxies vazios e nginx na mesma rede docker
    KnownNetworks = { },
    KnownProxies = { }
});

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
app.UseMetricServer();
app.UseHttpMetrics();
app.UseSerilogRequestLogging();
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

RecurringJob.AddOrUpdate<OperationalHeartbeatJob>(
    "operational-heartbeat",
    job => job.ExecuteAsync(),
    Cron.Hourly);

RecurringJob.AddOrUpdate<SellerSubscriptionNotificationJob>(
    "seller-subscription-notifications",
    job => job.ExecuteAsync(),
    Cron.Daily);

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
