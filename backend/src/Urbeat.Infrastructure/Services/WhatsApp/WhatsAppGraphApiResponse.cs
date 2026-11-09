using System.Text.Json;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// Defensive readers for Meta Graph API responses. Only short, non-sensitive values (message id,
/// numeric provider error code, upload handle) are extracted; bodies are never logged verbatim.
/// </summary>
internal static class WhatsAppGraphApiResponse
{
    public static string? ExtractMessageId(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        return id.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A 2xx response without a parseable body still means Meta accepted the message.
        }

        return null;
    }

    public static string? ExtractErrorCode(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code))
            {
                var value = code.ValueKind switch
                {
                    JsonValueKind.Number => code.GetRawText(),
                    JsonValueKind.String => code.GetString(),
                    _ => null
                };

                return string.IsNullOrWhiteSpace(value) ? null : $"meta_{value}";
            }
        }
        catch (JsonException)
        {
            // Never echo an unparseable provider body; a generic status code is enough.
        }

        return null;
    }

    public static string? ExtractStringProperty(string responseBody, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }
        catch (JsonException)
        {
            // Treated as an invalid provider response by the caller.
        }

        return null;
    }

    public static bool? ExtractBooleanProperty(string responseBody, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // No boolean to report.
        }

        return null;
    }
}
