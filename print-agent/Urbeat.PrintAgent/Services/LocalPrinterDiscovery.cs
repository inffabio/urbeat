using Urbeat.PrintAgent.Models;
using System.Diagnostics;

namespace Urbeat.PrintAgent.Services;

public sealed class LocalPrinterDiscovery : ILocalPrinterDiscovery
{
    private static readonly IReadOnlyList<AgentPrinterDescriptor> RecommendedProfiles =
    [
        new()
        {
            ProfileId = "pos-58",
            DisplayName = "POS-58",
            PaperWidth = "58mm",
            SupportsAutoCut = false,
            PreferredConnection = "android-bluetooth|wifi"
        },
        new()
        {
            ProfileId = "thermal-80",
            DisplayName = "Thermal 80",
            PaperWidth = "80mm",
            SupportsAutoCut = true,
            PreferredConnection = "wifi"
        }
    ];

    public IReadOnlyList<AgentPrinterDescriptor> GetRecommendedProfiles() => RecommendedProfiles;

    public AgentPrinterDescriptor GetProfile(string? profileId) =>
        RecommendedProfiles.FirstOrDefault(profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        ?? RecommendedProfiles[0];

    public async Task<IReadOnlyList<string>> ListInstalledPrintersAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await ListWindowsPrintersAsync(cancellationToken);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return await ListLpPrintersAsync(cancellationToken);
        }

        return [];
    }

    private static async Task<IReadOnlyList<string>> ListWindowsPrintersAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("powershell", "-NoProfile -Command \"Get-Printer | Select-Object -ExpandProperty Name\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null) return [];

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> ListLpPrintersAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("lpstat", "-a")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null) return [];

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }
}
