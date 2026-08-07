using Urbeat.Application.Interfaces;

namespace Urbeat.Infrastructure.Services;

public sealed class CustomerVerificationOptions
{
    public const string SectionName = "CustomerVerification";

    public CustomerVerificationChannel Channel { get; init; } = CustomerVerificationChannel.Sms;

    public string SmsProvider { get; init; } = "Fake";

    public InfobipSmsOptions Infobip { get; init; } = new();
}

public sealed class InfobipSmsOptions
{
    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string Sender { get; init; } = "Urbeat";
}
