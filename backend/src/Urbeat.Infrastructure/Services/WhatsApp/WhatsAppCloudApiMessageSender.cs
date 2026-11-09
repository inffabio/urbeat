using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// WhatsApp Cloud API text sender. The access token is only ever placed in the Authorization
/// header; request payloads, results and log entries never contain it.
/// </summary>
public sealed class WhatsAppCloudApiMessageSender : IWhatsAppMessageSender
{
    private const int MaxTextBodyLength = 4096;
    private const int MaxImageCaptionLength = 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly HttpClient _httpClient;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppCloudApiMessageSender> _logger;

    public WhatsAppCloudApiMessageSender(
        HttpClient httpClient,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppCloudApiMessageSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WhatsAppSendMessageResult> SendTextMessageAsync(
        WhatsAppTextMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WhatsAppPhoneNumber.TryNormalize(request.To, out var normalizedPhone))
        {
            return WhatsAppSendMessageResult.Failed("invalid_phone");
        }

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxTextBodyLength)
        {
            return WhatsAppSendMessageResult.Failed("invalid_body");
        }

        if (!_options.HasSendCredentials
            || string.IsNullOrWhiteSpace(_options.GraphApiVersion)
            || string.IsNullOrWhiteSpace(_options.GraphApiBaseUrl))
        {
            _logger.LogWarning("WhatsApp Cloud API send skipped because the integration is not configured.");
            return WhatsAppSendMessageResult.Failed("not_configured");
        }

        var url = $"{_options.GraphApiBaseUrl.TrimEnd('/')}/{_options.GraphApiVersion}/{_options.PhoneNumberId}/messages";
        var payload = JsonSerializer.Serialize(
            new CloudApiTextMessageRequest(
                "whatsapp",
                "individual",
                normalizedPhone,
                "text",
                new CloudApiText(request.Body, request.PreviewUrl)),
            JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = WhatsAppGraphApiResponse.ExtractErrorCode(responseBody)
                    ?? $"http_{(int)response.StatusCode}";
                _logger.LogWarning(
                    "WhatsApp Cloud API send failed. Status={StatusCode} ErrorCode={ErrorCode}.",
                    (int)response.StatusCode,
                    errorCode);
                return WhatsAppSendMessageResult.Failed(errorCode);
            }

            _logger.LogInformation("WhatsApp Cloud API message accepted.");
            return WhatsAppSendMessageResult.Sent(WhatsAppGraphApiResponse.ExtractMessageId(responseBody));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhatsApp Cloud API send timed out.");
            return WhatsAppSendMessageResult.Failed("timeout");
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("WhatsApp Cloud API send failed with a transport error.");
            return WhatsAppSendMessageResult.Failed("transport_error");
        }
    }

    public async Task<WhatsAppSendMessageResult> SendImageMessageAsync(
        WhatsAppImageMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WhatsAppPhoneNumber.TryNormalize(request.To, out var normalizedPhone))
        {
            return WhatsAppSendMessageResult.Failed("invalid_phone");
        }

        if (!WhatsAppMediaUrl.TryValidate(request.ImageUrl, out var imageUri))
        {
            return WhatsAppSendMessageResult.Failed("invalid_image_url");
        }

        var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption;
        if (caption is { Length: > MaxImageCaptionLength })
        {
            return WhatsAppSendMessageResult.Failed("invalid_caption");
        }

        if (!_options.HasSendCredentials
            || string.IsNullOrWhiteSpace(_options.GraphApiVersion)
            || string.IsNullOrWhiteSpace(_options.GraphApiBaseUrl))
        {
            _logger.LogWarning("WhatsApp Cloud API send skipped because the integration is not configured.");
            return WhatsAppSendMessageResult.Failed("not_configured");
        }

        var url = $"{_options.GraphApiBaseUrl.TrimEnd('/')}/{_options.GraphApiVersion}/{_options.PhoneNumberId}/messages";
        var payload = JsonSerializer.Serialize(
            new CloudApiImageMessageRequest(
                "whatsapp",
                "individual",
                normalizedPhone,
                "image",
                new CloudApiImage(imageUri.AbsoluteUri, caption)),
            JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = WhatsAppGraphApiResponse.ExtractErrorCode(responseBody)
                    ?? $"http_{(int)response.StatusCode}";
                _logger.LogWarning(
                    "WhatsApp Cloud API image send failed. Status={StatusCode} ErrorCode={ErrorCode}.",
                    (int)response.StatusCode,
                    errorCode);
                return WhatsAppSendMessageResult.Failed(errorCode);
            }

            _logger.LogInformation("WhatsApp Cloud API image message accepted.");
            return WhatsAppSendMessageResult.Sent(WhatsAppGraphApiResponse.ExtractMessageId(responseBody));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhatsApp Cloud API image send timed out.");
            return WhatsAppSendMessageResult.Failed("timeout");
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("WhatsApp Cloud API image send failed with a transport error.");
            return WhatsAppSendMessageResult.Failed("transport_error");
        }
    }

    private sealed record CloudApiTextMessageRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] CloudApiText Text);

    private sealed record CloudApiText(
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("preview_url")] bool PreviewUrl);

    private sealed record CloudApiImageMessageRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("image")] CloudApiImage Image);

    private sealed record CloudApiImage(
        [property: JsonPropertyName("link")] string Link,
        [property: JsonPropertyName("caption"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Caption);
}
