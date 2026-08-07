using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Services;

public interface ILocalPrinterDiscovery
{
    IReadOnlyList<AgentPrinterDescriptor> GetRecommendedProfiles();

    AgentPrinterDescriptor GetProfile(string? profileId);

    Task<IReadOnlyList<string>> ListInstalledPrintersAsync(CancellationToken cancellationToken);
}
