using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

/// <summary>
/// Test discovery with an explicit (usually empty) installed-printer list. An empty list means the
/// environment cannot verify printers, so the executor remains authoritative.
/// </summary>
internal sealed class TestPrinterDiscovery : ILocalPrinterDiscovery
{
    private readonly LocalPrinterDiscovery _inner = new();
    private readonly IReadOnlyList<string> _installedPrinters;

    public TestPrinterDiscovery()
        : this(Array.Empty<string>())
    {
    }

    public TestPrinterDiscovery(params string[] installedPrinters)
    {
        _installedPrinters = installedPrinters;
    }

    public IReadOnlyList<AgentPrinterDescriptor> GetRecommendedProfiles() => _inner.GetRecommendedProfiles();

    public AgentPrinterDescriptor GetProfile(string? profileId) => _inner.GetProfile(profileId);

    public Task<IReadOnlyList<string>> ListInstalledPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_installedPrinters);
}
