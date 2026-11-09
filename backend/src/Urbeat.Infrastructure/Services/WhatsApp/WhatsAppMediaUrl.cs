using System.Net;
using System.Net.Sockets;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// Validates public HTTPS URLs used as WhatsApp media links or as the institutional logo source.
/// Local, loopback, private-network and credentialed URLs are rejected so a configuration mistake
/// cannot turn the profile updater into a local network fetch.
/// </summary>
internal static class WhatsAppMediaUrl
{
    private const int MaxUrlLength = 2048;

    public static bool TryValidate(string? value, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUrlLength)
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || string.IsNullOrWhiteSpace(parsed.Host))
        {
            return false;
        }

        if (parsed.IsLoopback || IsLocalHostName(parsed.Host))
        {
            return false;
        }

        if (IPAddress.TryParse(parsed.Host, out var address)
            && (IPAddress.IsLoopback(address) || IsPrivateAddress(address)))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsLocalHostName(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.Equals(IPAddress.IPv6Loopback);
        }

        return false;
    }
}
