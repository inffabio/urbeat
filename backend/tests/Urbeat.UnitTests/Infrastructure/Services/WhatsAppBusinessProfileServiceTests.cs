using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Urbeat.Infrastructure.Services.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppBusinessProfileServiceTests
{
    private const string AccessToken = "unit-test-access-token";
    private const string PhoneNumberId = "1436364406224525";
    private const string MetaAppId = "987654321012345";
    private const string LogoUrl = "https://urbeat.com.br/assets/images/urbeat-logo.jpg";

    [Fact]
    public async Task UpdateProfilePicture_WithoutMetaAppId_ShouldReturnNotConfiguredAndNotCallMeta()
    {
        var handler = new ScriptedHandler();
        var logger = new RecordingLogger<WhatsAppBusinessProfileService>();
        var sut = CreateService(handler, new WhatsAppOptions
        {
            AccessToken = AccessToken,
            PhoneNumberId = PhoneNumberId,
            GraphApiVersion = WhatsAppOptions.DefaultGraphApiVersion,
            GraphApiBaseUrl = "https://graph.facebook.com",
            ProfilePictureUrl = LogoUrl
        }, logger);

        var result = await sut.UpdateProfilePictureAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("not_configured");
        handler.Requests.Should().BeEmpty();
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfilePicture_ShouldFollowResumableUploadContract()
    {
        var logoBytes = Encoding.UTF8.GetBytes("fake-jpeg-binary");
        var handler = new ScriptedHandler(
            () => ImageResponse(logoBytes),
            () => JsonResponse(HttpStatusCode.OK, """{"id":"upload:session-1"}"""),
            () => JsonResponse(HttpStatusCode.OK, """{"h":"2:handle-abc"}"""),
            () => JsonResponse(HttpStatusCode.OK, """{"success":true}"""));
        var logger = new RecordingLogger<WhatsAppBusinessProfileService>();
        var sut = CreateService(handler, CreateOptions(), logger);

        var result = await sut.UpdateProfilePictureAsync();

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        handler.Requests.Should().HaveCount(4);

        var logoRequest = handler.Requests[0];
        logoRequest.Method.Should().Be(HttpMethod.Get);
        logoRequest.Uri.Should().Be(LogoUrl);
        logoRequest.Authorization.Should().BeNull();

        var startRequest = handler.Requests[1];
        startRequest.Method.Should().Be(HttpMethod.Post);
        startRequest.Uri.Should().StartWith($"https://graph.facebook.com/{WhatsAppOptions.DefaultGraphApiVersion}/{MetaAppId}/uploads?");
        startRequest.Uri.Should().Contain($"file_length={logoBytes.Length}");
        startRequest.Uri.Should().Contain("file_type=image%2Fjpeg");
        startRequest.Uri.Should().Contain($"access_token={Uri.EscapeDataString(AccessToken)}");
        // The session is opened with the documented access_token query parameter, never a header.
        startRequest.Authorization.Should().BeNull();
        startRequest.BodyText.Should().BeEmpty();

        var uploadRequest = handler.Requests[2];
        uploadRequest.Method.Should().Be(HttpMethod.Post);
        uploadRequest.Uri.Should().Be($"https://graph.facebook.com/{WhatsAppOptions.DefaultGraphApiVersion}/upload:session-1");
        uploadRequest.Authorization!.Scheme.Should().Be("OAuth");
        uploadRequest.Authorization.Parameter.Should().Be(AccessToken);
        uploadRequest.FileOffset.Should().Be("0");
        uploadRequest.MediaType.Should().Be("image/jpeg");
        uploadRequest.BodyBytes.Should().Equal(logoBytes);

        var profileRequest = handler.Requests[3];
        profileRequest.Method.Should().Be(HttpMethod.Post);
        profileRequest.Uri.Should().Be(
            $"https://graph.facebook.com/{WhatsAppOptions.DefaultGraphApiVersion}/{PhoneNumberId}/whatsapp_business_profile");
        profileRequest.Authorization!.Scheme.Should().Be("Bearer");
        profileRequest.Authorization.Parameter.Should().Be(AccessToken);

        using var document = JsonDocument.Parse(profileRequest.BodyText);
        var root = document.RootElement;
        root.GetProperty("messaging_product").GetString().Should().Be("whatsapp");
        root.GetProperty("profile_picture_handle").GetString().Should().Be("2:handle-abc");

        handler.Requests.Select(request => request.BodyText)
            .Any(body => body.Contains(AccessToken, StringComparison.Ordinal))
            .Should().BeFalse();
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();
        logger.Messages.Any(message => message.Contains("handle-abc", StringComparison.Ordinal)).Should().BeFalse();
        logger.Messages.Any(message => message.Contains("urbeat.com.br", StringComparison.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfilePicture_WithNonHttpsLogo_ShouldNotCallMeta()
    {
        var handler = new ScriptedHandler();
        var options = new WhatsAppOptions
        {
            AccessToken = AccessToken,
            PhoneNumberId = PhoneNumberId,
            MetaAppId = MetaAppId,
            GraphApiVersion = WhatsAppOptions.DefaultGraphApiVersion,
            GraphApiBaseUrl = "https://graph.facebook.com",
            ProfilePictureUrl = "http://localhost/urbeat-logo.jpg"
        };
        var sut = CreateService(handler, options);

        var result = await sut.UpdateProfilePictureAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_profile_picture_url");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateProfilePicture_WhenProviderRejectsUploadStart_ShouldReturnSanitizedFailure()
    {
        var handler = new ScriptedHandler(
            () => ImageResponse(Encoding.UTF8.GetBytes("fake-jpeg-binary")),
            () => JsonResponse(
                HttpStatusCode.BadRequest,
                """{"error":{"message":"Invalid OAuth access token","type":"OAuthException","code":190}}"""));
        var logger = new RecordingLogger<WhatsAppBusinessProfileService>();
        var sut = CreateService(handler, CreateOptions(), logger);

        var result = await sut.UpdateProfilePictureAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("upload_start_failed");
        handler.Requests.Should().HaveCount(2);
        logger.Messages.Should().Contain(message => message.Contains("meta_190", StringComparison.Ordinal));
        logger.Messages.Any(message => message.Contains(AccessToken, StringComparison.Ordinal)).Should().BeFalse();
        logger.Messages.Any(message => message.Contains("Invalid OAuth access token", StringComparison.Ordinal))
            .Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfilePicture_WhenLogoContentTypeIsUnsupported_ShouldNotStartUpload()
    {
        var handler = new ScriptedHandler(() =>
        {
            var content = new StringContent("<html>not an image</html>");
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var sut = CreateService(handler, CreateOptions());

        var result = await sut.UpdateProfilePictureAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("logo_unavailable");
        handler.Requests.Should().ContainSingle();
    }

    private static WhatsAppOptions CreateOptions() => new()
    {
        AccessToken = AccessToken,
        PhoneNumberId = PhoneNumberId,
        MetaAppId = MetaAppId,
        GraphApiVersion = WhatsAppOptions.DefaultGraphApiVersion,
        GraphApiBaseUrl = "https://graph.facebook.com",
        ProfilePictureUrl = LogoUrl,
        TimeoutSeconds = 15
    };

    private static WhatsAppBusinessProfileService CreateService(
        ScriptedHandler handler,
        WhatsAppOptions options,
        RecordingLogger<WhatsAppBusinessProfileService>? logger = null)
    {
        return new WhatsAppBusinessProfileService(
            new FakeHttpClientFactory(new HttpClient(handler)),
            Options.Create(options),
            logger ?? Mock.Of<ILogger<WhatsAppBusinessProfileService>>());
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
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

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization is null
                    ? null
                    : new RecordedAuthorization(
                        request.Headers.Authorization.Scheme,
                        request.Headers.Authorization.Parameter),
                request.Headers.TryGetValues("file_offset", out var offsets)
                    ? offsets.FirstOrDefault()
                    : null,
                request.Content?.Headers.ContentType?.MediaType,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected HTTP call.");
            }

            return _responses.Dequeue()();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        RecordedAuthorization? Authorization,
        string? FileOffset,
        string? MediaType,
        byte[] BodyBytes)
    {
        public string BodyText => Encoding.UTF8.GetString(BodyBytes);
    }

    private sealed record RecordedAuthorization(string Scheme, string? Parameter);
}
