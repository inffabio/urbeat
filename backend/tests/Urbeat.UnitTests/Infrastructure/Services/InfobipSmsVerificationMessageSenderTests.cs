using System.Net;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class InfobipSmsVerificationMessageSenderTests
{
    [Fact]
    public async Task SendOtpAsync_posts_sms_with_app_authorization_and_normalized_phone()
    {
        var handler = new RecordingHttpMessageHandler();
        var sender = new InfobipSmsVerificationMessageSender(
            new HttpClient(handler),
            Options.Create(new CustomerVerificationOptions
            {
                Infobip = new InfobipSmsOptions
                {
                    BaseUrl = "https://m9zq59.api.infobip.com",
                    ApiKey = "test-api-key",
                    Sender = "Urbeat"
                }
            }),
            NullLogger<InfobipSmsVerificationMessageSender>.Instance);

        await sender.SendOtpAsync("22999990000", "22988887777", "1234", CancellationToken.None);

        handler.Request.Should().NotBeNull();
        handler.Request!.RequestUri.Should().Be("https://m9zq59.api.infobip.com/sms/2/text/advanced");
        handler.Request.Headers.Authorization?.Scheme.Should().Be("App");
        handler.Request.Headers.Authorization?.Parameter.Should().Be("test-api-key");

        using var document = JsonDocument.Parse(handler.Body!);
        var message = document.RootElement.GetProperty("messages")[0];
        message.GetProperty("from").GetString().Should().Be("Urbeat");
        message.GetProperty("destinations")[0].GetProperty("to").GetString().Should().Be("5522988887777");
        message.GetProperty("text").GetString().Should().Contain("1234");
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }
}
