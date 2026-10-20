using System.Reflection;
using Microsoft.Extensions.Hosting.WindowsServices;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;
using Urbeat.PrintAgent.Storage;

var builder = WebApplication.CreateBuilder(args);

var runningAsWindowsService = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();
if (runningAsWindowsService)
{
    builder.Host.UseWindowsService(options => options.ServiceName = "UrbeatPrintAgent");
}

var loopbackUrl = builder.Configuration["Agent:Url"] ?? "http://127.0.0.1:43111";
EnsureLoopbackUrl(loopbackUrl);
builder.WebHost.UseUrls(loopbackUrl);

var allowedOrigins = builder.Configuration.GetSection("Agent:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://urbeat.com.br", "https://www.urbeat.com.br", "http://localhost:4200", "http://127.0.0.1:4200" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var configPath = ResolveConfigPath(builder.Configuration, runningAsWindowsService);
var configStore = new AgentConfigStore(configPath);
var initialConfig = await configStore.LoadAsync(CancellationToken.None);
var localToken = initialConfig?.LocalToken;
if (string.IsNullOrWhiteSpace(localToken))
{
    localToken = LocalAgentTokenProvider.Generate();
    initialConfig ??= new AgentPrinterConfig();
    initialConfig.LocalToken = localToken;
    await configStore.SaveAsync(initialConfig, CancellationToken.None);
}

builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton(new LocalAgentTokenProvider(localToken));
builder.Services.AddSingleton<ILocalPrinterDiscovery, LocalPrinterDiscovery>();
builder.Services.AddSingleton<ILocalPrintExecutor, LocalPrintExecutor>();
builder.Services.AddSingleton<IPrintJobStore>(_ => new PrintJobStore(
    ResolveJobsPath(builder.Configuration, runningAsWindowsService)));
builder.Services.AddSingleton<IPrintJobService, PrintJobService>();

var app = builder.Build();

app.UseCors();

var tokenProvider = app.Services.GetRequiredService<LocalAgentTokenProvider>();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        await next();
        return;
    }

    var path = context.Request.Path;
    if (path.Equals("/health"))
    {
        await next();
        return;
    }

    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrWhiteSpace(origin) && !allowedOrigins.Contains(origin, StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (path.Equals("/pair") && HttpMethods.IsPost(context.Request.Method))
    {
        if (!context.Request.Headers.ContainsKey("X-Urbeat-Pair"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (string.IsNullOrWhiteSpace(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await next();
        return;
    }

    var providedToken = context.Request.Headers["X-Urbeat-Local-Token"].ToString();
    if (!tokenProvider.IsValid(providedToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});

app.MapGet("/health", (IPrintJobStore jobStore) => Results.Ok(new HealthResponse
{
    Status = "ok",
    Mode = "local-agent",
    BoundAddress = new Uri(loopbackUrl).Host,
    RunningAsService = runningAsWindowsService,
    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
    StorageWarning = (jobStore as PrintJobStore)?.LastLoadError
}));

// First-run pairing. Only allowed from an explicitly allowed dashboard origin and with the
// dedicated header, which forces a browser CORS preflight. The token itself is persisted in the
// agent config and never returned by any data endpoint.
app.MapPost("/pair", (LocalAgentTokenProvider provider) => Results.Ok(new PairAgentResponse
{
    Token = provider.Token
}));

app.MapGet("/config", async (AgentConfigStore store, ILocalPrinterDiscovery discovery, CancellationToken cancellationToken) =>
{
    var config = await store.LoadAsync(cancellationToken) ?? BuildDefaultConfig(discovery);
    return Results.Ok(config);
});

app.MapPost("/config", async (SaveConfigRequest request, AgentConfigStore store, ILocalPrinterDiscovery discovery, LocalAgentTokenProvider token, CancellationToken cancellationToken) =>
{
    var existing = await store.LoadAsync(cancellationToken);
    var config = BuildConfig(request, discovery, existing?.LocalToken ?? token.Token);
    await store.SaveAsync(config, cancellationToken);
    return Results.Ok(config);
});

app.MapGet("/printers", async (ILocalPrinterDiscovery discovery, CancellationToken cancellationToken) =>
{
    var installedPrinters = await discovery.ListInstalledPrintersAsync(cancellationToken);
    return Results.Ok(new PrinterCatalogResponse
    {
        RecommendedProfiles = discovery.GetRecommendedProfiles(),
        InstalledPrinters = installedPrinters
    });
});

app.MapPost("/print/test", async (PrintTestRequest request, AgentConfigStore store, IPrintJobService printJobService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.PrinterName))
    {
        request.PrinterName = (await store.LoadAsync(cancellationToken))?.PrinterName ?? string.Empty;
    }

    var result = await printJobService.BuildTestJobAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/print/order", async (PrintOrderRequest request, AgentConfigStore store, IPrintJobService printJobService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.PrinterName))
    {
        request.PrinterName = (await store.LoadAsync(cancellationToken))?.PrinterName ?? string.Empty;
    }

    var result = await printJobService.BuildOrderJobAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/jobs", (IPrintJobStore jobStore) => Results.Ok(jobStore.List()));

app.MapGet("/jobs/{id}", (string id, IPrintJobStore jobStore) =>
{
    var job = jobStore.Get(id);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

// Explicit recovery for ambiguous/failed jobs only. Ambiguous jobs are never auto-resent.
app.MapPost("/jobs/{id}/retry", async (string id, IPrintJobService printJobService, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await printJobService.RetryJobAsync(id, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { detail = exception.Message });
    }
});

app.Run();

static AgentPrinterConfig BuildDefaultConfig(ILocalPrinterDiscovery discovery)
{
    var profile = discovery.GetProfile("pos-58");

    return new AgentPrinterConfig
    {
        PreferredMode = "local-agent",
        PreferredProfile = profile.ProfileId,
        PaperWidth = profile.PaperWidth,
        AutoCut = profile.SupportsAutoCut
    };
}

static AgentPrinterConfig BuildConfig(SaveConfigRequest request, ILocalPrinterDiscovery discovery, string localToken)
{
    var profile = discovery.GetProfile(request.PreferredProfile);

    return new AgentPrinterConfig
    {
        PreferredMode = string.IsNullOrWhiteSpace(request.PreferredMode) ? "local-agent" : request.PreferredMode,
        PreferredProfile = profile.ProfileId,
        PrinterName = request.PrinterName,
        PaperWidth = profile.PaperWidth,
        AutoCut = profile.SupportsAutoCut && request.AutoCut,
        LocalToken = localToken
    };
}

static void EnsureLoopbackUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException("Agent:Url must be an absolute URL bound to loopback.");
    }

    var host = uri.Host.Trim('[', ']');
    var isLoopback = string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.Ordinal);

    if (!isLoopback)
    {
        throw new InvalidOperationException("The print agent API must be bound to loopback only.");
    }
}

static string ResolveConfigPath(IConfiguration configuration, bool isWindowsService)
{
    var configured = configuration["Agent:ConfigPath"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    return Path.Combine(ResolveDataRoot(isWindowsService), "agent-config.json");
}

static string ResolveJobsPath(IConfiguration configuration, bool isWindowsService)
{
    var configured = configuration["Agent:JobsPath"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    return Path.Combine(ResolveDataRoot(isWindowsService), "print-jobs.json");
}

static string ResolveDataRoot(bool isWindowsService)
{
    var dataRoot = isWindowsService && OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    if (string.IsNullOrWhiteSpace(dataRoot))
    {
        dataRoot = AppContext.BaseDirectory;
    }

    return Path.Combine(dataRoot, "Urbeat", "PrintAgent");
}

public partial class Program;
