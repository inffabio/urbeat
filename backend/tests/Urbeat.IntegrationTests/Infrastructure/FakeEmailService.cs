using System.Collections.Concurrent;
using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

public sealed class FakeEmailService : IEmailService
{
    private readonly ConcurrentQueue<SentEmail> _messages = new();

    public IReadOnlyCollection<SentEmail> Messages => _messages.ToArray();

    public Task<string?> SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? textBody = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(new SentEmail(toAddress, toName, subject, htmlBody, textBody));
        return Task.FromResult<string?>(idempotencyKey);
    }

    public SentEmail? FindLastByRecipient(string toAddress)
    {
        return _messages.LastOrDefault(m => string.Equals(m.ToAddress, toAddress, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
        }
    }
}

public sealed record SentEmail(string ToAddress, string ToName, string Subject, string HtmlBody, string? TextBody);
