using System.Text.Json;
using System.Text.Json.Serialization;

namespace Urbeat.Infrastructure.Outbox;

public static class OutboxPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"Outbox payload could not be deserialized to {typeof(T).Name}.");
    }
}
