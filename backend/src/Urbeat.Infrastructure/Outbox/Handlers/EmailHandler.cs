using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Outbox.Handlers;

public sealed class EmailHandler : IOutboxEventHandler
{
    private const string EmailChannel = "Email";
    public const string CustomerConfirmationTemplate = "CustomerConfirmation";
    public const string SellerConfirmationTemplate = "SellerConfirmation";

    private readonly IEmailService _emailService;
    private readonly IOutboxDeliveryTracker _deliveryTracker;
    private readonly IEmailTokenCache _emailTokenCache;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly EmailConfirmationOptions _confirmationOptions;

    public EmailHandler(
        IEmailService emailService,
        IOutboxDeliveryTracker deliveryTracker,
        IEmailTokenCache emailTokenCache,
        UserManager<IdentityUser<Guid>> userManager,
        IOptions<EmailConfirmationOptions> confirmationOptions)
    {
        _emailService = emailService;
        _deliveryTracker = deliveryTracker;
        _emailTokenCache = emailTokenCache;
        _userManager = userManager;
        _confirmationOptions = confirmationOptions.Value;
    }

    public IReadOnlyCollection<string> SupportedTypes => new[] { OutboxEventTypes.OutboundMessageRequested };

    public async Task<OutboxProcessingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var evt = OutboxPayloadSerializer.Deserialize<OutboundMessageRequestedEvent>(message.Payload);

        if (!string.Equals(evt.Channel, EmailChannel, StringComparison.OrdinalIgnoreCase))
        {
            return OutboxProcessingResult.Fail(
                $"Unsupported outbound channel '{evt.Channel}': no handler delivers this channel. " +
                "Message retained in the failed queue for operational review.");
        }

        var deliveryKey = string.IsNullOrWhiteSpace(evt.DeliveryKey)
            ? $"email:{evt.Channel.ToLowerInvariant()}:{evt.Recipient}:{evt.Template}"
            : evt.DeliveryKey;

        if (await _deliveryTracker.IsDeliveredAsync(deliveryKey, cancellationToken))
        {
            return OutboxProcessingResult.Ok();
        }

        var content = IsConfirmationTemplate(evt.Template)
            ? await ResolveConfirmationContentAsync(evt, cancellationToken)
            : new EmailContent(evt.Recipient, evt.ToName, evt.Subject, evt.Body);

        if (content is null)
        {
            return OutboxProcessingResult.Fail(
                "Confirmation e-mail could not be rendered: the confirmation request has expired or is missing. " +
                "Ask the user to request a new confirmation e-mail.");
        }

        try
        {
            // The delivery key doubles as the idempotency key forwarded to the provider. SMTP has no
            // server-side dedup, so delivery remains at-least-once; the persisted delivery record is a
            // best-effort guard, not an exactly-once guarantee.
            var providerMessageId = await _emailService.SendAsync(
                content.Recipient,
                content.ToName,
                content.Subject,
                content.Body,
                textBody: null,
                idempotencyKey: deliveryKey,
                cancellationToken: cancellationToken);

            await _deliveryTracker.RecordDeliveredAsync(
                deliveryKey,
                nameof(EmailHandler),
                message.Id,
                message.AttemptCount,
                providerMessageId,
                cancellationToken);

            return OutboxProcessingResult.Ok();
        }
        catch (Exception exception)
        {
            return OutboxProcessingResult.Retry(exception.Message);
        }
    }

    private static bool IsConfirmationTemplate(string template)
    {
        return string.Equals(template, CustomerConfirmationTemplate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(template, SellerConfirmationTemplate, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<EmailContent?> ResolveConfirmationContentAsync(
        OutboundMessageRequestedEvent evt,
        CancellationToken cancellationToken)
    {
        if (evt.CorrelationId is null || evt.UserId is null)
        {
            return null;
        }

        var request = await _emailTokenCache.GetConfirmationRequestAsync(evt.CorrelationId.Value, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(evt.UserId.Value.ToString());
        if (user is null)
        {
            return null;
        }

        var confirmUrl = BuildConfirmUrl(request.ShortCode);
        var (subject, html) = string.Equals(evt.Template, SellerConfirmationTemplate, StringComparison.OrdinalIgnoreCase)
            ? EmailTemplates.BuildSellerConfirmation(confirmUrl)
            : EmailTemplates.BuildCustomerConfirmation(confirmUrl);

        var toName = user.UserName ?? user.Email ?? string.Empty;
        return new EmailContent(user.Email ?? string.Empty, toName, subject, html);
    }

    private string BuildConfirmUrl(string shortCode)
    {
        return $"{_confirmationOptions.FrontendBaseUrl.TrimEnd('/')}/c/{shortCode}";
    }

    private sealed record EmailContent(string Recipient, string ToName, string Subject, string Body);
}
