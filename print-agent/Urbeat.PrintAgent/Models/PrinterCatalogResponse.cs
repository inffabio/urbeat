namespace Urbeat.PrintAgent.Models;

public sealed class PrinterCatalogResponse
{
    public IReadOnlyList<AgentPrinterDescriptor> RecommendedProfiles { get; set; } = [];

    public IReadOnlyList<string> InstalledPrinters { get; set; } = [];
}
