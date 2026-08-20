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
            PreferredConnection = "android-bluetooth|wifi",
            Connection = "usb",
            Protocol = "escpos",
            Capabilities = new[] { "thermal" }
        },
        new()
        {
            ProfileId = "thermal-80",
            DisplayName = "Thermal 80",
            PaperWidth = "80mm",
            SupportsAutoCut = true,
            PreferredConnection = "wifi",
            Connection = "wifi",
            Protocol = "escpos",
            Capabilities = new[] { "thermal", "auto-cut" }
        },
        new()
        {
            ProfileId = "generic-pos-58-usb",
            DisplayName = "Genérica POS-58 USB",
            PaperWidth = "58mm",
            SupportsAutoCut = false,
            PreferredConnection = "usb",
            Connection = "usb",
            Protocol = "escpos",
            Capabilities = new[] { "thermal" }
        },
        new()
        {
            ProfileId = "generic-pos-58-bluetooth-android",
            DisplayName = "Genérica POS-58 Bluetooth (Android)",
            PaperWidth = "58mm",
            SupportsAutoCut = false,
            PreferredConnection = "bluetooth",
            Connection = "bluetooth",
            Protocol = "escpos",
            Capabilities = new[] { "thermal" },
            CatalogOnly = true
        },
        new()
        {
            ProfileId = "generic-pos-58-wifi",
            DisplayName = "Genérica POS-58 Wi-Fi",
            PaperWidth = "58mm",
            SupportsAutoCut = false,
            PreferredConnection = "wifi",
            Connection = "wifi",
            Protocol = "escpos",
            Capabilities = new[] { "thermal", "wifi" }
        },
        new()
        {
            ProfileId = "generic-pos-80-wifi",
            DisplayName = "Genérica POS-80 Wi-Fi",
            PaperWidth = "80mm",
            SupportsAutoCut = true,
            PreferredConnection = "wifi",
            Connection = "wifi",
            Protocol = "escpos",
            Capabilities = new[] { "thermal", "auto-cut", "wifi" }
        },
        new()
        {
            ProfileId = "epson-tm-m30iii",
            DisplayName = "Epson TM-m30III (Wi-Fi / Ethernet)",
            PaperWidth = "80mm",
            SupportsAutoCut = true,
            PreferredConnection = "wifi|ethernet",
            Connection = "wifi|ethernet",
            Protocol = "escpos",
            Capabilities = new[] { "thermal", "auto-cut", "wifi", "ethernet" },
            Experimental = true
        },
        new()
        {
            ProfileId = "epson-tm-t20iii",
            DisplayName = "Epson TM-T20III (USB / Ethernet)",
            PaperWidth = "80mm",
            SupportsAutoCut = true,
            PreferredConnection = "usb|ethernet",
            Connection = "usb|ethernet",
            Protocol = "escpos",
            Capabilities = new[] { "thermal", "auto-cut", "usb", "ethernet" },
            Experimental = true
        }
    ];

    private static readonly string[] LpStatStatusMarkers =
    [
        "não está aceitando solicitações",
        "aceitando solicitações",
        "rejeitando solicitações",
        "not accepting requests",
        "accepting requests",
        "rejecting requests"
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

    public static IReadOnlyList<string> ParseLpStatPrinters(string output)
    {
        var lines = output.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var line in lines)
        {
            var name = ExtractPrinterName(line);
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(name);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

        return ParseLpStatPrinters(output);
    }

    private static string ExtractPrinterName(string line)
    {
        var trimmed = line.Trim();

        foreach (var marker in LpStatStatusMarkers)
        {
            var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return trimmed[..index].Trim();
            }
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace].Trim() : trimmed;
    }
}
