
namespace Urbeat.Application.Interfaces;

public class EmailTokenData
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
}

public interface IEmailTokenCache
{
    Task SetMappingAsync(string code, Guid userId, string token, CancellationToken ct = default);
    Task<EmailTokenData?> GetMappingAsync(string code, CancellationToken ct = default);
}
