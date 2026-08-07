using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Urbeat.Application.Dtos;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/printer-config")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class PrinterConfigController : ControllerBase
{
    private readonly IPrinterConfigService _service;
    private static readonly Regex PrivateIpRegex = new(
        @"^(10\.|172\.(1[6-9]|2\d|3[01])\.|192\.168\.)",
        RegexOptions.Compiled);

    public PrinterConfigController(IPrinterConfigService service)
    {
        _service = service;
    }

    [HttpGet("presets")]
    public async Task<IActionResult> GetPresets(CancellationToken cancellationToken)
    {
        var presets = await _service.GetPresetsAsync(cancellationToken);
        return Ok(presets);
    }

    [HttpGet("store")]
    public async Task<IActionResult> GetStoreConfig(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var config = await _service.GetStoreConfigAsync(userId.Value, cancellationToken);
        if (config is null) return NoContent();
        return Ok(config);
    }

    [HttpPut("store")]
    public async Task<IActionResult> SaveStoreConfig([FromBody] StorePrinterConfigRequestDto request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _service.SaveStoreConfigAsync(userId.Value, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("wifi-print")]
    public async Task<IActionResult> WifiPrint([FromBody] WifiPrintRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress) || string.IsNullOrWhiteSpace(request.Base64Data))
            return BadRequest("IP e dados sao obrigatorios.");

        if (!PrivateIpRegex.IsMatch(request.IpAddress))
            return BadRequest("Apenas IPs de rede local sao permitidos.");

        var port = request.Port > 0 ? request.Port : 9100;
        var data = Convert.FromBase64String(request.Base64Data);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(request.IpAddress), port, cancellationToken);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return Ok(new { ok = true, message = "Enviado para a impressora Wi-Fi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, message = $"Falha ao conectar na impressora: {ex.Message}" });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}

public sealed class WifiPrintRequest
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9100;
    public string Base64Data { get; set; } = string.Empty;
}
