using Urbeat.Application.Payments;
using Urbeat.Application.Subscriptions;
using Urbeat.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly ISender _sender;
    private readonly AsaasWebhookOptions _asaasWebhookOptions;

    public WebhooksController(ISender sender, IOptions<AsaasWebhookOptions> asaasWebhookOptions)
    {
        _sender = sender;
        _asaasWebhookOptions = asaasWebhookOptions.Value;
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
