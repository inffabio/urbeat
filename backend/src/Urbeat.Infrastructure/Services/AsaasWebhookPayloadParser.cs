using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Urbeat.Infrastructure.Services;

internal static class AsaasWebhookPayloadParser
{
    internal sealed record ParsedAsaasWebhook(
        string EventKey,
        string EventType,
        Guid? SellerUserId,
        string? PaymentId,
        string? ExternalReference,
        DateTime? DueDateUtc,
        DateTime? PaidAtUtc,
        decimal? Amount,
        string? BillingStatusRaw);

    public static ParsedAsaasWebhook? TryParse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(rawPayload);
        var root = doc.RootElement;

        var eventId = ReadString(root, "id");
        var eventType = ReadString(root, "event") ?? "unknown";

        var payment = TryGetProperty(root, "payment");
        var externalReference = ReadString(payment, "externalReference")
            ?? ReadString(root, "externalReference");

        var sellerUserId = TryParseGuid(ReadString(root, "sellerUserId"))
            ?? TryParseGuid(externalReference);

        var billingStatusRaw = ReadString(payment, "status")
            ?? ReadString(root, "status");

        var dueDateUtc = TryParseDueDate(ReadString(payment, "dueDate") ?? ReadString(root, "dueDate"));
        var paidAtUtc = TryParseDateTime(ReadString(payment, "clientPaymentDate")
            ?? ReadString(payment, "paymentDate")
            ?? ReadString(root, "clientPaymentDate")
            ?? ReadString(root, "paymentDate"));

        var amount = TryParseDecimal(ReadString(payment, "value")
            ?? ReadString(payment, "netValue")
            ?? ReadString(root, "value"));

        var paymentId = ReadString(payment, "id") ?? ReadString(root, "paymentId");
        var eventKey = BuildEventKey(eventId, eventType, paymentId, externalReference, billingStatusRaw, rawPayload);

        return new ParsedAsaasWebhook(
            eventKey,
            eventType,
            sellerUserId,
            paymentId,
            externalReference,
            dueDateUtc,
            paidAtUtc,
            amount,
            billingStatusRaw);
    }

    private static string BuildEventKey(
        string? eventId,
        string eventType,
        string? paymentId,
        string? externalReference,
        string? billingStatus,
        string rawPayload)
    {
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            return $"asaas:{eventId}";
        }

        var stableComposite = $"{eventType}:{paymentId}:{externalReference}:{billingStatus}";
        if (!string.IsNullOrWhiteSpace(paymentId) || !string.IsNullOrWhiteSpace(externalReference))
        {
            return $"asaas:{stableComposite}";
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
        var hash = Convert.ToHexString(hashBytes);
        return $"asaas:raw:{hash}";
    }

    private static JsonElement? TryGetProperty(JsonElement source, string propertyName)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return source.TryGetProperty(propertyName, out var property) ? property : null;
    }

    private static string? ReadString(JsonElement? source, string propertyName)
    {
        if (source is null || source.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!source.Value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static Guid? TryParseGuid(string? input)
    {
        return Guid.TryParse(input, out var guid) ? guid : null;
    }

    private static DateTime? TryParseDueDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static DateTime? TryParseDateTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static decimal? TryParseDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}