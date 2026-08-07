using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class FakeWhatsAppVerificationMessageSender : ICustomerVerificationMessageSender
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FakeWhatsAppVerificationMessageSender> _logger;

    public FakeWhatsAppVerificationMessageSender(IHostEnvironment environment, ILogger<FakeWhatsAppVerificationMessageSender> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public CustomerVerificationChannel Channel => CustomerVerificationChannel.WhatsApp;

    public Task SendOtpAsync(string fromPhone, string toPhone, string code, CancellationToken cancellationToken)
    {
        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            _logger.LogInformation("Fake WhatsApp OTP | From={FromPhone} | To={ToPhone} | Code={Code}", fromPhone, toPhone, code);
        }
        else
        {
            _logger.LogInformation("Fake WhatsApp OTP | From={FromPhone} | To={ToPhone} | CodeLength={CodeLength}", fromPhone, toPhone, code.Length);
        }

        return Task.CompletedTask;
    }
}
