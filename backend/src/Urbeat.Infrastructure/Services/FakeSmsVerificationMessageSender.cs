using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class FakeSmsVerificationMessageSender : ICustomerVerificationMessageSender
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FakeSmsVerificationMessageSender> _logger;

    public FakeSmsVerificationMessageSender(IHostEnvironment environment, ILogger<FakeSmsVerificationMessageSender> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public CustomerVerificationChannel Channel => CustomerVerificationChannel.Sms;

    public Task SendOtpAsync(string fromPhone, string toPhone, string code, CancellationToken cancellationToken)
    {
        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            _logger.LogInformation("Fake SMS OTP | From={FromPhone} | To={ToPhone} | Code={Code}", fromPhone, toPhone, code);
        }
        else
        {
            _logger.LogInformation("Fake SMS OTP | From={FromPhone} | To={ToPhone} | CodeLength={CodeLength}", fromPhone, toPhone, code.Length);
        }

        return Task.CompletedTask;
    }
}
