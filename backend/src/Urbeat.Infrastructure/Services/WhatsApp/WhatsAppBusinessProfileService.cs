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
/// Applies the institutional urbeat logo to the WhatsApp business profile using the Meta
/// Resumable Upload API:
/// <list type="number">
/// <item>POST /{AppId}/uploads with file_name/file_length/file_type and the documented
/// <c>access_token</c> query parameter to start an upload session.</item>
/// <item>POST /upload:{session} with the binary body (Authorization: OAuth + file_offset header).</item>
/// <item>POST /{PhoneNumberId}/whatsapp_business_profile with the returned handle (Bearer token).</item>
/// </list>
/// The operation is idempotent and never runs automatically. Only short error codes and HTTP
/// statuses are logged; tokens, URLs with query strings and upload handles are never logged.
/// </summary>
public sealed class WhatsAppBusinessProfileService : IWhatsAppBusinessProfileService
{
    private const long MaxLogoBytes = 5 * 1024 * 1024;
    private const int BufferSize = 81920;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, string> SupportedLogoTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "urbeat-logo.jpg",
            ["image/jpg"] = "urbeat-logo.jpg",
            ["image/png"] = "urbeat-logo.png"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppBusinessProfileService> _logger;

    public WhatsAppBusinessProfileService(
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppBusinessProfileService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WhatsAppProfilePictureUpdateResult> UpdateProfilePictureAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.HasProfileUpdateCredentials)
        {
            _logger.LogWarning(
                "WhatsApp profile picture update skipped because MetaAppId or send credentials are not configured.");
            return WhatsAppProfilePictureUpdateResult.Failed("not_configured");
        }

        if (string.IsNullOrWhiteSpace(_options.ProfilePictureUrl))
        {
            _logger.LogWarning("WhatsApp profile picture update skipped because no picture URL is configured.");
            return WhatsAppProfilePictureUpdateResult.Failed("not_configured");
        }

        if (!WhatsAppMediaUrl.TryValidate(_options.ProfilePictureUrl, out var logoUri))
        {
            _logger.LogWarning("WhatsApp profile picture update skipped because the picture URL is not a public https URL.");
            return WhatsAppProfilePictureUpdateResult.Failed("invalid_profile_picture_url");
        }

        if (string.IsNullOrWhiteSpace(_options.GraphApiVersion) || string.IsNullOrWhiteSpace(_options.GraphApiBaseUrl))
        {
            return WhatsAppProfilePictureUpdateResult.Failed("not_configured");
        }

        try
        {
            var logo = await DownloadLogoAsync(logoUri, cancellationToken);
            if (logo is null)
            {
                return WhatsAppProfilePictureUpdateResult.Failed("logo_unavailable");
            }

            var graphClient = _httpClientFactory.CreateClient(WhatsAppHttpClients.Graph);

            var uploadId = await StartUploadSessionAsync(graphClient, logo, cancellationToken);
            if (uploadId is null)
            {
                return WhatsAppProfilePictureUpdateResult.Failed("upload_start_failed");
            }

            var handle = await UploadLogoAsync(graphClient, uploadId, logo, cancellationToken);
            if (handle is null)
            {
                return WhatsAppProfilePictureUpdateResult.Failed("upload_failed");
            }

            return await ApplyProfilePictureAsync(graphClient, handle, cancellationToken)
                ? WhatsAppProfilePictureUpdateResult.Updated()
                : WhatsAppProfilePictureUpdateResult.Failed("profile_update_failed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("WhatsApp profile picture update timed out.");
            return WhatsAppProfilePictureUpdateResult.Failed("timeout");
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("WhatsApp profile picture update failed with a transport error.");
            return WhatsAppProfilePictureUpdateResult.Failed("transport_error");
        }
    }

    private async Task<LogoPayload?> DownloadLogoAsync(Uri logoUri, CancellationToken cancellationToken)
    {
        var imageClient = _httpClientFactory.CreateClient(WhatsAppHttpClients.ProfileImage);

        using var response = await imageClient.GetAsync(
            logoUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "WhatsApp profile logo download failed. Status={StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        if (response.Content.Headers.ContentLength > MaxLogoBytes)
        {
            _logger.LogWarning("WhatsApp profile logo rejected because it exceeds the size limit.");
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !SupportedLogoTypes.TryGetValue(mediaType, out var fileName))
        {
            _logger.LogWarning("WhatsApp profile logo rejected because its content type is not supported.");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        var total = 0;

        int read;
        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxLogoBytes)
            {
                _logger.LogWarning("WhatsApp profile logo rejected because it exceeds the size limit.");
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        if (total == 0)
        {
            _logger.LogWarning("WhatsApp profile logo rejected because it is empty.");
            return null;
        }

        return new LogoPayload(buffer.ToArray(), mediaType, fileName);
    }

    private async Task<string?> StartUploadSessionAsync(
        HttpClient client,
        LogoPayload logo,
        CancellationToken cancellationToken)
    {
        // Meta's Resumable Upload guide starts the session with the token in the `access_token`
        // query parameter (and file_name/file_length/file_type); the binary upload below uses the
        // `Authorization: OAuth` header and the profile update uses `Authorization: Bearer`.
        // The Graph client disables request logging so this URL is never written to logs.
        var url = $"{BuildGraphUrl($"{_options.MetaAppId}/uploads")}"
            + $"?file_name={Uri.EscapeDataString(logo.FileName)}"
            + $"&file_length={logo.Content.Length}"
            + $"&file_type={Uri.EscapeDataString(logo.MediaType)}"
            + $"&access_token={Uri.EscapeDataString(_options.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("upload_start", response, body);
            return null;
        }

        var uploadId = WhatsAppGraphApiResponse.ExtractStringProperty(body, "id");
        if (!IsSafeUploadId(uploadId))
        {
            _logger.LogWarning("WhatsApp profile upload start returned an invalid session id.");
            return null;
        }

        return uploadId;
    }

    private async Task<string?> UploadLogoAsync(
        HttpClient client,
        string uploadId,
        LogoPayload logo,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(logo.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue(logo.MediaType);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl(uploadId))
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _options.AccessToken);
        request.Headers.TryAddWithoutValidation("file_offset", "0");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("upload", response, body);
            return null;
        }

        var handle = WhatsAppGraphApiResponse.ExtractStringProperty(body, "h");
        if (!IsSafeHandle(handle))
        {
            _logger.LogWarning("WhatsApp profile upload returned an invalid handle.");
            return null;
        }

        return handle;
    }

    private async Task<bool> ApplyProfilePictureAsync(
        HttpClient client,
        string handle,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new CloudApiProfileUpdateRequest("whatsapp", handle),
            JsonOptions);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildGraphUrl($"{_options.PhoneNumberId}/whatsapp_business_profile"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogProviderFailure("profile_update", response, body);
            return false;
        }

        var success = WhatsAppGraphApiResponse.ExtractBooleanProperty(body, "success");
        if (success == false)
        {
            _logger.LogWarning("WhatsApp profile update was not acknowledged by Meta.");
            return false;
        }

        _logger.LogInformation("WhatsApp business profile picture updated.");
        return true;
    }

    private string BuildGraphUrl(string relativePath)
        => $"{_options.GraphApiBaseUrl.TrimEnd('/')}/{_options.GraphApiVersion}/{relativePath}";

    private void LogProviderFailure(string step, HttpResponseMessage response, string body)
    {
        var errorCode = WhatsAppGraphApiResponse.ExtractErrorCode(body)
            ?? $"http_{(int)response.StatusCode}";
        _logger.LogWarning(
            "WhatsApp profile picture update failed. Step={Step} Status={StatusCode} ErrorCode={ErrorCode}.",
            step,
            (int)response.StatusCode,
            errorCode);
    }

    private static bool IsSafeUploadId(string? uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId)
            || uploadId.Length > 256
            || !uploadId.StartsWith("upload:", StringComparison.Ordinal))
        {
            return false;
        }

        return uploadId.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is ':' or '_' or '-');
    }

    private static bool IsSafeHandle(string? handle)
    {
        return !string.IsNullOrWhiteSpace(handle)
            && handle.Length <= 4096
            && !handle.Any(char.IsControl);
    }

    private sealed record LogoPayload(byte[] Content, string MediaType, string FileName);

    private sealed record CloudApiProfileUpdateRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("profile_picture_handle")] string ProfilePictureHandle);
}
