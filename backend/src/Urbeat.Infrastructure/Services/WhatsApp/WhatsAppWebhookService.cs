using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Urbeat.Infrastructure.Services.WhatsApp;

public sealed class WhatsAppWebhookService : IWhatsAppWebhookService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppWebhookService> _logger;

    public WhatsAppWebhookService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppWebhookService> logger)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProcessWhatsAppWebhookResultDto> ProcessAsync(
        string rawPayload,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var parsedEvents = WhatsAppWebhookPayloadParser.TryParse(rawPayload);
        if (parsedEvents is null)
        {
            return new ProcessWhatsAppWebhookResultDto { InvalidPayload = true };
        }

        var recordedEvents = 0;
        var duplicateEvents = 0;
        var ignoredEvents = 0;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var expectedPhoneNumberId = _options.PhoneNumberId;

        foreach (var parsedEvent in parsedEvents)
        {
            // When the application knows its own Phone Number ID, events from any other number are
            // acknowledged but never persisted. Missing metadata is left to the signature-verified
            // flows (Meta signs the whole envelope) so account-level events keep working.
            if (!string.IsNullOrWhiteSpace(expectedPhoneNumberId)
                && !string.IsNullOrWhiteSpace(parsedEvent.PhoneNumberId)
                && !string.Equals(parsedEvent.PhoneNumberId, expectedPhoneNumberId, StringComparison.Ordinal))
            {
                ignoredEvents++;
                continue;
            }

            if (!seenKeys.Add(parsedEvent.EventKey))
            {
                duplicateEvents++;
                continue;
            }

            var alreadyStored = await _dbContext.WhatsAppWebhookEvents
                .AsNoTracking()
                .AnyAsync(
                    x => x.Provider == WhatsAppWebhookEvent.MetaProvider && x.EventKey == parsedEvent.EventKey,
                    cancellationToken);

            if (alreadyStored)
            {
                duplicateEvents++;
                continue;
            }

            var entity = new WhatsAppWebhookEvent
            {
                Provider = WhatsAppWebhookEvent.MetaProvider,
                EventKey = parsedEvent.EventKey,
                EventType = parsedEvent.EventType,
                MessageId = parsedEvent.MessageId,
                PhoneNumberId = parsedEvent.PhoneNumberId,
                SenderWaId = parsedEvent.SenderWaId,
                EventTimestamp = parsedEvent.EventTimestamp,
                Payload = rawPayload,
                Status = WhatsAppWebhookEventStatus.Received,
                ReceivedAtUtc = DateTime.UtcNow
            };

            _dbContext.WhatsAppWebhookEvents.Add(entity);

            try
            {
                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                recordedEvents++;
            }
            catch (DbUpdateException exception) when (IsDuplicateEvent(exception))
            {
                // A concurrent delivery persisted the same (Provider, EventKey) first. The unique
                // index is authoritative; the losing request is still an idempotent duplicate.
                _dbContext.Entry(entity).State = EntityState.Detached;
                duplicateEvents++;
            }
        }

        _logger.LogInformation(
            "WhatsApp webhook ingested. Recorded={RecordedEvents} Duplicates={DuplicateEvents} Ignored={IgnoredEvents}.",
            recordedEvents,
            duplicateEvents,
            ignoredEvents);

        return new ProcessWhatsAppWebhookResultDto
        {
            Accepted = true,
            RecordedEvents = recordedEvents,
            DuplicateEvents = duplicateEvents,
            IgnoredEvents = ignoredEvents
        };
    }

    private static bool IsDuplicateEvent(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException
            && postgresException.MessageText.Contains("WhatsAppWebhookEvents", StringComparison.OrdinalIgnoreCase);
    }
}
