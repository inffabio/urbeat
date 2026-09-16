using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Urbeat.WebApi.Infrastructure;

/// <summary>
/// Builds <see cref="ForwardedHeadersOptions"/> from the <c>ForwardedHeaders</c> configuration
/// section. Trusting <c>X-Forwarded-*</c> headers is only safe when the immediate peer is a known
/// proxy: otherwise any client could spoof <c>X-Forwarded-For</c> and bypass IP-based rate limiting.
/// Only the proxies/networks listed in configuration are trusted (plus the framework loopback
/// defaults), so an empty configuration fails closed instead of trusting every origin.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public const string SectionName = "ForwardedHeaders";

    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            // The scheme is still recovered from the proxy so HTTPS-aware behaviour keeps working
            // once a trusted proxy is configured.
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        foreach (var proxy in configuration.GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy?.Trim(), out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [])
        {
            try
            {
                options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(network?.Trim() ?? string.Empty));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                // Invalid configuration is ignored; an unparsable entry must not broaden trust.
            }
        }

        return options;
    }
}
