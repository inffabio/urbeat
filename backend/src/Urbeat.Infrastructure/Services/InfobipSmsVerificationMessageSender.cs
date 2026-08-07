using System.Net.Http.Headers;
using System.Net.Http.Json;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services;

public sealed class InfobipSmsVerificationMessageSender : ICustomerVerificationMessageSender
{
    private readonly HttpClient _httpClient;
    private readonly CustomerVerificationOptions _options;
    private readonly ILogger<InfobipSmsVerificationMessageSender> _logger;

    public InfobipSmsVerificationMessageSender(
        HttpClient httpClient,
        IOptions<CustomerVerificationOptions> options,
        ILogger<InfobipSmsVerificationMessageSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public CustomerVerificationChannel Channel => CustomerVerificationChannel.Sms;

    public async Task SendOtpAsync(string fromPhone, string toPhone, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Infobip.BaseUrl) || string.IsNullOrWhiteSpace(_options.Infobip.ApiKey))
        {
            throw new InvalidOperationException("Infobip SMS não configurado.");
        }

        _httpClient.BaseAddress ??= new Uri(_options.Infobip.BaseUrl.TrimEnd('/'));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("App", _options.Infobip.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            messages = new[]
            {
                new
                {
                    from = string.IsNullOrWhiteSpace(_options.Infobip.Sender) ? fromPhone : _options.Infobip.Sender,
                    destinations = new[] { new { to = NormalizeBrazilPhone(toPhone) } },
                    text = $"Seu código urbeat é {code}. Ele expira em 1 minuto."
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/sms/2/text/advanced", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Infobip SMS failed | Status={StatusCode} | Body={Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException("Não foi possível enviar o SMS de verificação.");
        }
    }

    private static string NormalizeBrazilPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.StartsWith("55", StringComparison.Ordinal) ? digits : $"55{digits}";
    }
}
