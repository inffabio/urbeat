using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;
using Urbeat.PrintAgent.Storage;

var builder = WebApplication.CreateBuilder(args);

var loopbackUrl = builder.Configuration["Agent:Url"] ?? "http://127.0.0.1:43111";
builder.WebHost.UseUrls(loopbackUrl);

builder.Services.AddSingleton<ILocalPrinterDiscovery, LocalPrinterDiscovery>();
builder.Services.AddSingleton<ILocalPrintExecutor, LocalPrintExecutor>();
builder.Services.AddSingleton<IPrintJobService, PrintJobService>();
builder.Services.AddSingleton(provider =>
{
    var configuredPath = builder.Configuration["Agent:ConfigPath"];
    var configPath = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(AppContext.BaseDirectory, "agent-config.json")
        : configuredPath;

    if (!Path.IsPathRooted(configPath))
    {
        configPath = Path.Combine(AppContext.BaseDirectory, configPath);
    }

    return new AgentConfigStore(configPath);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse
{
    Status = "ok",
    Mode = "local-agent",
    BoundAddress = new Uri(loopbackUrl).Host
}));

app.MapGet("/config", async (AgentConfigStore store, ILocalPrinterDiscovery discovery, CancellationToken cancellationToken) =>
{
    var config = await store.LoadAsync(cancellationToken) ?? BuildDefaultConfig(discovery);
    return Results.Ok(config);
});

app.MapPost("/config", async (SaveConfigRequest request, AgentConfigStore store, ILocalPrinterDiscovery discovery, CancellationToken cancellationToken) =>
{
    var config = BuildConfig(request, discovery);
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

static AgentPrinterConfig BuildConfig(SaveConfigRequest request, ILocalPrinterDiscovery discovery)
{
    var profile = discovery.GetProfile(request.PreferredProfile);

    return new AgentPrinterConfig
    {
        PreferredMode = string.IsNullOrWhiteSpace(request.PreferredMode) ? "local-agent" : request.PreferredMode,
        PreferredProfile = profile.ProfileId,
        PrinterName = request.PrinterName,
        PaperWidth = profile.PaperWidth,
        AutoCut = profile.SupportsAutoCut && request.AutoCut,
        LocalToken = request.LocalToken
    };
}

public partial class Program;
