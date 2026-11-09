using FluentAssertions;
using Urbeat.Infrastructure.Services.WhatsApp;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppWebhookPayloadParserTests
{
    [Fact]
    public void TryParse_MessageEnvelope_ShouldCreateStableMessageEvent()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba-1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "phone_number_id": "1436364406224525" },
                    "messages": [
                      {
                        "from": "5511999999999",
                        "id": "wamid.1",
                        "timestamp": "1730000000",
                        "type": "text",
                        "text": { "body": "oi" }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var events = WhatsAppWebhookPayloadParser.TryParse(payload);

        events.Should().NotBeNull();
        events!.Should().ContainSingle();
        events[0].EventKey.Should().Be("meta:message:wamid.1");
        events[0].EventType.Should().Be("message.text");
        events[0].MessageId.Should().Be("wamid.1");
        events[0].PhoneNumberId.Should().Be("1436364406224525");
        events[0].SenderWaId.Should().Be("5511999999999");
        events[0].EventTimestamp.Should().Be("1730000000");
    }

    [Fact]
    public void TryParse_StatusEnvelope_ShouldKeyEachStatusSeparately()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba-1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "metadata": { "phone_number_id": "1436364406224525" },
                    "statuses": [
                      { "id": "wamid.1", "status": "delivered", "timestamp": "1730000001", "recipient_id": "5511999999999" },
                      { "id": "wamid.1", "status": "read", "timestamp": "1730000002", "recipient_id": "5511999999999" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var events = WhatsAppWebhookPayloadParser.TryParse(payload);

        events.Should().NotBeNull();
        events!.Select(x => x.EventKey).Should().Equal(
            "meta:status:wamid.1:delivered",
            "meta:status:wamid.1:read");
    }

    [Fact]
    public void TryParse_UnknownChange_ShouldCreateAStableFallbackKey()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            { "id": "waba-1", "changes": [ { "field": "account_update", "value": { "event": "x" } } ] }
          ]
        }
        """;

        var first = WhatsAppWebhookPayloadParser.TryParse(payload);
        var second = WhatsAppWebhookPayloadParser.TryParse(payload);

        first.Should().NotBeNull();
        first!.Should().ContainSingle();
        first[0].EventKey.Should().StartWith("meta:change:waba-1:account_update:");
        first[0].EventType.Should().Be("account_update");
        second![0].EventKey.Should().Be(first[0].EventKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{")]
    public void TryParse_InvalidOrEmptyPayload_ReturnsNull(string payload)
    {
        WhatsAppWebhookPayloadParser.TryParse(payload).Should().BeNull();
    }

    [Fact]
    public void TryParse_ValidJsonWithoutEntries_ReturnsEmptyList()
    {
        var events = WhatsAppWebhookPayloadParser.TryParse("{\"object\":\"whatsapp_business_account\"}");

        events.Should().NotBeNull();
        events!.Should().BeEmpty();
    }
}
