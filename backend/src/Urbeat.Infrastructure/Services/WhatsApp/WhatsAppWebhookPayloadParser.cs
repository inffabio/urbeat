using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Urbeat.Infrastructure.Services.WhatsApp;

/// <summary>
/// Parses the Meta WhatsApp Cloud API envelope into independent idempotent events. Meta retries
/// the whole envelope, so message ids and message statuses are the natural event keys; shapes the
/// integration does not model yet fall back to a stable hash of the raw payload and position.
/// </summary>
internal static class WhatsAppWebhookPayloadParser
{
    internal sealed record ParsedWhatsAppEvent(
        string EventKey,
        string EventType,
        string? MessageId,
        string? PhoneNumberId,
        string? SenderWaId,
        string? EventTimestamp);

    public static IReadOnlyList<ParsedWhatsAppEvent>? TryParse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawPayload);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var events = new List<ParsedWhatsAppEvent>();

            if (!document.RootElement.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return events;
            }

            var entryIndex = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                var entryId = ReadString(entry, "id") ?? "unknown";

                if (!entry.TryGetProperty("changes", out var changes)
                    || changes.ValueKind != JsonValueKind.Array)
                {
                    entryIndex++;
                    continue;
                }

                var changeIndex = 0;
                foreach (var change in changes.EnumerateArray())
                {
                    var field = ReadString(change, "field") ?? "unknown";
                    var value = TryGetProperty(change, "value");

                    string? phoneNumberId = null;
                    var addedEvent = false;

                    if (value is { } changeValue && changeValue.ValueKind == JsonValueKind.Object)
                    {
                        phoneNumberId = ReadNestedString(changeValue, "metadata", "phone_number_id");
                        addedEvent |= AddMessages(events, changeValue, rawPayload, entryIndex, changeIndex, phoneNumberId);
                        addedEvent |= AddStatuses(events, changeValue, rawPayload, entryIndex, changeIndex, phoneNumberId);
                    }

                    if (!addedEvent)
                    {
                        events.Add(new ParsedWhatsAppEvent(
                            BuildChangeEventKey(entryId, field, rawPayload, entryIndex, changeIndex),
                            field,
                            null,
                            phoneNumberId,
                            null,
                            null));
                    }

                    changeIndex++;
                }

                entryIndex++;
            }

            return events;
        }
    }

    private static bool AddMessages(
        ICollection<ParsedWhatsAppEvent> events,
        JsonElement value,
        string rawPayload,
        int entryIndex,
        int changeIndex,
        string? phoneNumberId)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var added = false;
        var messageIndex = 0;
        foreach (var message in messages.EnumerateArray())
        {
            var messageId = ReadString(message, "id");
            var type = ReadString(message, "type") ?? "unknown";
            var eventKey = !string.IsNullOrWhiteSpace(messageId)
                ? $"meta:message:{messageId}"
                : BuildFallbackEventKey("message", rawPayload, entryIndex, changeIndex, messageIndex);

            events.Add(new ParsedWhatsAppEvent(
                eventKey,
                $"message.{type}",
                messageId,
                phoneNumberId,
                ReadString(message, "from"),
                ReadString(message, "timestamp")));

            added = true;
            messageIndex++;
        }

        return added;
    }

    private static bool AddStatuses(
        ICollection<ParsedWhatsAppEvent> events,
        JsonElement value,
        string rawPayload,
        int entryIndex,
        int changeIndex,
        string? phoneNumberId)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var added = false;
        var statusIndex = 0;
        foreach (var status in statuses.EnumerateArray())
        {
            var messageId = ReadString(status, "id");
            var statusValue = ReadString(status, "status") ?? "unknown";
            var eventKey = !string.IsNullOrWhiteSpace(messageId)
                ? $"meta:status:{messageId}:{statusValue}"
                : BuildFallbackEventKey("status", rawPayload, entryIndex, changeIndex, statusIndex);

            events.Add(new ParsedWhatsAppEvent(
                eventKey,
                $"status.{statusValue}",
                messageId,
                phoneNumberId,
                ReadString(status, "recipient_id"),
                ReadString(status, "timestamp")));

            added = true;
            statusIndex++;
        }

        return added;
    }

    private static string BuildChangeEventKey(
        string entryId,
        string field,
        string rawPayload,
        int entryIndex,
        int changeIndex)
    {
        var hash = Hash(rawPayload, entryIndex, changeIndex);
        return $"meta:change:{entryId}:{field}:{hash}";
    }

    private static string BuildFallbackEventKey(
        string kind,
        string rawPayload,
        int entryIndex,
        int changeIndex,
        int itemIndex)
    {
        var hash = Hash(rawPayload, entryIndex, changeIndex, itemIndex);
        return $"meta:{kind}:{hash}";
    }

    private static string Hash(string rawPayload, params int[] indexes)
    {
        var builder = new StringBuilder(rawPayload.Length + 16);
        builder.Append(rawPayload);
        foreach (var index in indexes)
        {
            builder.Append('|').Append(index);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static JsonElement? TryGetProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
                ? value
                : null;
    }

    private static string? ReadNestedString(JsonElement element, string parentProperty, string childProperty)
    {
        var parent = TryGetProperty(element, parentProperty);
        return parent is { } parentValue && parentValue.ValueKind == JsonValueKind.Object
            ? ReadString(parentValue, childProperty)
            : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
