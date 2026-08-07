namespace Urbeat.Application.DTOs;

public sealed class ProcessWebhookResultDto
{
    public bool Ignored { get; init; }

    public bool PaymentNotFound { get; init; }

    public bool Processed { get; init; }
}
