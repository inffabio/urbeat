using Urbeat.Application.Payments;
using Urbeat.Application.Subscriptions;
using Urbeat.Application.WhatsApp;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.WhatsApp;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly ISender _sender;
    private readonly AsaasWebhookOptions _asaasWebhookOptions;
    private readonly WhatsAppOptions _whatsAppOptions;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        ISender sender,
        IOptions<AsaasWebhookOptions> asaasWebhookOptions,
        IOptions<WhatsAppOptions> whatsAppOptions,
        ILogger<WebhooksController> logger)
    {
        _sender = sender;
        _asaasWebhookOptions = asaasWebhookOptions.Value;
        _whatsAppOptions = whatsAppOptions.Value;
        _logger = logger;
    }

    [HttpPost("mercadopago")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MercadoPago(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        await _sender.Send(new ProcessMercadoPagoWebhookCommand(
            payload,
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);

        return Ok(new { received = true });
    }

    [HttpPost("asaas")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Asaas(CancellationToken cancellationToken)
    {
        if (!IsValidAsaasToken())
        {
            return Unauthorized();
        }

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        await _sender.Send(new ProcessAsaasWebhookCommand(
            payload,
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);

        return Ok(new { received = true });
    }

    [HttpGet("whatsapp")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult VerifyWhatsApp()
    {
        var mode = Request.Query["hub.mode"].FirstOrDefault();
        var providedToken = Request.Query["hub.verify_token"].FirstOrDefault();
        var challenge = Request.Query["hub.challenge"].FirstOrDefault();

        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(challenge)
            || !WhatsAppWebhookSignature.TokensMatch(_whatsAppOptions.WebhookVerifyToken, providedToken))
        {
            _logger.LogWarning("WhatsApp webhook verification rejected.");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Content(challenge, "text/plain");
    }

    [HttpPost("whatsapp")]
    [AllowAnonymous]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> WhatsApp(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_whatsAppOptions.AppSecret))
        {
            _logger.LogWarning("WhatsApp webhook rejected because the integration is not configured.");
            return Unauthorized();
        }

        using var bodyBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(bodyBuffer, cancellationToken);
        var payloadBytes = bodyBuffer.ToArray();

        var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!WhatsAppWebhookSignature.IsValidSignature(_whatsAppOptions.AppSecret, payloadBytes, signatureHeader))
        {
            _logger.LogWarning("WhatsApp webhook rejected because the signature is missing or invalid.");
            return Unauthorized();
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var result = await _sender.Send(
            new ProcessWhatsAppWebhookCommand(
                payload,
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        if (result.InvalidPayload)
        {
            return BadRequest(new { error = "Invalid WhatsApp webhook payload." });
        }

        return Ok(new { received = true });
    }

    private bool IsValidAsaasToken()
    {
        if (string.IsNullOrWhiteSpace(_asaasWebhookOptions.Token))
        {
            return false;
        }

        var requestToken = Request.Headers["asaas-access-token"].FirstOrDefault();
        return string.Equals(requestToken, _asaasWebhookOptions.Token, StringComparison.Ordinal);
    }
}
