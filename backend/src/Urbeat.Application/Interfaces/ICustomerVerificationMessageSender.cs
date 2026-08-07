namespace Urbeat.Application.Interfaces;

public interface ICustomerVerificationMessageSender
{
    CustomerVerificationChannel Channel { get; }

    Task SendOtpAsync(string fromPhone, string toPhone, string code, CancellationToken cancellationToken);
}

public enum CustomerVerificationChannel
{
    Sms = 0,
    WhatsApp = 1
}
