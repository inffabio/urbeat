using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Infrastructure.Services.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppCloudApiMessageSenderTests
{
    private const string AccessToken = "unit-test-access-token";
    private const string PhoneNumberId = "1436364406224525";

    [Fact]
    public async Task SendTextMessage_ShouldUseBearerHeaderAndKeepTokenOutOfTheBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.out.1"}]}""");
        var logger = new RecordingLogger<WhatsAppCloudApiMessageSender>();
        var sut = CreateSender(handler, logger: logger);

        var result = await sut.SendTextMessageAsync(
            new WhatsAppTextMessageRequest("+5511999999999", "Olá", PreviewUrl: true));

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("wamid.out.1");

        handler.CallCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should()
            .Be($"https://graph.facebook.com/{WhatsAppOptions.DefaultGraphApiVersion}/{PhoneNumberId}/messages");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(AccessToken);

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().NotContain(AccessToken);
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var root = document.RootElement;
        root.GetProperty("messaging_product").GetString().Should().Be("whatsapp");
        root.GetProperty("recipient_type").GetString().Should().Be("individual");
        root.GetProperty("type").GetString().Should().Be("text");
        root.GetProperty("to").GetString().Should().Be("5511999999999");
        root.GetProperty("text").GetProperty("body").GetString().Should().Be("Olá");
        root.GetProperty("text").GetProperty("preview_url").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SendTextMessage_WithInvalidPhone_ShouldNotCallTheApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler);

        var result = await sut.SendTextMessageAsync(new WhatsAppTextMessageRequest("0123", "Oi"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_phone");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendTextMessage_WithoutCredentials_ShouldNotCallTheApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler, new WhatsAppOptions
        {
            PhoneNumberId = PhoneNumberId,
            GraphApiVersion = WhatsAppOptions.DefaultGraphApiVersion
        });

        var result = await sut.SendTextMessageAsync(new WhatsAppTextMessageRequest("5511999999999", "Oi"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("not_configured");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendTextMessage_WithOverlongBody_ShouldNotCallTheApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler);

        var result = await sut.SendTextMessageAsync(
            new WhatsAppTextMessageRequest("5511999999999", new string('a', 4097)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_body");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendTextMessage_WhenProviderRejects_ShouldReturnSanitizedErrorCode()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"Invalid parameter","type":"OAuthException","code":131026}}""");
        var logger = new RecordingLogger<WhatsAppCloudApiMessageSender>();
        var sut = CreateSender(handler, logger: logger);

        var result = await sut.SendTextMessageAsync(new WhatsAppTextMessageRequest("5511999999999", "Oi"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("meta_131026");
        result.MessageId.Should().BeNull();
        result.ErrorCode.Should().NotContain(AccessToken);
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public async Task SendTextMessage_WhenProviderReturnsHtmlError_ShouldFallBackToStatus()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>");
        var sut = CreateSender(handler);

        var result = await sut.SendTextMessageAsync(new WhatsAppTextMessageRequest("5511999999999", "Oi"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("http_502");
    }

    [Fact]
    public async Task SendImageMessage_ShouldPostImagePayloadAndKeepTokenOutOfTheBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.img.1"}]}""");
        var logger = new RecordingLogger<WhatsAppCloudApiMessageSender>();
        var sut = CreateSender(handler, logger: logger);
        const string logoUrl = "https://cdn.example.com/stores/logo.jpg?token=private";

        var result = await sut.SendImageMessageAsync(
            new WhatsAppImageMessageRequest("+5511999999999", logoUrl, "Bem-vindo!"));

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("wamid.img.1");

        handler.CallCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should()
            .Be($"https://graph.facebook.com/{WhatsAppOptions.DefaultGraphApiVersion}/{PhoneNumberId}/messages");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(AccessToken);

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().NotContain(AccessToken);
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();
        logger.Messages.Any(message => message.Contains("cdn.example.com", StringComparison.Ordinal)).Should().BeFalse();

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var root = document.RootElement;
        root.GetProperty("messaging_product").GetString().Should().Be("whatsapp");
        root.GetProperty("recipient_type").GetString().Should().Be("individual");
        root.GetProperty("type").GetString().Should().Be("image");
        root.GetProperty("to").GetString().Should().Be("5511999999999");
        root.GetProperty("image").GetProperty("link").GetString().Should().Be(logoUrl);
        root.GetProperty("image").GetProperty("caption").GetString().Should().Be("Bem-vindo!");
    }

    [Fact]
    public async Task SendImageMessage_WithoutCaption_ShouldOmitCaption()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messages":[{"id":"wamid.img.2"}]}""");
        var sut = CreateSender(handler);

        var result = await sut.SendImageMessageAsync(
            new WhatsAppImageMessageRequest("5511999999999", "https://cdn.example.com/logo.png"));

        result.Success.Should().BeTrue();

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var image = document.RootElement.GetProperty("image");
        image.GetProperty("link").GetString().Should().Be("https://cdn.example.com/logo.png");
        image.TryGetProperty("caption", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://cdn.example.com/logo.jpg")]
    [InlineData("https://localhost/logo.jpg")]
    [InlineData("https://api.localhost/logo.jpg")]
    [InlineData("https://127.0.0.1/logo.jpg")]
    [InlineData("https://192.168.0.10/logo.jpg")]
    [InlineData("https://user:secret@cdn.example.com/logo.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///C:/logo.jpg")]
    public async Task SendImageMessage_WithUnsafeUrl_ShouldNotCallTheApi(string imageUrl)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler);

        var result = await sut.SendImageMessageAsync(
            new WhatsAppImageMessageRequest("5511999999999", imageUrl, "Oi"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_image_url");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendImageMessage_WithInvalidPhone_ShouldNotCallTheApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler);

        var result = await sut.SendImageMessageAsync(
            new WhatsAppImageMessageRequest("0123", "https://cdn.example.com/logo.jpg"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_phone");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendImageMessage_WithOverlongCaption_ShouldNotCallTheApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSender(handler);

        var result = await sut.SendImageMessageAsync(
            new WhatsAppImageMessageRequest(
                "5511999999999",
                "https://cdn.example.com/logo.jpg",
                new string('a', 1025)));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_caption");
        handler.CallCount.Should().Be(0);
    }

    private static WhatsAppCloudApiMessageSender CreateSender(
        RecordingHandler handler,
        WhatsAppOptions? options = null,
        RecordingLogger<WhatsAppCloudApiMessageSender>? logger = null)
    {
        options ??= new WhatsAppOptions
        {
            AccessToken = AccessToken,
            PhoneNumberId = PhoneNumberId,
            GraphApiVersion = WhatsAppOptions.DefaultGraphApiVersion,
            GraphApiBaseUrl = "https://graph.facebook.com"
        };

        return new WhatsAppCloudApiMessageSender(
            new HttpClient(handler),
            Options.Create(options),
            logger ?? Mock.Of<ILogger<WhatsAppCloudApiMessageSender>>());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
