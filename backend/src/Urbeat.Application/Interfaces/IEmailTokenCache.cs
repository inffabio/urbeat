
namespace Urbeat.Application.Interfaces;

public class EmailTokenData
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// The full confirmation request material, keyed by a correlation id, that the outbox e-mail handler
/// resolves at send time. This is deliberately kept out of the durable outbox payload: the payload
/// carries only <c>CorrelationId</c>, never the short code or the raw token.
/// </summary>
public sealed class EmailConfirmationRequest
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public string ShortCode { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public interface IEmailTokenCache
{
    Task SetMappingAsync(string code, Guid userId, string token, CancellationToken ct = default);
    Task<EmailTokenData?> GetMappingAsync(string code, CancellationToken ct = default);

    Task SetConfirmationRequestAsync(EmailConfirmationRequest request, CancellationToken ct = default);
    Task<EmailConfirmationRequest?> GetConfirmationRequestAsync(Guid correlationId, CancellationToken ct = default);
}
