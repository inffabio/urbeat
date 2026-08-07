using System.Text.Json;

namespace Urbeat.Infrastructure.Services.Payments;

public static class MercadoPagoWebhookPayloadParser
{
    public static string? TryGetTransactionId(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        using var json = JsonDocument.Parse(rawPayload);
        var root = json.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("id", out var idElement) && idElement.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                return idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()
                    : idElement.GetRawText();
            }
        }

        if (root.TryGetProperty("id", out var fallbackId) && fallbackId.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return fallbackId.ValueKind == JsonValueKind.String
                ? fallbackId.GetString()
                : fallbackId.GetRawText();
        }

        return null;
    }
}
