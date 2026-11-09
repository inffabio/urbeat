using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class WhatsAppWebhookServiceTests : IDisposable
{
    private const string PhoneNumberId = "1436364406224525";

    private readonly ApplicationDbContext _db;
    private readonly WhatsAppWebhookService _sut;

    public WhatsAppWebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-whatsapp-webhook-{Guid.NewGuid():N}")
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new WhatsAppWebhookService(
            _db,
            new EfUnitOfWork(_db),
            Options.Create(new WhatsAppOptions { PhoneNumberId = PhoneNumberId }),
            Mock.Of<ILogger<WhatsAppWebhookService>>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_MessagePayload_ShouldPersistOneReceivedEvent()
    {
        var result = await _sut.ProcessAsync(MessagePayload("wamid.unit.1"));

        result.Accepted.Should().BeTrue();
        result.InvalidPayload.Should().BeFalse();
        result.RecordedEvents.Should().Be(1);
        result.DuplicateEvents.Should().Be(0);

        var stored = await _db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync();
        stored.Provider.Should().Be(WhatsAppWebhookEvent.MetaProvider);
        stored.EventKey.Should().Be("meta:message:wamid.unit.1");
        stored.EventType.Should().Be("message.text");
        stored.MessageId.Should().Be("wamid.unit.1");
        stored.PhoneNumberId.Should().Be("1436364406224525");
        stored.SenderWaId.Should().Be("5511999999999");
        stored.Status.Should().Be(WhatsAppWebhookEventStatus.Received);
        stored.Payload.Should().Be(MessagePayload("wamid.unit.1"));
    }

    [Fact]
    public async Task ProcessAsync_RepeatedPayload_ShouldBeIdempotent()
    {
        var first = await _sut.ProcessAsync(MessagePayload("wamid.unit.2"));
        var second = await _sut.ProcessAsync(MessagePayload("wamid.unit.2"));

        first.RecordedEvents.Should().Be(1);
        second.RecordedEvents.Should().Be(0);
        second.DuplicateEvents.Should().Be(1);
        (await _db.WhatsAppWebhookEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldDeduplicateEventsInsideTheSameEnvelope()
    {
        var result = await _sut.ProcessAsync(MultipleMessagesPayload("wamid.unit.3", "wamid.unit.3"));

        result.RecordedEvents.Should().Be(1);
        result.DuplicateEvents.Should().Be(1);
        (await _db.WhatsAppWebhookEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPersistMessagesAndStatusesIndependently()
    {
        var result = await _sut.ProcessAsync(MixedPayload("wamid.unit.4"));

        result.RecordedEvents.Should().Be(2);
        (await _db.WhatsAppWebhookEvents.CountAsync()).Should().Be(2);
        (await _db.WhatsAppWebhookEvents.CountAsync(x => x.EventType == "status.delivered")).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{")]
    public async Task ProcessAsync_InvalidPayload_ShouldNotPersist(string payload)
    {
        var result = await _sut.ProcessAsync(payload);

        result.Accepted.Should().BeFalse();
        result.InvalidPayload.Should().BeTrue();
        (await _db.WhatsAppWebhookEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_EventFromAnotherPhoneNumberId_ShouldBeAcknowledgedAndIgnored()
    {
        var result = await _sut.ProcessAsync(
            MessagePayload("wamid.unit.foreign", phoneNumberId: "999999999999999"));

        result.Accepted.Should().BeTrue();
        result.InvalidPayload.Should().BeFalse();
        result.RecordedEvents.Should().Be(0);
        result.IgnoredEvents.Should().Be(1);
        (await _db.WhatsAppWebhookEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_EventWithoutPhoneNumberMetadata_ShouldKeepWorking()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            { "id": "waba-1", "changes": [ { "field": "account_update", "value": { "event": "x" } } ] }
          ]
        }
        """;

        var result = await _sut.ProcessAsync(payload);

        result.RecordedEvents.Should().Be(1);
        result.IgnoredEvents.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_UnknownChange_ShouldStillBeRecorded()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            { "id": "waba-1", "changes": [ { "field": "account_update", "value": { "event": "x" } } ] }
          ]
        }
        """;

        var result = await _sut.ProcessAsync(payload);

        result.RecordedEvents.Should().Be(1);
        var stored = await _db.WhatsAppWebhookEvents.AsNoTracking().SingleAsync();
        stored.EventType.Should().Be("account_update");
        stored.EventKey.Should().StartWith("meta:change:waba-1:account_update:");
    }

    private static string MessagePayload(string messageId, string phoneNumberId = PhoneNumberId) => $$"""
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
                "metadata": { "phone_number_id": "{{phoneNumberId}}" },
                "messages": [
                  { "from": "5511999999999", "id": "{{messageId}}", "timestamp": "1730000000", "type": "text", "text": { "body": "oi" } }
                ]
              }
            }
          ]
        }
      ]
    }
    """;

    private static string MultipleMessagesPayload(string firstId, string secondId) => $$"""
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
                "messages": [
                  { "from": "5511999999999", "id": "{{firstId}}", "timestamp": "1730000000", "type": "text", "text": { "body": "a" } },
                  { "from": "5511999999999", "id": "{{secondId}}", "timestamp": "1730000000", "type": "text", "text": { "body": "b" } }
                ]
              }
            }
          ]
        }
      ]
    }
    """;

    private static string MixedPayload(string messageId) => $$"""
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
                "messages": [
                  { "from": "5511999999999", "id": "{{messageId}}", "timestamp": "1730000000", "type": "text", "text": { "body": "a" } }
                ],
                "statuses": [
                  { "id": "{{messageId}}", "status": "delivered", "timestamp": "1730000001", "recipient_id": "5511999999999" }
                ]
              }
            }
          ]
        }
      ]
    }
    """;
}
